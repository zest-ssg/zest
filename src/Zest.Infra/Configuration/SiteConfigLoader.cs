using Tomlyn;
using Zest.Engine;

#nullable enable

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
            var enableMinification    = GetBool(model, "enable_minification",     config.EnableMinification);
            var enableCacheBusting   = GetBool(model, "enable_cache_busting",    config.EnableCacheBusting);
            var enableParallel       = GetBool(model, "enable_parallel_build",   config.EnableParallelBuild);
            var enableIncremental    = GetBool(model, "enable_incremental_build",config.EnableIncrementalBuild);
            var author               = GetString(model, "author",   config.Author);
            var language             = GetString(model, "language", config.Language);

            // Parse [[taxonomies]] array
            var taxonomies = config.Taxonomies;
            if (model.TryGetValue("taxonomies", out var taxObj) && taxObj is Tomlyn.Model.TomlTableArray taxArr)
            {
                var list = new List<TaxonomyConfig>();
                foreach (var t in taxArr)
                    if (t.TryGetValue("name", out var n) && t.TryGetValue("plural", out var p))
                        list.Add(new TaxonomyConfig(name: n.ToString()!, plural: p.ToString()!));
                if (list.Count > 0)
                    taxonomies = Microsoft.FSharp.Collections.ListModule.OfSeq(list);
            }

            // Parse [menus.*] tables → IDictionary<string, MenuItem list>
            var menus = new Dictionary<string, Microsoft.FSharp.Collections.FSharpList<MenuItem>>();
            if (model.TryGetValue("menu", out var menuObj) && menuObj is Tomlyn.Model.TomlTable menuTable)
            {
                foreach (var kv in menuTable)
                {
                    if (kv.Value is Tomlyn.Model.TomlTableArray entries)
                    {
                        var items = new List<MenuItem>();
                        foreach (var e in entries)
                            items.Add(new MenuItem(
                                label:  e.TryGetValue("label",  out var lv) ? lv.ToString()! : "",
                                url:    e.TryGetValue("url",    out var uv) ? uv.ToString()! : "#",
                                weight: e.TryGetValue("weight", out var wv) && wv is long wl ? (int)wl : 0));
                        items.Sort((a, b) => a.Weight.CompareTo(b.Weight));
                        menus[kv.Key] = Microsoft.FSharp.Collections.ListModule.OfSeq(items);
                    }
                }
            }
            IDictionary<string, Microsoft.FSharp.Collections.FSharpList<MenuItem>> menusDict = menus;

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
                siteVersion: siteVersion,
                enableParallelBuild: enableParallel,
                enableIncrementalBuild: enableIncremental,
                taxonomies: taxonomies,
                menus: menusDict,
                author: author,
                language: language
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
