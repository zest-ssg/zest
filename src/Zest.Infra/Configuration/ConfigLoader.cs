using Tomlyn;
using Zest.Engine;
using Zest.Infra.Services;

#nullable enable

namespace Zest.Infra.Configuration;

/// <summary>
/// Loads SiteConfig from _config.toml with full zero-config support.
/// When _config.toml is absent, all fields use sensible defaults.
/// Only overrides specified fields — all others remain at defaults.
///
/// Includes caching: the config file's last write time is tracked so that
/// repeated calls only re-parse when the file has actually changed.
/// </summary>
public static class ConfigLoader
{
    private static SiteConfig? _cachedConfig;
    private static DateTime _lastLoadTimeUtc;
    private static string? _lastConfigPath;

    /// <summary>
    /// Load site configuration from the given project root, or auto-detect.
    /// Uses cached result if the config file hasn't changed since last load.
    /// </summary>
    public static SiteConfig Load(string? projectPath = null)
    {
        var root = RootFinder.Find(projectPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "_config.toml");

        // Check cache: if the file hasn't changed, return cached config
        if (_cachedConfig != null && _lastConfigPath == configPath)
        {
            var currentWriteTime = File.Exists(configPath)
                ? File.GetLastWriteTimeUtc(configPath)
                : DateTime.MinValue;

            if (currentWriteTime == _lastLoadTimeUtc)
                return _cachedConfig;

            _lastLoadTimeUtc = currentWriteTime;
        }
        else
        {
            _lastConfigPath = configPath;
            _lastLoadTimeUtc = File.Exists(configPath)
                ? File.GetLastWriteTimeUtc(configPath)
                : DateTime.MinValue;
        }

        if (!File.Exists(configPath))
        {
            _cachedConfig = SiteConfigDefaults.create();
            return _cachedConfig;
        }

        try
        {
            var model = Toml.ToModel(File.ReadAllText(configPath));
            if (model == null)
            {
                _cachedConfig = SiteConfigDefaults.create();
                return _cachedConfig;
            }

            var config = SiteConfigDefaults.create();

            // Support both root-level keys and the conventional [site] / [build]
            // tables (e.g. `[site] title = "..."`, `[build] output = "_site"`).
            // The tables take precedence when present; root-level keys remain a
            // supported fallback so existing flat configs still work.
            var siteTbl = (model.TryGetValue("site", out var siteObj) && siteObj is Tomlyn.Model.TomlTable st) ? st : null;
            var buildTbl = (model.TryGetValue("build", out var buildObj) && buildObj is Tomlyn.Model.TomlTable bt) ? bt : null;

            string FromSiteStr(string key, string fallback) =>
                siteTbl != null ? TomlReader.GetString(siteTbl, key, fallback) : fallback;
            int FromSiteInt(string key, int fallback) =>
                siteTbl != null ? TomlReader.GetInt(siteTbl, key, fallback) : fallback;
            bool FromSiteBool(string key, bool fallback) =>
                siteTbl != null ? TomlReader.GetBool(siteTbl, key, fallback) : fallback;
            string FromBuildStr(string key, string fallback) =>
                buildTbl != null ? TomlReader.GetString(buildTbl, key, fallback) : fallback;

            var title = FromSiteStr("title", TomlReader.GetString(model, "title", config.Title));
            var baseUrl = FromSiteStr("url", TomlReader.GetString(model, "base_url", config.BaseUrl));
            var description = FromSiteStr("description", TomlReader.GetString(model, "description", config.Description));
            var defaultLayout = FromSiteStr("default_layout", TomlReader.GetString(model, "default_layout", config.DefaultLayout));
            var permalinkFormat = FromSiteStr("permalink_format", TomlReader.GetString(model, "permalink_format", config.PermalinkFormat));
            var siteVersion = FromSiteStr("site_version", TomlReader.GetString(model, "site_version", config.SiteVersion));
            var contentDir = FromSiteStr("content_dir", TomlReader.GetString(model, "content_dir", config.ContentDir));
            var outputDir = FromBuildStr("output", TomlReader.GetString(model, "output_dir", config.OutputDir));
            var layoutsDir = FromBuildStr("layouts_dir", TomlReader.GetString(model, "layouts_dir", config.LayoutsDir));
            var includesDir = FromBuildStr("includes_dir", TomlReader.GetString(model, "includes_dir", config.IncludesDir));
            var dataDir = FromBuildStr("data_dir", TomlReader.GetString(model, "data_dir", config.DataDir));
            var assetsDir = FromBuildStr("assets_dir", TomlReader.GetString(model, "assets_dir", config.AssetsDir));
            var rootDir = FromSiteStr("root_dir", TomlReader.GetString(model, "root_dir", config.RootDir));
            var devServerPort = FromSiteInt("dev_server_port", TomlReader.GetInt(model, "dev_server_port", config.DevServerPort));
            var liveReloadPort = FromSiteInt("live_reload_port", TomlReader.GetInt(model, "live_reload_port", config.LiveReloadPort));
            var enableMinification    = FromSiteBool("enable_minification",     TomlReader.GetBool(model, "enable_minification",     config.EnableMinification));
            var enableCacheBusting   = FromSiteBool("enable_cache_busting",    TomlReader.GetBool(model, "enable_cache_busting",    config.EnableCacheBusting));
            var enableParallel       = FromSiteBool("enable_parallel_build",   TomlReader.GetBool(model, "enable_parallel_build",   config.EnableParallelBuild));
            var enableIncremental    = FromSiteBool("enable_incremental_build",TomlReader.GetBool(model, "enable_incremental_build",config.EnableIncrementalBuild));
            var author               = FromSiteStr("author",   TomlReader.GetString(model, "author",   config.Author));
            var language             = FromSiteStr("language", TomlReader.GetString(model, "language", config.Language));
            var logLevel             = FromSiteStr("log_level", TomlReader.GetString(model, "log_level", config.LogLevel));
            var logToFile            = FromSiteBool("log_to_file", TomlReader.GetBool  (model, "log_to_file", config.LogToFile));
            var logTimestamps        = FromSiteBool("log_timestamps", TomlReader.GetBool  (model, "log_timestamps", config.LogTimestamps));
            var templateEngine       = TomlReader.GetString(model, "template_engine", config.TemplateEngine);

            // ── Parse [compat] table: SSG-specific behavior flags ──
            // Example:
            //   [compat]
            //   jekyll = true
            //   hexo   = false
            var compatJekyll  = config.CompatJekyll;
            var compatHexo    = config.CompatHexo;
            var compatHugo    = config.CompatHugo;
            var compatEleventy = config.CompatEleventy;
            if (model.TryGetValue("compat", out var compatObj) && compatObj is Tomlyn.Model.TomlTable compatTbl)
            {
                compatJekyll   = TomlReader.GetBool(compatTbl, "jekyll",   compatJekyll);
                compatHexo     = TomlReader.GetBool(compatTbl, "hexo",     compatHexo);
                compatHugo     = TomlReader.GetBool(compatTbl, "hugo",     compatHugo);
                compatEleventy = TomlReader.GetBool(compatTbl, "eleventy", compatEleventy);
            }

            // ── Parse [template] table: engine + nunjucks compatibility mode ──
            // Example:
            //   [template]
            //   engine = "native"
            //   [template.nunjucks]
            //   compatibility = "zest"   # "strict" | "zest"
            //
            // [template].engine overrides the top-level `template_engine` key
            // when present, so users can group template config in one table.
            var nunjucksCompat = config.NunjucksCompatibility;
            if (model.TryGetValue("template", out var tplObj) && tplObj is Tomlyn.Model.TomlTable tplTbl)
            {
                var tplEngine = TomlReader.GetString(tplTbl, "engine", templateEngine);
                if (!string.IsNullOrEmpty(tplEngine)) templateEngine = tplEngine;
                // Flat form: template.nunjucks_compatibility = "zest"
                nunjucksCompat = TomlReader.GetString(tplTbl, "nunjucks_compatibility", nunjucksCompat);
                // Nested form: [template.nunjucks] compatibility = "zest"
                if (tplTbl.TryGetValue("nunjucks", out var njObj) && njObj is Tomlyn.Model.TomlTable njTbl)
                {
                    nunjucksCompat = TomlReader.GetString(njTbl, "compatibility", nunjucksCompat);
                }
            }

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

            // Parse [menus.*] tables
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

            _cachedConfig = new SiteConfig(
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
                templateEngine: templateEngine,
                compatJekyll: compatJekyll,
                compatHexo: compatHexo,
                compatHugo: compatHugo,
                compatEleventy: compatEleventy,
                nunjucksCompatibility: nunjucksCompat
            );
            return _cachedConfig;
        }
        catch (Exception ex)
        {
            LogWriter.Error("Config", $"Failed to parse '{configPath}': {ex.Message}", ex);
            _cachedConfig = SiteConfigDefaults.create();
            return _cachedConfig;
        }
    }

    /// <summary>
    /// Clear the cached configuration so the next Load() call re-parses the config file.
    /// Useful for testing or when the caller knows the file has changed externally.
    /// </summary>
    public static void ClearCache()
    {
        _cachedConfig = null;
        _lastLoadTimeUtc = DateTime.MinValue;
        _lastConfigPath = null;
    }
}
