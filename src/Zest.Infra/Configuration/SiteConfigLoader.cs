using Tomlyn;
using Zest.Engine;
using Zest.Infra.Services;

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
        var root = ProjectRootFinder.Find(projectPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "_config.toml");

        if (!File.Exists(configPath))
            return SiteConfigDefaults.create();

        try
        {
            var model = Toml.ToModel(File.ReadAllText(configPath));
            if (model == null) return SiteConfigDefaults.create();

            var config = SiteConfigDefaults.create();

            var title = TomlConfigReader.GetString(model, "title", config.Title);
            var baseUrl = TomlConfigReader.GetString(model, "base_url", config.BaseUrl);
            var description = TomlConfigReader.GetString(model, "description", config.Description);
            var defaultLayout = TomlConfigReader.GetString(model, "default_layout", config.DefaultLayout);
            var permalinkFormat = TomlConfigReader.GetString(model, "permalink_format", config.PermalinkFormat);
            var siteVersion = TomlConfigReader.GetString(model, "site_version", config.SiteVersion);
            var contentDir = TomlConfigReader.GetString(model, "content_dir", config.ContentDir);
            var outputDir = TomlConfigReader.GetString(model, "output_dir", config.OutputDir);
            var layoutsDir = TomlConfigReader.GetString(model, "layouts_dir", config.LayoutsDir);
            var includesDir = TomlConfigReader.GetString(model, "includes_dir", config.IncludesDir);
            var dataDir = TomlConfigReader.GetString(model, "data_dir", config.DataDir);
            var assetsDir = TomlConfigReader.GetString(model, "assets_dir", config.AssetsDir);
            var rootDir = TomlConfigReader.GetString(model, "root_dir", config.RootDir);
            var devServerPort = TomlConfigReader.GetInt(model, "dev_server_port", config.DevServerPort);
            var liveReloadPort = TomlConfigReader.GetInt(model, "live_reload_port", config.LiveReloadPort);
            var enableMinification    = TomlConfigReader.GetBool(model, "enable_minification",     config.EnableMinification);
            var enableCacheBusting   = TomlConfigReader.GetBool(model, "enable_cache_busting",    config.EnableCacheBusting);
            var enableParallel       = TomlConfigReader.GetBool(model, "enable_parallel_build",   config.EnableParallelBuild);
            var enableIncremental    = TomlConfigReader.GetBool(model, "enable_incremental_build",config.EnableIncrementalBuild);
            var author               = TomlConfigReader.GetString(model, "author",   config.Author);
            var language             = TomlConfigReader.GetString(model, "language", config.Language);
            var logLevel             = TomlConfigReader.GetString(model, "log_level", config.LogLevel);
            var logToFile            = TomlConfigReader.GetBool  (model, "log_to_file", config.LogToFile);
            var logTimestamps        = TomlConfigReader.GetBool  (model, "log_timestamps", config.LogTimestamps);
            var templateEngine       = TomlConfigReader.GetString(model, "template_engine", config.TemplateEngine);

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
                rootDir: rootDir,
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
                language: language,
                logLevel: logLevel,
                logToFile: logToFile,
                logTimestamps: logTimestamps,
                templateEngine: templateEngine
            );
        }
        catch (Exception ex)
        {
            Logger.Error("Config", $"Failed to parse '{configPath}': {ex.Message}", ex);
            return SiteConfigDefaults.create();
        }
    }
}
