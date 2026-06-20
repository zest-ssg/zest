using Tomlyn;
using Zest.Engine;

namespace Zest.Infra.Configuration;

/// <summary>
/// Loads SiteConfig from _config.toml with full zero-config support.
/// When _config.toml is absent, all fields use sensible defaults.
/// Only overrides specified fields — all others remain at defaults.
/// </summary>
public static class SiteConfigLoader
{
    /// <summary>
    /// Load site configuration from the given project root, or auto-detect.
    /// </summary>
    public static SiteConfig Load(string? projectPath = null)
    {
        var root = FindProjectRoot(projectPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "_config.toml");

        if (!File.Exists(configPath))
            return SiteConfigDefaults.create();

        try
        {
            var model = Toml.ToModel(File.ReadAllText(configPath));
            if (model == null) return SiteConfigDefaults.create();

            var config = SiteConfigDefaults.create();

            // Build a new config with overrides using F# record With method
            var title = GetString(model, "title", config.Title);
            var baseUrl = GetString(model, "base_url", config.BaseUrl);
            var description = GetString(model, "description", config.Description);
            var defaultLayout = GetString(model, "default_layout", config.DefaultLayout);
            var permalinkFormat = GetString(model, "permalink_format", config.PermalinkFormat);
            var siteVersion = GetString(model, "site_version", config.SiteVersion);
            var contentDir = GetString(model, "content_dir", config.ContentDir);
            var outputDir = GetString(model, "output_dir", config.OutputDir);
            var layoutsDir = GetString(model, "layouts_dir", config.LayoutsDir);
            var includesDir = GetString(model, "includes_dir", config.IncludesDir);
            var dataDir = GetString(model, "data_dir", config.DataDir);
            var assetsDir = GetString(model, "assets_dir", config.AssetsDir);
            var devServerPort = GetInt(model, "dev_server_port", config.DevServerPort);
            var liveReloadPort = GetInt(model, "live_reload_port", config.LiveReloadPort);
            var enableMinification = GetBool(model, "enable_minification", config.EnableMinification);
            var enableCacheBusting = GetBool(model, "enable_cache_busting", config.EnableCacheBusting);

            return new SiteConfig(
                title: title,
                baseUrl: baseUrl,
                description: description,
                contentDir: contentDir,
                outputDir: outputDir,
                layoutsDir: layoutsDir,
                includesDir: includesDir,
                dataDir: dataDir,
                assetsDir: assetsDir,
                defaultLayout: defaultLayout,
                permalinkFormat: permalinkFormat,
                devServerPort: devServerPort,
                liveReloadPort: liveReloadPort,
                enableMinification: enableMinification,
                enableCacheBusting: enableCacheBusting,
                siteVersion: siteVersion
            );
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Zest] Warning: Failed to parse '{configPath}': {ex.Message}");
            return SiteConfigDefaults.create();
        }
    }

    private static string? FindProjectRoot(string? hint)
    {
        var start = hint != null
            ? new DirectoryInfo(hint)
            : new DirectoryInfo(Directory.GetCurrentDirectory());
        var dir = start;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "_config.toml")) ||
                Directory.Exists(Path.Combine(dir.FullName, "content")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return start.FullName;
    }

    private static string GetString(IDictionary<string, object> dict, string key, string fallback)
    {
        if (dict.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
            return s;
        return fallback;
    }

    private static int GetInt(IDictionary<string, object> dict, string key, int fallback)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
        }
        return fallback;
    }

    private static bool GetBool(IDictionary<string, object> dict, string key, bool fallback)
    {
        if (dict.TryGetValue(key, out var val) && val is bool b)
            return b;
        return fallback;
    }
}
