using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Zest.App.CommandLine;

/// <summary>
/// Reads CLI metadata from root zest.toml and provides static access
/// to all branding text, version info, and help content.
/// Loaded once at first access via Lazy&lt;T&gt;.
/// </summary>
internal static class HelpRenderer
{
    private static readonly Lazy<TomlTable> _config = new(() =>
    {
        var path = ResolveConfigPath();
        if (!File.Exists(path))
            return CreateDefaultTable();

        var text = File.ReadAllText(path, Encoding.UTF8);
        return Toml.ToModel(text);
    });

    private static TomlTable Config => _config.Value;

    // ── Path resolution ────────────────────────────────────

    private static string ResolveConfigPath()
    {
        // Published: next to the executable
        var exeDir = AppContext.BaseDirectory;
        var local = Path.Combine(exeDir, "zest.toml");
        if (File.Exists(local))
            return local;

        // Development: 5 levels up from bin/Debug/net10.0/ → repo root
        var dev = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "zest.toml"));
        return dev;
    }

    private static TomlTable CreateDefaultTable()
    {
        var t = new TomlTable();
        var meta = new TomlTable
        {
            ["version"] = "1.1.2",
            ["header"] = "Zest v{0} — Zenith Efficient Static Toolkit",
            ["ecosystem"] = "Ecosystem: .zpage.fsx + .zcss + .toml + .js"
        };
        t["meta"] = meta;
        return t;
    }

    // ── Public API ─────────────────────────────────────────

    public static string Version => GetMeta("version");
    public static string Header => GetMeta("header");
    public static string Ecosystem => GetMeta("ecosystem");

    public static string Usage => Get("usage", "general");
    public static string Commands => Get("commands", "build");
    public static string CommandsServe => Get("commands", "serve");
    public static string CommandsPreview => Get("commands", "preview");
    public static string CommandsInit => Get("commands", "init");
    public static string CommandsVersion => Get("commands", "version");
    public static string CommandsHelp => Get("commands", "help");
    public static string Options => Get("options", "port");
    public static string OptionsWatch => Get("options", "watch");
    public static string FileFormats => Get("formats", "items");
    public static string HelpSuffix => Get("suffix", "help_message");

    // ── Helpers ────────────────────────────────────────────

    private static string GetMeta(string key)
    {
        var meta = Config["meta"] as TomlTable;
        return meta?.TryGetValue(key, out var v) == true ? v?.ToString() ?? "" : "";
    }

    private static string Get(string section, string key)
    {
        var table = Config[section] as TomlTable;
        return table?.TryGetValue(key, out var v) == true ? v?.ToString()?.Trim() ?? "" : "";
    }
}
