using System.Net;
using System.Text;
using Zest.Engine;

namespace Zest.Infra.Services;

/// <summary>
/// Preview server — serves _site/ static files directly, no build triggered.
/// Useful for previewing the built site before deployment.
/// </summary>
public class PreviewService : IDisposable
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    public int Port => _port;

    public PreviewService(SiteConfig config, int port)
    {
        _config = config;
        _port = port;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();

        _ = Task.Run(() => ServeHttp(_cts.Token));

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[Zest] Preview server started at http://localhost:{_port}");
        Console.ResetColor();
        Console.WriteLine("       Press Ctrl+C to stop.");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
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
            catch { break; }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var outputDir = Path.GetFullPath(Path.Combine(
                Directory.GetCurrentDirectory(), _config.OutputDir.TrimStart('.', '\\', '/')));

            var filePath = ResolveFilePath(outputDir, ctx.Request.Url?.AbsolutePath ?? "/");

            if (!File.Exists(filePath))
            {
                ctx.Response.StatusCode = 404;
                var custom404 = Path.Combine(outputDir, "404.html");
                if (File.Exists(custom404))
                {
                    var bytes = await File.ReadAllBytesAsync(custom404);
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = bytes.Length;
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                }
                else
                {
                    var msg = Encoding.UTF8.GetBytes("<h1>404 — Page Not Found</h1><p>The requested resource was not found.</p>");
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    ctx.Response.ContentLength64 = msg.Length;
                    await ctx.Response.OutputStream.WriteAsync(msg);
                }
            }
            else
            {
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                ctx.Response.ContentType = ext switch
                {
                    ".html" => "text/html; charset=utf-8",
                    ".css"  => "text/css; charset=utf-8",
                    ".js"   => "application/javascript; charset=utf-8",
                    ".png"  => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif"  => "image/gif",
                    ".svg"  => "image/svg+xml",
                    ".ico"  => "image/x-icon",
                    ".woff" => "font/woff",
                    ".woff2" => "font/woff2",
                    ".json" => "application/json",
                    ".xml"  => "application/xml",
                    _       => "application/octet-stream"
                };

                var bytes = await File.ReadAllBytesAsync(filePath);
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes);
            }
        }
        catch { }
        finally { ctx.Response.OutputStream.Close(); }
    }

    /// <summary>
    /// Resolve a URL path to a physical file path, handling directory-style routes.
    /// e.g. "/guide/" → "/guide/index.html", "/guide" → "/guide/index.html"
    /// </summary>
    private static string ResolveFilePath(string outputDir, string urlPath)
    {
        if (urlPath == "/") urlPath = "/index.html";

        // Strip leading slash, normalize separators
        var relative = urlPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

        // If path ends with separator (e.g. "/guide/"), append index.html
        if (relative.EndsWith(Path.DirectorySeparatorChar))
            relative = relative + "index.html";

        var fullPath = Path.Combine(outputDir, relative);

        // If no extension and file doesn't exist, try "/index.html" suffix
        if (!File.Exists(fullPath) && string.IsNullOrEmpty(Path.GetExtension(relative)))
            fullPath = Path.Combine(outputDir, relative, "index.html");

        return fullPath;
    }
}
