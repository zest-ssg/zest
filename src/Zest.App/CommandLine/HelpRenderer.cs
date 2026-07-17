using System.Reflection;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Zest.App.CommandLine;

/// <summary>
/// Reads CLI metadata (branding, version, help text) from the bundled
/// <c>zest.toml</c> resource and provides static access to all sections.
///
/// <para><b>Resolution order:</b></para>
/// <list type="number">
///   <item><c>zest.toml</c> embedded as a manifest resource (used by the
///     installed <c>dotnet tool</c> — works with no file on disk).</item>
///   <item><c>zest.toml</c> on disk next to the executable (dev builds).</item>
///   <item><c>zest.toml</c> found by walking up to the repo root (local dev).</item>
///   <item>Hard-coded minimal defaults (last resort).</item>
/// </list>
/// Loaded once at first access via <see cref="Lazy{T}"/>.
/// </summary>
internal static class HelpRenderer
{
    private static readonly Lazy<TomlTable> _config = new(() => LoadConfig());

    private static TomlTable Config => _config.Value;

    // ── Config loading ─────────────────────────────────────

    private static TomlTable LoadConfig()
    {
        // 1) Embedded resource — always available in the packed tool.
        var embedded = TryReadEmbeddedConfig();
        if (embedded is not null)
            return embedded;

        // 2) File on disk (dev builds: next to exe, or walked up to repo root).
        var path = ResolveConfigPath();
        if (File.Exists(path))
        {
            try { return Toml.ToModel(File.ReadAllText(path, Encoding.UTF8)); }
            catch { /* fall through to defaults */ }
        }

        // 3) Hard-coded minimal defaults.
        return CreateDefaultTable();
    }

    /// <summary>
    /// Read the zest.toml content compiled in as the <c>zest.toml</c>
    /// manifest resource. Returns null if the resource is absent (e.g.
    /// running from a loose dev build without the EmbeddedResource item).
    /// </summary>
    private static TomlTable? TryReadEmbeddedConfig()
    {
        var asm = Assembly.GetExecutingAssembly();
        // LogicalName="zest.toml" in the .csproj maps to this resource name.
        using var stream = asm.GetManifestResourceStream("zest.toml");
        if (stream is null) return null;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        try { return Toml.ToModel(reader.ReadToEnd()); }
        catch { return null; }
    }

    // ── Path resolution (dev fallback) ─────────────────────

    private static string ResolveConfigPath()
    {
        // Dev build: next to the executable
        var exeDir = AppContext.BaseDirectory;
        var local = Path.Combine(exeDir, "zest.toml");
        if (File.Exists(local))
            return local;

        // Development: walk up from bin/<Config>/<TFM>/<RID>/ to find the
        // repo root containing zest.toml. Robust against RID subfolders
        // (e.g. win-x64) and framework version changes (net10.0 → net8.0).
        var dir = exeDir;
        for (var i = 0; i < 8; i++)
        {
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
            var candidate = Path.Combine(dir, "zest.toml");
            if (File.Exists(candidate))
                return candidate;
            // Stop at the drive root to avoid infinite loops.
            if (Path.GetPathRoot(dir) == dir)
                break;
        }

        // Fallback: assume the original 5-up guess (may not exist).
        return Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", "..", "..", "zest.toml"));
    }

    private static TomlTable CreateDefaultTable()
    {
        var t = new TomlTable();
        var meta = new TomlTable
        {
            ["version"] = "0.0.0",
            ["header"] = "Zest v{0} — Zealous Efficient Static Toolkit",
            ["ecosystem"] = "Ecosystem: .zest.fsx + .zcss"
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
        // Defensive: if [meta] is missing, fall back to empty rather than throw.
        if (!Config.TryGetValue("meta", out var metaObj) || metaObj is not TomlTable meta)
            return "";
        return meta.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    }

    private static string Get(string section, string key)
    {
        // Use TryGetValue instead of the indexer so a missing section
        // (e.g. when zest.toml is partial or absent) yields an empty
        // string rather than throwing KeyNotFoundException.
        if (!Config.TryGetValue(section, out var sectionObj) || sectionObj is not TomlTable table)
            return "";
        return table.TryGetValue(key, out var v) ? v?.ToString()?.Trim() ?? "" : "";
    }
}

