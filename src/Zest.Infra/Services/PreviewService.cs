using System.Net;
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
            catch
            {
                break;
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var outputDir = GetOutputDir();
            var urlPath = ctx.Request.Url?.AbsolutePath ?? "/";

            string filePath;
            try
            {
                filePath = HttpHelper.ResolveFilePath(outputDir, urlPath);
            }
            catch (UnauthorizedAccessException)
            {
                ctx.Response.StatusCode = 403;
                await HttpHelper.WriteStringResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
                return;
            }

            if (!File.Exists(filePath))
            {
                await HttpHelper.WriteNotFoundResponse(ctx, outputDir);
                return;
            }

            await HttpHelper.WriteFileResponse(ctx, filePath);
        }
        catch (Exception ex)
        {
            try
            {
                ctx.Response.StatusCode = 500;
                await HttpHelper.WriteStringResponse(ctx, 500,
                    $"<h1>500 — Internal Server Error</h1><p>{ex.Message}</p>");
            }
            catch { }
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
