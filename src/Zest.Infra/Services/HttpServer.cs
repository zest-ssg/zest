using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Abstract base class for HTTP servers (development server and preview server).
/// Encapsulates common HTTP handling: listener lifecycle, CORS, 404/500, request logging,
/// path traversal protection, ETag caching, and statistics tracking.
/// </summary>
public abstract class HttpServer : IDisposable
{
    protected string Host { get; }
    protected bool OpenBrowser { get; }
    protected bool EnableSpaFallback { get; set; }
    protected bool EnableDirectoryListing { get; set; }
    protected HttpListener? Listener { get; set; }
    protected CancellationTokenSource? Cts { get; set; }
    private long _totalRequests;
    private long _cacheHits;
    private long _totalBytesServed;
    protected long TotalRequests => _totalRequests;
    protected long CacheHits => _cacheHits;
    protected long TotalBytesServed => _totalBytesServed;

    protected HttpServer(string host = "localhost", bool openBrowser = false)
    {
        Host = host;
        OpenBrowser = openBrowser;
    }

    /// <summary>Display name for the server (used in logs and banner).</summary>
    protected abstract string ServerName { get; }

    /// <summary>The port the server listens on.</summary>
    protected abstract int Port { get; }

    /// <summary>Resolve the output/content directory for serving files.</summary>
    protected abstract string GetOutputDir();

    /// <summary>
    /// Optional hook for handling special file types (e.g. .zcss compilation,
    /// live-reload script injection into HTML).
    /// Return true if the request was fully handled; false to fall through to default file serving.
    /// </summary>
    protected virtual Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
        => Task.FromResult(false);

    /// <summary>
    /// Optional hook for providing a live-reload script snippet to inject into HTML pages.
    /// Return null if no injection is needed.
    /// </summary>
    protected virtual string? GetLiveReloadScript() => null;

    /// <summary>
    /// Optional hook for handling virtual paths (e.g. SSE endpoints).
    /// Return true if the request was fully handled; false to fall through to normal file serving.
    /// </summary>
    protected virtual Task<bool> TryHandleVirtualPath(HttpListenerContext ctx, string urlPath)
        => Task.FromResult(false);

    public void Start()
    {
        Cts = new CancellationTokenSource();
        Listener = new HttpListener();
        Listener.Prefixes.Add($"http://{Host}:{Port}/");
        Listener.Start();
        _ = Task.Run(() => ServeHttp(Cts.Token));

        OnStarted();

        var outputDir = GetOutputDir();

        LogWriter.Banner(
            $"Zest {ServerName} Server",
            $"http://{Host}:{Port}/",
            ("Host", Host),
            ("Port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("Root", outputDir),
            ("Verbose", LogWriter.Verbose ? "ON" : "off")
        );

        TryOpenBrowser();

        LogWriter.WriteDim("  Press Ctrl+C to stop.");
    }

    /// <summary>Called after the HTTP listener starts. Override for additional setup (build, file watching, etc.).</summary>
    protected virtual void OnStarted() { }

    public virtual void Shutdown()
    {
        Cts?.Cancel();
        Listener?.Stop();

        LogWriter.Info($"Total requests: {TotalRequests}, cache hits: {CacheHits}, bytes served: {TotalBytesServed:N0}");
    }

    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    private async Task ServeHttp(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && Listener!.IsListening)
        {
            try
            {
                var ctx = await Listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(ctx), CancellationToken.None);
            }
            catch { break; }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        var sw = Stopwatch.StartNew();
        var urlPath = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;
        var bytesBefore = Interlocked.Read(ref _totalBytesServed);
        var isCacheHit = false;

        try
        {
            // Handle OPTIONS preflight
            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                HttpHelper.AddCorsHeaders(ctx.Response);
                ctx.Response.OutputStream.Close();
                sw.Stop();
                LogWriter.Request(method, urlPath, 204, sw.ElapsedMilliseconds);
                return;
            }

            // Only allow GET and HEAD
            if (method != "GET" && method != "HEAD")
            {
                await HttpHelper.WriteStringResponse(ctx, 405, "<h1>405 — Method Not Allowed</h1>");
                ctx.Response.Headers["Allow"] = "GET, HEAD, OPTIONS";
                sw.Stop();
                LogWriter.Request(method, urlPath, 405, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = GetOutputDir();

            // Check virtual paths (SSE endpoints, etc.)
            if (await TryHandleVirtualPath(ctx, urlPath))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            string filePath;
            try
            {
                filePath = PathMapper.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                await HttpHelper.WriteStringResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                sw.Stop();
                LogWriter.Request(method, urlPath, 403, sw.ElapsedMilliseconds);
                LogWriter.Warn("Security", $"Path traversal blocked: {urlPath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                // Directory listing: show file list if enabled and path maps to a directory
                if (EnableDirectoryListing)
                {
                    var dirCheckPath = PathMapper.ResolveDirPath(outputDir, urlPath);
                    if (dirCheckPath != null && Directory.Exists(dirCheckPath))
                    {
                        var html = DirectoryListing.Render(dirCheckPath, urlPath, outputDir);
                        await WriteStringResponse(ctx.Response, html);
                        Interlocked.Increment(ref _totalRequests);
                        sw.Stop();
                        LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                        return;
                    }
                }

                // SPA fallback: serve index.html for client-side routes
                if (EnableSpaFallback && !HasStaticFileExtension(urlPath))
                {
                    var indexPath = Path.Combine(outputDir, "index.html");
                    if (File.Exists(indexPath))
                    {
                        await WriteFileResponse(ctx, indexPath, FileExtensions.Html);
                        if (method == "HEAD")
                            ctx.Response.OutputStream.Close();
                        Interlocked.Increment(ref _totalRequests);
                        sw.Stop();
                        goto LogRequest;
                    }
                }

                await ErrorPage.WriteNotFound(ctx, outputDir, urlPath);
                sw.Stop();
                LogWriter.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Allow subclasses to handle special file types
            if (await TryHandleSpecialFile(ctx, filePath, ext))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Serve the file
            await WriteFileResponse(ctx, filePath, ext);

            if (method == "HEAD")
            {
                ctx.Response.OutputStream.Close();
            }

            Interlocked.Increment(ref _totalRequests);
            isCacheHit = ctx.Response.StatusCode == 304;
            sw.Stop();

        LogRequest:
            var bytesServed = Interlocked.Read(ref _totalBytesServed) - bytesBefore;
            LogWriter.RequestDetail(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds,
                bytesServed > 0 ? bytesServed : null, isCacheHit ? true : null);
        }
        catch (Exception ex)
        {
            try
            {
                await HttpHelper.WriteStringResponse(ctx, 500,
                    $"<h1>500 — Internal Server Error</h1><p>{WebUtility.HtmlEncode(ex.Message)}</p>");
            }
            catch { }
            sw.Stop();
            LogWriter.Request(method, urlPath, 500, sw.ElapsedMilliseconds);
            LogWriter.Error("Server", ex.Message);
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    /// <summary>
    /// Write a file response with MIME type detection, optional HTML live-reload injection,
    /// ETag-based caching, and on-the-fly compression (Brotli/Gzip).
    /// Uses streaming CopyToAsync for large non-compressible files.
    /// </summary>
    private async Task WriteFileResponse(HttpListenerContext ctx, string filePath, string ext)
    {
        var response = ctx.Response;
        var request = ctx.Request;

        HttpHelper.AddCorsHeaders(response);
        response.ContentType = MimeMapper.GetMimeType(filePath);

        var etag = HttpHelper.ComputeETag(filePath);
        response.Headers["ETag"] = etag;
        response.Headers["Last-Modified"] = new FileInfo(filePath).LastWriteTimeUtc.ToString("R");

        if (HttpHelper.IsETagMatch(request, etag))
        {
            response.StatusCode = 304;
            response.ContentLength64 = 0;
            Interlocked.Increment(ref _cacheHits);
            return;
        }

        var fileInfo = new FileInfo(filePath);
        var script = GetLiveReloadScript();

        // Determine compression method
        var compressionMethod = GetCompressionMethod(request.Headers["Accept-Encoding"], response.ContentType, fileInfo.Length);

        // Inject live-reload script into HTML if needed
        if (ext == FileExtensions.Html && script != null)
        {
            var html = await File.ReadAllTextAsync(filePath);
            html = html.Replace("</body>", script + "\n</body>");
            if (!html.Contains("</body>"))
                html += script;

            var bytes = Encoding.UTF8.GetBytes(html);
            await WriteCompressedOrRaw(response, bytes, compressionMethod);
        }
        else if (compressionMethod != null)
        {
            // Read and compress text-based files
            var bytes = await File.ReadAllBytesAsync(filePath);
            await WriteCompressedOrRaw(response, bytes, compressionMethod);
        }
        else
        {
            // Stream binary files directly — no intermediate buffer for large files
            response.ContentLength64 = fileInfo.Length;
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await fs.CopyToAsync(response.OutputStream);
            Interlocked.Add(ref _totalBytesServed, fileInfo.Length);
        }

        await response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Determine the best compression method based on Accept-Encoding, content type, and file size.
    /// Returns null if compression should not be applied.
    /// </summary>
    private static string? GetCompressionMethod(string? acceptEncoding, string contentType, long fileSize)
    {
        // Skip compression for small files (less than 1KB)
        if (fileSize < 1024) return null;

        // Only compress text-based content types
        if (!IsCompressibleContentType(contentType)) return null;

        if (string.IsNullOrEmpty(acceptEncoding)) return null;

        // Prefer Brotli, fallback to Gzip
        if (acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase))
            return "br";
        if (acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase))
            return "gzip";

        return null;
    }

    private static bool IsCompressibleContentType(string contentType)
    {
        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("svg", StringComparison.OrdinalIgnoreCase) ||
               contentType.Contains("xml", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Write byte content with optional compression. Sets appropriate headers.
    /// </summary>
    private async Task WriteCompressedOrRaw(HttpListenerResponse response, byte[] data, string? compressionMethod)
    {
        if (compressionMethod != null)
        {
            response.Headers["Content-Encoding"] = compressionMethod;
            response.Headers["Vary"] = "Accept-Encoding";

            using var ms = new MemoryStream();
            using (var cs = CreateCompressionStream(ms, compressionMethod))
            {
                await cs.WriteAsync(data);
            }

            var compressed = ms.ToArray();
            response.ContentLength64 = compressed.Length;
            await response.OutputStream.WriteAsync(compressed);
            Interlocked.Add(ref _totalBytesServed, compressed.Length);
        }
        else
        {
            response.ContentLength64 = data.Length;
            await response.OutputStream.WriteAsync(data);
            Interlocked.Add(ref _totalBytesServed, data.Length);
        }
    }

    private static Stream CreateCompressionStream(Stream output, string method)
    {
        return method.Equals("br", StringComparison.OrdinalIgnoreCase)
            ? new BrotliStream(output, CompressionLevel.Fastest)
            : new GZipStream(output, CompressionLevel.Fastest);
    }

    private static async Task WriteStringResponse(HttpListenerResponse response, string content, string contentType = "text/html; charset=utf-8")
    {
        response.ContentType = contentType;
        HttpHelper.AddCorsHeaders(response);
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        await response.OutputStream.FlushAsync();
    }

    private void TryOpenBrowser()
    {
        if (!OpenBrowser) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"http://{Host}:{Port}/",
                UseShellExecute = true
            });
            LogWriter.Info("Browser", "Opened in default browser");
        }
        catch (Exception ex)
        {
            LogWriter.Warn("Browser", $"Could not open browser: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if the URL path has a known static file extension.
    /// Used by SPA fallback to distinguish between asset requests and client-side routes.
    /// </summary>
    private static bool HasStaticFileExtension(string urlPath)
    {
        var ext = Path.GetExtension(urlPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return false;

        // Extensions that indicate a static file request (not a client-side route)
        return ext switch
        {
            ".html" or ".htm" or ".css" or ".js" or ".mjs" or ".json" or ".xml" or
            ".svg" or ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".ico" or
            ".woff" or ".woff2" or ".ttf" or ".otf" or ".pdf" or ".map" or
            ".mp4" or ".webm" or ".mp3" or ".ogg" or ".wav" or ".txt" or ".md" or
            ".csv" or ".wasm" or ".avif" or ".zcss" => true,
            _ => false
        };
    }
}
