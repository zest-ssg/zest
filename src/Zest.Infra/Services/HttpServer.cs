using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Abstract base class for HTTP servers (development server and preview server).
/// Encapsulates common HTTP handling: listener lifecycle, CORS, 404/500, request
/// logging, path traversal protection, ETag caching, compression, and statistics.
/// </summary>
public abstract class HttpServer : IDisposable
{
    protected string Host { get; }
    protected bool OpenBrowser { get; }
    protected bool EnableSpaFallback { get; set; }
    protected bool EnableDirectoryListing { get; set; }
    protected HttpListener? Listener { get; set; }
    protected CancellationTokenSource? Cts { get; set; }

    // Statistics
    private long _totalRequests;
    private long _cacheHits;
    private long _totalBytesServed;
    protected long TotalRequests => Interlocked.Read(ref _totalRequests);
    protected long CacheHits => Interlocked.Read(ref _cacheHits);
    protected long TotalBytesServed => Interlocked.Read(ref _totalBytesServed);

    /// <summary>Directories whose contents should NOT trigger rebuilds.</summary>
    protected HashSet<string>? IgnoredDirNames { get; set; }

    protected HttpServer(string host = "localhost", bool openBrowser = false)
    {
        Host = host;
        OpenBrowser = openBrowser;
    }

    // ── Abstract members ──

    /// <summary>Display name for the server (used in logs and banner).</summary>
    protected abstract string ServerName { get; }

    /// <summary>The port the server listens on.</summary>
    protected abstract int Port { get; }

    /// <summary>Resolve the output/content directory for serving files.</summary>
    protected abstract string GetOutputDir();

    /// <summary>Hook for handling special file types (e.g., .zcss compilation).</summary>
    protected virtual Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
        => Task.FromResult(false);

    /// <summary>Hook for providing a live-reload script snippet for HTML injection.</summary>
    protected virtual string? GetLiveReloadScript() => null;

    /// <summary>Hook for handling virtual paths (e.g., SSE endpoints, status).</summary>
    protected virtual Task<bool> TryHandleVirtualPath(HttpListenerContext ctx, string urlPath)
        => Task.FromResult(false);

    // ── Debug / status endpoint ──

    /// <summary>
    /// Build a JSON status response for the /__zest_status debug endpoint.
    /// Override in subclasses to add server-specific metrics.
    /// </summary>
    protected virtual string GetStatusJson()
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new System.Text.StringBuilder();
        sb.Append('{');
        sb.Append(ci, $"\"server\":\"{ServerName}\",");
        sb.Append(ci, $"\"port\":{Port},");
        sb.Append(ci, $"\"requests\":{TotalRequests},");
        sb.Append(ci, $"\"cacheHits\":{CacheHits},");
        sb.Append(ci, $"\"bytesServed\":{TotalBytesServed},");
        sb.Append(ci, $"\"uptime\":\"{DateTime.UtcNow - System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime():c}\"");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Handle the /__zest_status debug endpoint. Returns server metrics as JSON.
    /// </summary>
    protected async Task HandleStatusEndpoint(HttpListenerContext ctx)
    {
        var json = GetStatusJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        ctx.Response.ContentType = "application/json; charset=utf-8";
        AddStandardHeaders(ctx.Response);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    // ── Lifecycle ──

    /// <summary>
    /// Start the HTTP listener. Displays the banner <em>before</em> calling
    /// <see cref="OnStarted"/> so the user sees server info immediately,
    /// even when the initial build takes several seconds.
    /// </summary>
    public void Start()
    {
        Cts = new CancellationTokenSource();
        Listener = new HttpListener();
        Listener.Prefixes.Add($"http://{Host}:{Port}/");
        Listener.Start();
        _ = Task.Run(() => ServeHttp(Cts.Token));

        var outputDir = GetOutputDir();

        // Show banner BEFORE OnStarted so long builds don't hide server info.
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

        // Deferred setup (build, file watching, WebSocket) — runs after banner.
        OnStarted();
    }

    /// <summary>Called after the banner is displayed. Override for setup.</summary>
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

    // ── HTTP request handling ──

    private async Task ServeHttp(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && Listener!.IsListening)
        {
            try
            {
                var ctx = await Listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(ctx), CancellationToken.None);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
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
            // OPTIONS preflight
            if (method == "OPTIONS")
            {
                ctx.Response.StatusCode = 204;
                AddStandardHeaders(ctx.Response);
                ctx.Response.OutputStream.Close();
                sw.Stop();
                LogWriter.Request(method, urlPath, 204, sw.ElapsedMilliseconds);
                return;
            }

            // Only GET and HEAD
            if (method != "GET" && method != "HEAD")
            {
                await WriteErrorResponse(ctx, 405, "<h1>405 — Method Not Allowed</h1>");
                ctx.Response.Headers["Allow"] = "GET, HEAD, OPTIONS";
                sw.Stop();
                LogWriter.Request(method, urlPath, 405, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = GetOutputDir();

            // Debug status endpoint
            if (urlPath == "/__zest_status")
            {
                await HandleStatusEndpoint(ctx);
                Interlocked.Increment(ref _totalRequests);
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Virtual paths (SSE endpoints, etc.)
            if (await TryHandleVirtualPath(ctx, urlPath))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Resolve file path with traversal protection
            string filePath;
            try
            {
                filePath = PathMapper.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                await WriteErrorResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                sw.Stop();
                LogWriter.Request(method, urlPath, 403, sw.ElapsedMilliseconds);
                LogWriter.Warn("Security", $"Path traversal blocked: {urlPath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                // Directory listing
                if (EnableDirectoryListing)
                {
                    var dirCheckPath = PathMapper.ResolveDirPath(outputDir, urlPath);
                    if (dirCheckPath != null && Directory.Exists(dirCheckPath))
                    {
                        var html = DirectoryListing.Render(dirCheckPath, urlPath, outputDir);
                        await WriteHtmlResponse(ctx.Response, html);
                        Interlocked.Increment(ref _totalRequests);
                        sw.Stop();
                        LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                        return;
                    }
                }

                // SPA fallback
                if (EnableSpaFallback && !HasStaticFileExtension(urlPath))
                {
                    var indexPath = Path.Combine(outputDir, "index.html");
                    if (File.Exists(indexPath))
                    {
                        await ServeFile(ctx, indexPath, FileExtensions.Html, method);
                        Interlocked.Increment(ref _totalRequests);
                        sw.Stop();
                        LogWriter.Request(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
                        return;
                    }
                }

                await ErrorPage.WriteNotFound(ctx, outputDir, urlPath);
                sw.Stop();
                LogWriter.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Special file handling (e.g., .zcss compilation)
            if (await TryHandleSpecialFile(ctx, filePath, ext))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Serve the file
            await ServeFile(ctx, filePath, ext, method);

            Interlocked.Increment(ref _totalRequests);
            isCacheHit = ctx.Response.StatusCode == 304;
            sw.Stop();

            var bytesServed = Interlocked.Read(ref _totalBytesServed) - bytesBefore;
            LogWriter.RequestDetail(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds,
                bytesServed > 0 ? bytesServed : null, isCacheHit ? true : null);
        }
        catch (Exception ex)
        {
            try
            {
                var diagnosticHtml = BuildErrorPage(500, "Internal Server Error", ex, urlPath);
                await WriteErrorResponse(ctx, 500, diagnosticHtml);
            }
            catch { /* response may already be sent */ }
            sw.Stop();
            LogWriter.Request(method, urlPath, 500, sw.ElapsedMilliseconds);
            LogWriter.Error("Server", $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    // ── File serving ──

    /// <summary>
    /// Serve a file with MIME type, ETag caching, optional live-reload injection,
    /// and on-the-fly compression. For HEAD requests, only headers are sent.
    /// </summary>
    private async Task ServeFile(HttpListenerContext ctx, string filePath, string ext, string method)
    {
        var response = ctx.Response;
        var request = ctx.Request;

        AddStandardHeaders(response);
        response.ContentType = MimeMapper.GetMimeType(filePath);

        // Compute ETag once and reuse the FileInfo for Last-Modified
        var fileInfo = new FileInfo(filePath);
        var etag = ComputeETag(fileInfo);
        response.Headers["ETag"] = etag;
        response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R");

        if (IsETagMatch(request, etag))
        {
            response.StatusCode = 304;
            response.ContentLength64 = 0;
            Interlocked.Increment(ref _cacheHits);
            return;
        }

        // HEAD requests: send headers only, no body.
        if (method == "HEAD")
        {
            response.ContentLength64 = fileInfo.Length;
            return;
        }

        var script = GetLiveReloadScript();
        var compressionMethod = GetCompressionMethod(
            request.Headers["Accept-Encoding"], response.ContentType, fileInfo.Length);

        // HTML with live-reload injection
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
            // Stream binary files directly — no intermediate buffer
            response.ContentLength64 = fileInfo.Length;
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await fs.CopyToAsync(response.OutputStream);
            Interlocked.Add(ref _totalBytesServed, fileInfo.Length);
        }

        await response.OutputStream.FlushAsync();
    }

    /// <summary>
    /// Compute ETag from file metadata (path + size + mtime).
    /// Uses the same FileInfo instance already obtained during request handling.
    /// </summary>
    private static string ComputeETag(FileInfo fileInfo)
    {
        var raw = $"{fileInfo.FullName}:{fileInfo.Length}:{fileInfo.LastWriteTimeUtc.Ticks}";
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return "\"" + Convert.ToHexString(hash) + "\"";
    }

    private static bool IsETagMatch(HttpListenerRequest request, string etag)
    {
        var ifNoneMatch = request.Headers["If-None-Match"];
        return !string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag;
    }

    // ── Compression ──

    /// <summary>
    /// Determine the best compression method based on Accept-Encoding, content type,
    /// and file size. Returns null if compression should not be applied.
    /// </summary>
    private static string? GetCompressionMethod(string? acceptEncoding, string contentType, long fileSize)
    {
        // Skip compression for small files (< 1 KB)
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

    // ── Response helpers ──

    /// <summary>Add CORS and security headers to every response.</summary>
    private static void AddStandardHeaders(HttpListenerResponse response)
    {
        // CORS for local development
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, If-None-Match";

        // Security headers
        response.Headers["X-Content-Type-Options"] = "nosniff";
    }

    private static async Task WriteErrorResponse(HttpListenerContext ctx, int statusCode, string html)
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        AddStandardHeaders(ctx.Response);
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    private static async Task WriteHtmlResponse(HttpListenerResponse response, string html)
    {
        response.ContentType = "text/html; charset=utf-8";
        AddStandardHeaders(response);
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        await response.OutputStream.FlushAsync();
    }

    // ── Error page builder ──

    /// <summary>
    /// Build a diagnostic error page with exception details (type, message,
    /// stack trace). Only shown in dev/preview mode — production would
    /// use a generic error page.
    /// </summary>
    private static string BuildErrorPage(int status, string title, Exception ex, string urlPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine(CultureInfo_Invariant($"<title>{status} — {title} · Zest</title>"));
        sb.AppendLine("<style>");
        sb.AppendLine("body{font-family:system-ui,sans-serif;max-width:720px;margin:60px auto;padding:0 24px;color:#1a1a2e;line-height:1.6}");
        sb.AppendLine("h1{color:#e74c3c;font-size:2em;margin-bottom:4px}");
        sb.AppendLine(".path{color:#666;font-size:.9em;margin-bottom:24px}");
        sb.AppendLine(".details{background:#fef3f2;border:1px solid #fecaca;border-radius:8px;padding:16px;margin:16px 0}");
        sb.AppendLine(".details h2{font-size:1em;margin:0 0 8px;color:#991b1b}");
        sb.AppendLine("pre{background:#1a1a2e;color:#e2e8f0;padding:12px;border-radius:6px;overflow-x:auto;font-size:.85em;white-space:pre-wrap}");
        sb.AppendLine("code{font-family:'JetBrains Mono',monospace}");
        sb.AppendLine(".tag{display:inline-block;margin-top:24px;padding:4px 12px;background:#1a1a2e;color:#fff;border-radius:20px;font-size:.75em}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(CultureInfo_Invariant($"<h1>{status}</h1>"));
        sb.AppendLine(CultureInfo_Invariant($"<p class=\"path\"><code>{WebUtility.HtmlEncode(urlPath)}</code></p>"));
        sb.AppendLine(CultureInfo_Invariant($"<p>{WebUtility.HtmlEncode(title)}</p>"));

        sb.AppendLine("<div class=\"details\">");
        sb.AppendLine(CultureInfo_Invariant($"<h2>{WebUtility.HtmlEncode(ex.GetType().Name)}</h2>"));
        sb.AppendLine(CultureInfo_Invariant($"<p>{WebUtility.HtmlEncode(ex.Message)}</p>"));
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine("<pre><code>");
            sb.AppendLine(WebUtility.HtmlEncode(ex.StackTrace));
            sb.AppendLine("</code></pre>");
        }
        if (ex.InnerException != null)
        {
            sb.AppendLine(CultureInfo_Invariant($"<p><strong>Inner:</strong> {WebUtility.HtmlEncode(ex.InnerException.GetType().Name)}: {WebUtility.HtmlEncode(ex.InnerException.Message)}</p>"));
        }
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"tag\">ZEST · Zenith Efficient Static Toolkit</div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    // Workaround: static method can't use CultureInfo.InvariantCulture directly in
    // string interpolation. Provide a thin wrapper.
    private static string CultureInfo_Invariant(FormattableString fs)
        => fs.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ── Browser auto-open ──

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

    // ── SPA fallback helper ──

    private static bool HasStaticFileExtension(string urlPath)
    {
        var ext = Path.GetExtension(urlPath).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) return false;

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
