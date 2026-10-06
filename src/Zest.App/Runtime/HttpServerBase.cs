using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Zest.Compiler.Model;
using Zest.Compiler.Build;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Abstract base class for HTTP servers (development server and preview server).
/// Encapsulates common HTTP handling: listener lifecycle, CORS, 404/500, request
/// logging, path traversal protection, ETag caching, compression, SSE fallback
/// and statistics.
/// </summary>
public abstract class HttpServerBase : IDisposable
{
    /// <summary>SSE fallback endpoint used when WebSocket is unavailable.</summary>
    protected const string SsePath = "/__zest_livereload_events";

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

    // Live-reload snippet — identical for every page, so it is built once.
    private string? _liveReloadScript;
    private readonly object _liveReloadLock = new();

    // In-flight request tasks, tracked so Shutdown can wait for them.
    private readonly HashSet<Task> _inflightTasks = new();
    private readonly object _inflightLock = new();

    // SSE fallback clients. Included here rather than duplicated in each
    // server: the two servers differ only in when they broadcast.
    private readonly List<Stream> _sseClients = new();
    private readonly object _sseLock = new();

    private bool _shutdown;

    /// <summary>Directories whose contents should NOT trigger rebuilds.</summary>
    protected HashSet<string>? IgnoredDirNames { get; set; }

    protected HttpServerBase(string host = "localhost", bool openBrowser = false)
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

    /// <summary>Whether this server exposes the SSE fallback endpoint.</summary>
    protected virtual bool EnableSse => false;

    /// <summary>Hook for handling special file types (e.g., .zcss compilation).</summary>
    protected virtual Task<bool> TryHandleSpecialFile(HttpListenerContext ctx, string filePath, string ext)
        => Task.FromResult(false);

    /// <summary>Hook for providing a live-reload script snippet for HTML injection.</summary>
    protected virtual string? GetLiveReloadScript() => null;

    /// <summary>Hook for handling virtual paths (e.g., status endpoints).</summary>
    protected virtual Task<bool> TryHandleVirtualPath(HttpListenerContext ctx, string urlPath)
        => Task.FromResult(false);

    // ── Host handling ──

    /// <summary>
    /// Map a user-facing host into an <see cref="HttpListener"/> prefix host.
    /// <c>0.0.0.0</c>, <c>*</c> and <c>+</c> all mean "every interface", and
    /// HTTP.sys only accepts <c>+</c> or <c>*</c> spelled that way.
    /// </summary>
    protected static string ListenHost(string host) =>
        host is "0.0.0.0" or "*" or "+" ? "+" : host;

    /// <summary>Host to put in a browser URL (a wildcard bind is not browsable).</summary>
    protected static string BrowserHost(string host) =>
        host is "0.0.0.0" or "*" or "+" ? "localhost" : host;

    /// <summary>
    /// True when the server is only reachable from this machine, which is the
    /// condition under which wildcard CORS is safe.
    /// </summary>
    protected static bool IsLoopbackHost(string host) =>
        host is "localhost" or "127.0.0.1" or "::1" or "[::1]";

    private bool AllowWildcardOrigin => IsLoopbackHost(Host);

    // ── Debug / status endpoint ──

    /// <summary>
    /// Build a JSON status response for the /__zest_status debug endpoint.
    /// Override in subclasses to add server-specific metrics.
    /// </summary>
    protected virtual string GetStatusJson()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append(ci, $"\"server\":\"{ServerName}\",");
        sb.Append(ci, $"\"port\":{Port},");
        sb.Append(ci, $"\"requests\":{TotalRequests},");
        sb.Append(ci, $"\"cacheHits\":{CacheHits},");
        sb.Append(ci, $"\"bytesServed\":{TotalBytesServed},");
        sb.Append(ci, $"\"sseClients\":{SseClientCount},");
        sb.Append(ci, $"\"uptimeSeconds\":{(int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds}");
        sb.Append('}');
        return sb.ToString();
    }

    private int SseClientCount
    {
        get { lock (_sseLock) return _sseClients.Count; }
    }

    /// <summary>
    /// Handle the /__zest_status debug endpoint. Returns server metrics as JSON.
    /// </summary>
    protected async Task HandleStatusEndpoint(HttpListenerContext ctx)
    {
        var json = GetStatusJson();
        await WriteTextResponse(ctx, 200, json, "application/json; charset=utf-8");
    }

    // ── Lifecycle ──

    /// <summary>
    /// Start the HTTP listener. Displays the banner <em>before</em> calling
    /// <see cref="OnStarted"/> so the user sees server info immediately,
    /// even when the initial build takes several seconds.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The port is already in use, or the prefix cannot be bound.
    /// </exception>
    public void Start()
    {
        var cts = new CancellationTokenSource();
        Cts = cts;
        var listener = new HttpListener();
        Listener = listener;
        var prefix = $"http://{ListenHost(Host)}:{Port}/";
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (Exception ex) when (ex is HttpListenerException or SocketException or InvalidOperationException or AccessViolationException)
        {
            Listener = null;
            Cts = null;
            cts.Dispose();
            throw new InvalidOperationException(
                $"Could not listen on {prefix} — {ex.Message}. " +
                "Check that the port is free and that the host is permitted to bind " +
                "(binding a non-localhost host usually requires elevated rights).", ex);
        }

        _ = Task.Run(() => ServeHttp(cts.Token));

        var outputDir = GetOutputDir();

        // Show banner BEFORE OnStarted so long builds don't hide server info.
        LogWriter.Banner(
            $"Zest {ServerName} Server",
            $"http://{BrowserHost(Host)}:{Port}/",
            ("Host", Host),
            ("Port", Port.ToString(CultureInfo.InvariantCulture)),
            ("Output", outputDir),
            ("Verbose", LogWriter.Verbose ? "ON" : "off")
        );

        TryOpenBrowser();

        LogWriter.WriteDim("  Press Ctrl+C to stop.");

        // Deferred setup (build, file watching, WebSocket) — runs after banner.
        OnStarted();
    }

    /// <summary>Called after the banner is displayed. Override for setup.</summary>
    protected virtual void OnStarted() { }

    /// <summary>
    /// Called exactly once during <see cref="Shutdown"/>, after the listener is
    /// stopped and SSE clients are closed. Override for server-specific cleanup
    /// (watchers, WebSocket hub, child processes).
    /// </summary>
    protected virtual void OnShutdown() { }

    /// <summary>
    /// Stop serving and release the listener. Idempotent: a Ctrl+C handler and
    /// a <c>using</c> block both run it, and the second call must be a no-op
    /// rather than a second round of statistics.
    /// </summary>
    public virtual void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;

        Cts?.Cancel();

        try
        {
            Listener?.Stop();
            Listener?.Close();
        }
        catch (ObjectDisposedException) { /* already closed */ }
        finally
        {
            Listener = null;
        }

        CloseSseClients();
        OnShutdown();

        // Give in-flight requests a moment to finish cleanly. SSE/keep-alive
        // loops exit on their own once the CTS is cancelled above.
        Task[] pending;
        lock (_inflightLock) pending = _inflightTasks.ToArray();
        if (pending.Length > 0)
        {
            try { Task.WaitAll(pending, TimeSpan.FromSeconds(5)); } catch { }
        }

        Cts?.Dispose();
        Cts = null;

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
        while (!ct.IsCancellationRequested && Listener is { IsListening: true })
        {
            try
            {
                var ctx = await Listener.GetContextAsync().WaitAsync(ct);
                // Track the request so Shutdown can wait for it instead of
                // abandoning a response mid-write.
                var task = Task.Run(() => HandleRequest(ctx), CancellationToken.None);
                TrackRequest(task);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (HttpListenerException) { break; }
            catch (InvalidOperationException) { break; }
            catch (Exception ex)
            {
                // A transient failure must not silently stop the server; log it
                // and pause briefly so a persistent error cannot spin.
                LogWriter.Error("Server", $"Accept loop error: {ex.Message}");
                try { await Task.Delay(200, ct); } catch { break; }
            }
        }
    }

    /// <summary>Remember a fire-and-forget request task; remove it when done.</summary>
    private void TrackRequest(Task task)
    {
        lock (_inflightLock) _inflightTasks.Add(task);
        _ = task.ContinueWith(t =>
        {
            lock (_inflightLock) _inflightTasks.Remove(t);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>Live-reload snippet, built once per server lifetime.</summary>
    private string? GetCachedLiveReloadScript()
    {
        lock (_liveReloadLock)
        {
            _liveReloadScript ??= GetLiveReloadScript();
            return _liveReloadScript;
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        // NOTE: every early return in this method must call LogWriter.Request so
        // the request line and the statistics stay in step.
        var sw = Stopwatch.StartNew();
        var urlPath = ctx.Request.Url?.AbsolutePath ?? "/";
        var method = ctx.Request.HttpMethod;
        var bytesBefore = Interlocked.Read(ref _totalBytesServed);

        // Count every request that reaches us, once. Previously only a few
        // branches incremented this, so /__zest_status under-reported badly.
        Interlocked.Increment(ref _totalRequests);

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
                ctx.Response.Headers["Allow"] = "GET, HEAD, OPTIONS";
                await WriteTextResponse(ctx, 405, "<h1>405 — Method Not Allowed</h1>");
                sw.Stop();
                LogWriter.Request(method, urlPath, 405, sw.ElapsedMilliseconds);
                return;
            }

            var outputDir = GetOutputDir();

            // Debug status endpoint
            if (urlPath == "/__zest_status")
            {
                await HandleStatusEndpoint(ctx);
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // SSE fallback for live reload
            if (EnableSse && urlPath == SsePath)
            {
                await HandleSseConnection(ctx);
                sw.Stop();
                LogWriter.Request(method, urlPath, 200, sw.ElapsedMilliseconds);
                return;
            }

            // Virtual paths (status endpoints, etc.)
            if (await TryHandleVirtualPath(ctx, urlPath))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
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
                await WriteTextResponse(ctx, 403, "<h1>403 — Forbidden</h1>");
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
                        var html = DirectoryListing.Render(dirCheckPath, urlPath);
                        await WriteHtmlResponse(ctx.Response, html);
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
                        await ServeFile(ctx, indexPath, FileTypes.Html, method);
                        sw.Stop();
                        LogWriter.Request(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
                        return;
                    }
                }

                await ErrorPage.WriteNotFound(ctx, outputDir, urlPath, AllowWildcardOrigin);
                sw.Stop();
                LogWriter.Request(method, urlPath, 404, sw.ElapsedMilliseconds);
                return;
            }

            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            // Special file handling (e.g., .zcss compilation)
            if (await TryHandleSpecialFile(ctx, filePath, ext))
            {
                sw.Stop();
                LogWriter.Request(method, urlPath, ctx.Response.StatusCode, sw.ElapsedMilliseconds);
                return;
            }

            // Serve the file
            await ServeFile(ctx, filePath, ext, method);

            var isCacheHit = ctx.Response.StatusCode == 304;
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
                await WriteTextResponse(ctx, 500, diagnosticHtml);
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
        var etag = HttpResponses.ComputeETag(filePath, fileInfo.Length, fileInfo.LastWriteTimeUtc);
        response.Headers["ETag"] = etag;
        response.Headers["Last-Modified"] = fileInfo.LastWriteTimeUtc.ToString("R", CultureInfo.InvariantCulture);

        var compressionMethod = GetCompressionMethod(
            request.Headers["Accept-Encoding"], response.ContentType, fileInfo.Length);

        // Advertise encoding negotiation whenever we might compress, so caches
        // never hand a compressed body to a client that cannot read it.
        if (compressionMethod != null)
            response.Headers["Vary"] = "Accept-Encoding";

        if (HttpResponses.IsETagMatch(request, etag))
        {
            response.StatusCode = 304;
            response.ContentLength64 = 0;
            if (compressionMethod != null)
                response.Headers["Vary"] = "Accept-Encoding";
            Interlocked.Increment(ref _cacheHits);
            return;
        }

        // HEAD requests: send headers only, no body.
        if (method == "HEAD")
        {
            response.ContentLength64 = fileInfo.Length;
            return;
        }

        var script = GetCachedLiveReloadScript();

        // HTML with live-reload injection
        if (ext == FileTypes.Html && script != null)
        {
            var html = await ReadAllTextWithDeleteShareAsync(filePath);
            if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
                html = html.Replace("</body>", script + Environment.NewLine + "</body>", StringComparison.OrdinalIgnoreCase);
            else
                html += script;

            var bytes = Encoding.UTF8.GetBytes(html);
            await WriteCompressedOrRaw(response, bytes, compressionMethod);
        }
        else if (compressionMethod != null)
        {
            // Read and compress text-based files
            var bytes = await ReadAllBytesWithDeleteShareAsync(filePath);
            await WriteCompressedOrRaw(response, bytes, compressionMethod);
        }
        else
        {
            // Stream binary files directly — no intermediate buffer. FileShare.Delete
            // lets the build's atomic rename swap the file while we hold the old
            // handle, so the server and the build never fight over the same inode.
            response.ContentLength64 = fileInfo.Length;
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, useAsync: true);
            await fs.CopyToAsync(response.OutputStream);
            Interlocked.Add(ref _totalBytesServed, fileInfo.Length);
        }

        await response.OutputStream.FlushAsync();
    }

    /// <summary>Read all text with FileShare.Delete so concurrent atomic renames succeed.</summary>
    private static async Task<string> ReadAllTextWithDeleteShareAsync(string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, useAsync: true);
        using var reader = new StreamReader(fs, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    /// <summary>Read all bytes with FileShare.Delete so concurrent atomic renames succeed.</summary>
    private static async Task<byte[]> ReadAllBytesWithDeleteShareAsync(string filePath)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, useAsync: true);
        using var ms = new MemoryStream();
        await fs.CopyToAsync(ms);
        return ms.ToArray();
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

        var accepted = ParseAcceptEncoding(acceptEncoding);
        if (accepted.Count == 0) return null;

        // Prefer Brotli, fallback to Gzip. A quality value of 0 means "not
        // acceptable", so a plain Contains('br') test is not enough.
        if (accepted.GetValueOrDefault("br", 0) > 0) return "br";
        if (accepted.GetValueOrDefault("gzip", 0) > 0) return "gzip";

        return null;
    }

    /// <summary>
    /// Parse an Accept-Encoding header into token → quality pairs, honouring
    /// <c>q=0</c> exclusions such as <c>gzip;q=0</c>.
    /// </summary>
    private static Dictionary<string, double> ParseAcceptEncoding(string? header)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(header)) return result;

        foreach (var part in header.Split(','))
        {
            var segments = part.Split(';');
            var token = segments[0].Trim();
            if (token.Length == 0) continue;

            var quality = 1.0;
            for (var i = 1; i < segments.Length; i++)
            {
                var parameter = segments[i].Trim();
                if (!parameter.StartsWith("q=", StringComparison.OrdinalIgnoreCase)) continue;
                if (double.TryParse(parameter[2..], NumberStyles.Float, CultureInfo.InvariantCulture, out var q))
                    quality = q;
            }

            result[token] = quality;
        }
        return result;
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

    // ── SSE (Server-Sent Events) fallback ──

    /// <summary>
    /// Hold a server-sent-events connection open. Used when WebSocket is
    /// blocked by the environment. Runs until shutdown or client disconnect.
    /// </summary>
    protected async Task HandleSseConnection(HttpListenerContext ctx)
    {
        var token = Cts?.Token ?? CancellationToken.None;
        var response = ctx.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["Connection"] = "keep-alive";
        HttpResponses.AddCorsHeaders(response, AllowWildcardOrigin);
        response.SendChunked = true;

        var stream = response.OutputStream;
        lock (_sseLock) _sseClients.Add(stream);
        LogWriter.VerboseLog($"SSE client connected (total: {SseClientCount})");

        try
        {
            var initBytes = Encoding.UTF8.GetBytes(": connected\n\n");
            await stream.WriteAsync(initBytes, token);
            await stream.FlushAsync(token);

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(15_000, token);
                var keepalive = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await stream.WriteAsync(keepalive, token);
                await stream.FlushAsync(token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogWriter.VerboseLog($"SSE client disconnected: {ex.Message}");
        }
        finally
        {
            lock (_sseLock) _sseClients.Remove(stream);
            try { stream.Close(); } catch { }
        }
    }

    /// <summary>Push one SSE data frame to every connected client.</summary>
    protected void BroadcastSse(string jsonData)
    {
        // Snapshot under the lock, write outside it — a stalled SSE client
        // must never block the rebuild loop.
        Stream[] snapshot;
        lock (_sseLock)
        {
            if (_sseClients.Count == 0) return;
            snapshot = _sseClients.ToArray();
        }

        var payload = Encoding.UTF8.GetBytes($"data: {jsonData}\n\n");
        _ = Task.Run(() =>
        {
            var dead = new List<Stream>();
            foreach (var s in snapshot)
            {
                try
                {
                    s.Write(payload, 0, payload.Length);
                    s.Flush();
                }
                catch { dead.Add(s); }
            }

            if (dead.Count > 0)
            {
                lock (_sseLock)
                {
                    foreach (var s in dead) _sseClients.Remove(s);
                }
            }
        });
    }

    private void CloseSseClients()
    {
        lock (_sseLock)
        {
            foreach (var s in _sseClients)
            {
                try { s.Close(); } catch { }
            }
            _sseClients.Clear();
        }
    }

    // ── Response helpers ──

    /// <summary>Add CORS and security headers to every response.</summary>
    private void AddStandardHeaders(HttpListenerResponse response) =>
        HttpResponses.AddCorsHeaders(response, AllowWildcardOrigin);

    /// <summary>
    /// Write a body response honouring HEAD semantics: headers only, no body.
    /// </summary>
    private async Task WriteTextResponse(HttpListenerContext ctx, int statusCode, string html,
        string contentType = "text/html; charset=utf-8")
    {
        ctx.Response.StatusCode = statusCode;
        ctx.Response.ContentType = contentType;
        AddStandardHeaders(ctx.Response);
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentLength64 = bytes.Length;
        if (HttpResponses.IsHead(ctx)) return;

        await ctx.Response.OutputStream.WriteAsync(bytes);
        await ctx.Response.OutputStream.FlushAsync();
    }

    private async Task WriteHtmlResponse(HttpListenerResponse response, string html)
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
        sb.AppendLine(Invariant($"<title>{status} — {title} · Zest</title>"));
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
        sb.AppendLine(Invariant($"<h1>{status}</h1>"));
        sb.AppendLine(Invariant($"<p class=\"path\"><code>{WebUtility.HtmlEncode(urlPath)}</code></p>"));
        sb.AppendLine(Invariant($"<p>{WebUtility.HtmlEncode(title)}</p>"));

        sb.AppendLine("<div class=\"details\">");
        sb.AppendLine(Invariant($"<h2>{WebUtility.HtmlEncode(ex.GetType().Name)}</h2>"));
        sb.AppendLine(Invariant($"<p>{WebUtility.HtmlEncode(ex.Message)}</p>"));
        if (!string.IsNullOrEmpty(ex.StackTrace))
        {
            sb.AppendLine("<pre><code>");
            sb.AppendLine(WebUtility.HtmlEncode(ex.StackTrace));
            sb.AppendLine("</code></pre>");
        }
        if (ex.InnerException != null)
        {
            sb.AppendLine(Invariant($"<p><strong>Inner:</strong> {WebUtility.HtmlEncode(ex.InnerException.GetType().Name)}: {WebUtility.HtmlEncode(ex.InnerException.Message)}</p>"));
        }
        sb.AppendLine("</div>");

        sb.AppendLine("<div class=\"tag\">ZEST · Zenith Efficient Static Toolkit</div>");
        sb.AppendLine("</body></html>");

        return sb.ToString();
    }

    /// <summary>
    /// Format an interpolated string with the invariant culture. Without this
    /// the interpolation would use the ambient culture, so a machine with a
    /// comma decimal separator could render numbers into the HTML.
    /// </summary>
    private static string Invariant(FormattableString fs) => fs.ToString(CultureInfo.InvariantCulture);

    // ── Browser auto-open ──

    private void TryOpenBrowser()
    {
        if (!OpenBrowser) return;

        var url = $"http://{BrowserHost(Host)}:{Port}/";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
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
