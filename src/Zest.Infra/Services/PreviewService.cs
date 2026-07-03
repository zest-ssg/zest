using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Preview server — serves _site/ static files directly, no build triggered.
/// Useful for previewing the built site before deployment.
/// </summary>
public class PreviewService : HttpServer
{
    private readonly SiteConfig _config;
    private readonly int _port;
    private string? _outputDir;

    protected override string ServerName => "Preview";
    protected override int Port => _port;

    public PreviewService(SiteConfig config, int port, string host = "localhost", bool openBrowser = false)
        : base(host, openBrowser)
    {
        _config = config;
        _port = port;
    }

    protected override string GetOutputDir()
    {
        if (_outputDir != null) return _outputDir;

        _outputDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(),
            _config.OutputDir.TrimStart('.', '\\', '/')));

        if (!Directory.Exists(_outputDir))
            Directory.CreateDirectory(_outputDir);

        return _outputDir;
    }

    protected override void OnStarted()
    {
        var outputDir = GetOutputDir();

        // Verify output directory has content
        if (!Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            LogWriter.Warn("Preview", $"Output directory '{outputDir}' is empty. Run 'zest build' first.");
        }
    }
}
