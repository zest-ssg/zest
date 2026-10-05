using Microsoft.FSharp.Collections;
using Tomlyn;
using Tomlyn.Model;
using Zest.Compiler.Model;
using Zest.Compiler.Build;
using Zest.App.Runtime;

#nullable enable

namespace Zest.App.Config;

/// <summary>
/// Loads SiteConfig from _config.toml. The file is entirely optional: when it
/// is absent, every field falls back to a working default so a site builds
/// with no configuration at all.
///
/// Only two tables are recognised. [site] holds identity and presentation;
/// [build] holds output and optimisation. Directory layout is convention and
/// is never configured here — see SitePaths.
///
/// The parsed result is cached and re-parsed only when the file's write time
/// changes, so repeated loads during a watch loop cost nothing.
/// </summary>
public static class ConfigLoader
{
    private static SiteConfig? _cachedConfig;
    private static DateTime _lastLoadTimeUtc;
    private static string? _lastConfigPath;

    /// <summary>
    /// Load site configuration from the given project root, or auto-detect it.
    /// Returns defaults when no _config.toml exists.
    /// </summary>
    public static SiteConfig Load(string? projectPath = null)
    {
        var root = RootFinder.Find(projectPath) ?? Directory.GetCurrentDirectory();
        var configPath = Path.Combine(root, "_config.toml");

        var currentWriteTime = File.Exists(configPath)
            ? File.GetLastWriteTimeUtc(configPath)
            : DateTime.MinValue;

        // Serve the cache only when the path and the write time are unchanged.
        if (_cachedConfig != null && _lastConfigPath == configPath && _lastLoadTimeUtc == currentWriteTime)
            return _cachedConfig;

        _lastConfigPath = configPath;
        _lastLoadTimeUtc = currentWriteTime;

        if (!File.Exists(configPath))
            return _cachedConfig = SiteConfigDefaults.create();

        try
        {
            var model = Toml.ToModel(File.ReadAllText(configPath));
            return _cachedConfig = model is null
                ? SiteConfigDefaults.create()
                : Parse(SiteConfigDefaults.create(), model);
        }
        catch (Exception ex)
        {
            LogWriter.Error("Config", $"Failed to parse '{configPath}': {ex.Message}", ex);
            return _cachedConfig = SiteConfigDefaults.create();
        }
    }

    private static SiteConfig Parse(SiteConfig config, TomlTable model)
    {
        var site = Table(model, "site");
        var build = Table(model, "build");

        // Trim the trailing slash so template concatenation with absolute page
        // paths ({{ site.base_url }}{{ page.url }}) cannot produce "//".
        var baseUrl = Str(site, "url", config.BaseUrl).TrimEnd('/');

        var paginationPerPage = config.PaginationPerPage;
        if (Table(model, "pagination") is { } pagination)
            paginationPerPage = Int(pagination, "per_page", paginationPerPage);

        return new SiteConfig(
            title: Str(site, "title", config.Title),
            baseUrl: baseUrl,
            description: Str(site, "description", config.Description),
            contentDir: Str(site, "content_dir", config.ContentDir),
            outputDir: Str(build, "output", config.OutputDir),
            defaultLayout: Str(site, "default_layout", config.DefaultLayout),
            permalinkFormat: Str(site, "permalink_format", config.PermalinkFormat),
            devServerPort: Int(site, "dev_server_port", config.DevServerPort),
            liveReloadPort: Int(site, "live_reload_port", config.LiveReloadPort),
            enableMinification: Bool(build, "minify", config.EnableMinification),
            enableAssetFormatting: Bool(build, "format_assets", config.EnableAssetFormatting),
            enableHtmlFormatting: Bool(build, "format_html", config.EnableHtmlFormatting),
            enableHtmlMinification: Bool(build, "minify_html", config.EnableHtmlMinification),
            enableCacheBusting: Bool(build, "cache_busting", config.EnableCacheBusting),
            siteVersion: Str(site, "version", config.SiteVersion),
            enableParallelBuild: Bool(build, "parallel", config.EnableParallelBuild),
            enableIncrementalBuild: Bool(build, "incremental", config.EnableIncrementalBuild),
            taxonomies: ParseTaxonomies(model, config.Taxonomies),
            menus: ParseMenus(model),
            author: Str(site, "author", config.Author),
            language: Str(site, "language", config.Language),
            logLevel: Str(site, "log_level", config.LogLevel),
            logToFile: Bool(site, "log_to_file", config.LogToFile),
            logTimestamps: Bool(site, "log_timestamps", config.LogTimestamps),
            zealucksCompatibility: ParseZealucksCompatibility(model, config.ZealucksCompatibility),
            include: ParseStringList(model, "include"),
            exclude: ParseStringList(model, "exclude"),
            paginationPerPage: paginationPerPage,
            pageDefaults: ParsePageDefaults(model),
            @params: ParseParams(model));
    }

    // ── Table accessors ────────────────────────────────────────

    private static TomlTable? Table(TomlTable model, string key) =>
        model.TryGetValue(key, out var value) && value is TomlTable table ? table : null;

    private static string Str(TomlTable? table, string key, string fallback) =>
        table is null ? fallback : TomlReader.GetString(table, key, fallback);

    private static int Int(TomlTable? table, string key, int fallback) =>
        table is null ? fallback : TomlReader.GetInt(table, key, fallback);

    private static bool Bool(TomlTable? table, string key, bool fallback) =>
        table is null ? fallback : TomlReader.GetBool(table, key, fallback);

    // ── Sections ───────────────────────────────────────────────

    /// <summary>Read the Zealucks filter-set mode: [template.zealucks] compatibility.</summary>
    private static string ParseZealucksCompatibility(TomlTable model, string fallback)
    {
        if (Table(model, "template") is not { } template) return fallback;
        if (Table(template, "zealucks") is not { } zealucks) return fallback;
        return TomlReader.GetString(zealucks, "compatibility", fallback);
    }

    /// <summary>Read [[taxonomies]] entries, e.g. name = "tag", plural = "tags".</summary>
    private static FSharpList<TaxonomyConfig> ParseTaxonomies(TomlTable model, FSharpList<TaxonomyConfig> fallback)
    {
        if (model.TryGetValue("taxonomies", out var value) is false || value is not TomlTableArray array)
            return fallback;

        var list = new List<TaxonomyConfig>();
        foreach (var entry in array)
            if (entry.TryGetValue("name", out var name) && entry.TryGetValue("plural", out var plural))
                list.Add(new TaxonomyConfig(name: name.ToString()!, plural: plural.ToString()!));

        return list.Count > 0 ? ListModule.OfSeq(list) : fallback;
    }

    /// <summary>Read [menu.*] tables into a language-agnostic navigation map.</summary>
    private static Dictionary<string, FSharpList<MenuItem>> ParseMenus(TomlTable model)
    {
        var menus = new Dictionary<string, FSharpList<MenuItem>>();
        if (Table(model, "menu") is not { } menuTable) return menus;

        foreach (var kv in menuTable)
        {
            if (kv.Value is not TomlTableArray entries) continue;

            var items = new List<MenuItem>();
            foreach (var entry in entries)
                items.Add(new MenuItem(
                    label:  entry.TryGetValue("label",  out var label)  ? label.ToString()!  : "",
                    url:    entry.TryGetValue("url",    out var url)    ? url.ToString()!    : "#",
                    weight: entry.TryGetValue("weight", out var weight) && weight is long w ? (int)w : 0));

            items.Sort((a, b) => a.Weight.CompareTo(b.Weight));
            menus[kv.Key] = ListModule.OfSeq(items);
        }
        return menus;
    }

    /// <summary>Read [[defaults]] entries: frontmatter defaults per glob pattern.</summary>
    private static FSharpList<PageDefaults> ParsePageDefaults(TomlTable model)
    {
        var result = new List<PageDefaults>();
        if (model.TryGetValue("defaults", out var value) is false || value is not TomlTableArray array)
            return ListModule.OfSeq(result);

        foreach (var item in array)
        {
            var path = TomlReader.GetString(item, "path", "");
            var values = new Dictionary<string, string>();
            if (item.TryGetValue("values", out var rawValues) && rawValues is TomlTable valueTable)
                foreach (var kv in valueTable)
                    values[kv.Key] = kv.Value?.ToString() ?? "";

            result.Add(new PageDefaults(path, MapModule.OfSeq(values.Select(kv => Tuple.Create(kv.Key, kv.Value)))));
        }
        return ListModule.OfSeq(result);
    }

    /// <summary>Read a plain string array such as include = ["README.md"].</summary>
    private static FSharpList<string> ParseStringList(TomlTable model, string key) =>
        model.TryGetValue(key, out var value) && value is TomlArray array
            ? ListModule.OfSeq(array.Select(x => x?.ToString() ?? ""))
            : ListModule.OfSeq(Array.Empty<string>());

    /// <summary>Read [params]: arbitrary values surfaced to templates as site.params.*.</summary>
    private static Dictionary<string, object> ParseParams(TomlTable model)
    {
        var result = new Dictionary<string, object>();
        if (Table(model, "params") is not { } table) return result;

        foreach (var kv in table)
            result[kv.Key] = TomlToNative(kv.Value);
        return result;
    }

    /// <summary>
    /// Recursively convert Tomlyn container values to plain .NET types so the
    /// <c>[params]</c> table is directly iterable in Zealucks and F# scripts.
    /// Mirrors <c>GlobalData.tomlToNative</c> in the F# engine: TomlTable →
    /// Dictionary, TomlArray / TomlTableArray → array; scalars pass through.
    /// </summary>
    private static object TomlToNative(object? v)
    {
        switch (v)
        {
            case TomlTable t:
                var d = new Dictionary<string, object>();
                foreach (var kv in t)
                    d[kv.Key] = TomlToNative(kv.Value);
                return d;
            case TomlArray a:
                return a.Select(TomlToNative).ToArray();
            case TomlTableArray ta:
                return ta.Select(TomlToNative).ToArray();
            case null:
                return null!;
            default:
                return v;
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
