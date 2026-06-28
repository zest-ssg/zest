using System.Diagnostics;
using System.Net;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Preview server — serves _site/ static files directly, no build triggered.
/// Useful for previewing the built site before deployment.
/// Features: request logging, ETag caching, CORS, custom 404 with suggestions,
/// pretty URLs, directory index fallback, SPA fallback.
/// </summary>
public class PreviewService : IDisposable
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private readonly string _host;
    private readonly bool _openBrowser;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private long _totalRequests;
    private long _cacheHits;
    private long _totalBytesServed;

    public int Port => _port;
    public string Host => _host;

    public PreviewService(SiteConfig config, int port, string host = "localhost", bool openBrowser = false)
    {
        _config = config;
        _port = port;
        _host = host;
        _openBrowser = openBrowser;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        var outputDir = GetOutputDir();

        // Verify output directory has content
        if (!Directory.EnumerateFiles(outputDir, "*", SearchOption.AllDirectories).Any())
        {
            Logger.Warn("Preview", $"Output directory '{outputDir}' is empty. Run 'zest build' first.");
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{_host}:{_port}/");
        _listener.Start();

        _ = Task.Run(() => ServeHttp(_cts.Token));

        Logger.Banner(
            "Zest Preview Server",
            $"http://{_host}:{_port}/",
            ("Host", _host),
            ("Port", _port.ToString()),
            ("Root", outputDir),
            ("Verbose", Logger.Verbose ? "ON" : "off")
        );

        if (_openBrowser)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"http://{_host}:{_port}/",
                    UseShellExecute = true
                });
                Logger.Info("Browser", "Opened in default browser");
            }
            catch (Exception ex)
            {
                Logger.Warn("Browser", $"Could not open browser: {ex.Message}");
            }
        }

        Logger.Info("Press Ctrl+C to stop.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();

        Logger.Info($"Total requests: {_totalRequests}, cache hits: {_cacheHits}, bytes served: {_totalBytesServed:N0}");
    }

    public void Dispose() => Stop();

    private async Task ServeHttp(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener!.IsListening)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().WaitAsync(ct);
                _ = Task.Run(() => HandleRequest(ctx));
            }
            catch
            {
                break;
            }
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
                HttpResponseUtility.AddCorsHeaders(ctx.Response);
                ctx.Response.OutputStream.Close();
                sw.Stop();
                Logger.Request(method, urlPath, 204, sw.ElapsedMilliseconds);
                return;
            }

            // Only allow GET and HEAD
            if (method != "GET" && method != "HEAD")
            {
                ctx.Response.StatusCode = 405;
                ctx.Response.Headers["Allow"] = "GET, HEAD, OPTIONS";
                await HttpResponseUtility.WriteStringResponse(ctx, 405, "<h1>405 — Method Not Allowed</h1>");
                sw.Stop();
                Logger.Request(method, urlPath, 405, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = GetOutputDir();

            string filePath;
            try
            {
                filePath = HttpResponseUtility.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                ctx.Response.StatusCode = 403;
                await HttpResponseUtility.WriteStringResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                sw.Stop();
                Logger.Request(method, urlPath, 403, sw.ElapsedMilliseconds);
                Logger.Warn("Security", $"Path traversal blocked: {urlPath}");
                return;
            }

            if (!File.Exists(filePath))
            {
                await HttpResponseUtility.WriteNotFoundResponse(ctx, outputDir, urlPath);
                sw.Stop();
                Logger.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                Logger.VerboseLog($"404 for {urlPath} → resolved to {filePath}");
                return;
            }

            // Serve the file with ETag/caching
            var cacheHit = await HttpResponseUtility.WriteFileResponseAsync(ctx, filePath);

            // HEAD request: don't send body
            if (method == "HEAD")
            {
                ctx.Response.OutputStream.Close();
            }

            Interlocked.Increment(ref _totalRequests);
            if (cacheHit) Interlocked.Increment(ref _cacheHits);
            var fi = new FileInfo(filePath);
            Interlocked.Add(ref _totalBytesServed, fi.Length);

            sw.Stop();
            Logger.Request(method, urlPath, cacheHit ? 304 : 200, sw.ElapsedMilliseconds);

            if (Logger.Verbose)
            {
                Logger.VerboseLog($"  → {filePath} ({fi.Length:N0} bytes)");
                if (cacheHit)
                    Logger.VerboseLog("  → 304 Not Modified (ETag match)");
            }
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                await HttpResponseUtility.WriteStringResponse(ctx, 500,
                    $"<h1>500 — Internal Server Error</h1><p>{System.Net.WebUtility.HtmlEncode(ex.Message)}</p>");
            }
            catch { }
            sw.Stop();
            Logger.Request(method, urlPath, 500, sw.ElapsedMilliseconds);
            Logger.Error("Server", ex.Message);
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch { }
        }
    }

    private string GetOutputDir()
    {
        var dir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        return dir;
    }
}
