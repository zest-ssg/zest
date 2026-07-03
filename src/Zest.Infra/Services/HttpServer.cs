using System.Diagnostics;
using System.Net;
using System.Text;

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
            sw.Stop();
            var status = ctx.Response.StatusCode;
            LogWriter.Request(method, urlPath, status, sw.ElapsedMilliseconds);
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
    /// and ETag-based caching. Uses streaming CopyToAsync for large files.
    /// </summary>
    private async Task WriteFileResponse(HttpListenerContext ctx, string filePath, string ext)
    {
        HttpHelper.AddCorsHeaders(ctx.Response);
        ctx.Response.ContentType = MimeMapper.GetMimeType(filePath);

        var etag = HttpHelper.ComputeETag(filePath);
        ctx.Response.Headers["ETag"] = etag;
        ctx.Response.Headers["Last-Modified"] = new FileInfo(filePath).LastWriteTimeUtc.ToString("R");

        if (HttpHelper.IsETagMatch(ctx.Request, etag))
        {
            ctx.Response.StatusCode = 304;
            ctx.Response.ContentLength64 = 0;
            Interlocked.Increment(ref _cacheHits);
            return;
        }

        // Inject live-reload script into HTML if needed
        var script = GetLiveReloadScript();
        if (ext == ".html" && script != null)
        {
            var html = await File.ReadAllTextAsync(filePath);
            html = html.Replace("</body>", script + "\n</body>");
            if (!html.Contains("</body>"))
                html += script;

            var bytes = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
        }
        else
        {
            // Stream the file directly — no intermediate buffer for large files
            ctx.Response.ContentLength64 = new FileInfo(filePath).Length;
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
            await fs.CopyToAsync(ctx.Response.OutputStream);
        }

        await ctx.Response.OutputStream.FlushAsync();
        Interlocked.Add(ref _totalBytesServed, new FileInfo(filePath).Length);
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
}
