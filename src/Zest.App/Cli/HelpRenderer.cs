using System.Reflection;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Zest.App.Cli;

/// <summary>
/// Reads CLI metadata (branding, version, and the help footer) from the
/// bundled <c>.config/zest/</c> resources and provides static access to it.
/// The configuration is split by concern — <c>meta.toml</c> carries identity
/// and branding, <c>help.toml</c> carries user-facing copy — and the two are
/// merged into one table at load time.
/// </summary>
///
/// <para><b>Resolution order:</b></para>
/// <list type="number">
///   <item><c>zest.meta.toml</c> and <c>zest.help.toml</c> embedded as manifest
///     resources (used by the installed <c>dotnet tool</c> — works with no file
///     on disk).</item>
///   <item>The same two files under <c>.config/zest/</c>, found by walking up to
///     the repository root (local dev).</item>
///   <item>Hard-coded minimal defaults (last resort).</item>
/// </list>
/// Loaded once at first access via <see cref="Lazy{T}"/>.
internal static class HelpRenderer
{
    /// One row of the help table: an invocation and what it does.
    internal sealed record HelpRow(string Usage, string Description);

    /// Manifest resource names produced by the EmbeddedResource items in the csproj.
    private static readonly string[] ResourceNames = { "zest.meta.toml", "zest.help.toml" };

    /// File names looked up under <c>.config/zest/</c> when no resource is embedded.
    private static readonly string[] FileNames = { "meta.toml", "help.toml" };

    private static readonly Lazy<TomlTable> _config = new(() => LoadConfig());

    private static TomlTable Config => _config.Value;

    // ── Config loading ─────────────────────────────────────

    private static TomlTable LoadConfig()
    {
        // 1) Embedded resources — always available in the packed tool.
        var embedded = TryReadEmbeddedConfig();
        if (embedded is not null)
            return embedded;

        // 2) Files on disk (local dev).
        var onDisk = TryReadDirectoryConfig(ResolveConfigDirectory());
        if (onDisk is not null)
            return onDisk;

        // 3) Hard-coded minimal defaults.
        return CreateDefaultTable();
    }

    /// <summary>
    /// Parse and merge every embedded CLI config resource. Returns null when
    /// the resources are absent (e.g. running from a loose dev build without
    /// the EmbeddedResource items).
    /// </summary>
    private static TomlTable? TryReadEmbeddedConfig()
    {
        var asm = Assembly.GetExecutingAssembly();
        TomlTable? merged = null;
        foreach (var name in ResourceNames)
        {
            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null) continue;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            try
            {
                var table = Toml.ToModel(reader.ReadToEnd());
                merged = Merge(merged, table);
            }
            catch { /* skip a malformed fragment, keep the rest */ }
        }
        return merged;
    }

    /// <summary>
    /// Parse and merge every CLI config file in <paramref name="dir"/>.
    /// Returns null when the directory holds no readable config.
    /// </summary>
    private static TomlTable? TryReadDirectoryConfig(string? dir)
    {
        if (dir is null || !Directory.Exists(dir)) return null;
        TomlTable? merged = null;
        foreach (var name in FileNames)
        {
            var path = Path.Combine(dir, name);
            if (!File.Exists(path)) continue;
            try
            {
                var table = Toml.ToModel(File.ReadAllText(path, Encoding.UTF8));
                merged = Merge(merged, table);
            }
            catch { /* skip a malformed fragment, keep the rest */ }
        }
        return merged;
    }

    /// <summary>
    /// Merge <paramref name="source"/> into <paramref name="target"/>. Later
    /// files win on a key collision, which keeps help.toml able to override
    /// branding without editing meta.toml.
    /// </summary>
    private static TomlTable Merge(TomlTable? target, TomlTable source)
    {
        var result = target ?? new TomlTable();
        foreach (var kv in source)
            result[kv.Key] = kv.Value;
        return result;
    }

    // ── Path resolution (dev fallback) ─────────────────────

    /// <summary>
    /// Locate <c>.config/zest</c>: next to the executable first, then by
    /// walking up towards the repository root. Returns null when absent.
    /// </summary>
    private static string? ResolveConfigDirectory()
    {
        var exeDir = AppContext.BaseDirectory;
        var local = Path.Combine(exeDir, ".config", "zest");
        if (Directory.Exists(local))
            return local;

        // Walk up from bin/<Config>/<TFM>/<RID>/ to the repo root. Robust
        // against RID subfolders (e.g. win-x64) and framework version changes.
        var dir = exeDir;
        for (var i = 0; i < 8; i++)
        {
            dir = Path.GetFullPath(Path.Combine(dir, ".."));
            var candidate = Path.Combine(dir, ".config", "zest");
            if (Directory.Exists(candidate))
                return candidate;
            // Stop at the drive root to avoid infinite loops.
            if (Path.GetPathRoot(dir) == dir)
                break;
        }

        return null;
    }

    private static TomlTable CreateDefaultTable()
    {
        var t = new TomlTable();
        var meta = new TomlTable
        {
            ["version"] = "0.0.0",
            ["header"] = "Zest v{0} — Zealous Efficient Static Toolkit",
            ["ecosystem"] = "Ecosystem: .zlk + .zest.fsx + .zcss"
        };
        t["meta"] = meta;
        return t;
    }

    // ── Public API ─────────────────────────────────────────

    public static string Version => GetMeta("version");
    public static string Header => GetMeta("header");
    public static string Ecosystem => GetMeta("ecosystem");
    public static string HelpSuffix => Get("suffix", "help_message");

    /// Commands listed under [[command]] in help.toml, in file order.
    public static IReadOnlyList<HelpRow> Commands => Rows("command");

    /// Options listed under [[option]] in help.toml, in file order.
    public static IReadOnlyList<HelpRow> Options => Rows("option");

    private static HelpRow[] Rows(string section)
    {
        if (!Config.TryGetValue(section, out var value) || value is not TomlTableArray rows)
            return Array.Empty<HelpRow>();

        return rows
            .Select(row => new HelpRow(
                row.TryGetValue("usage", out var u) ? u?.ToString() ?? "" : "",
                row.TryGetValue("desc", out var d) ? d?.ToString() ?? "" : ""))
            .ToArray();
    }

    /// Usage line of a command-specific help page, e.g. [build] usage.
    public static string CommandUsage(string command)
    {
        if (!Config.TryGetValue(command, out var value) || value is not TomlTable page)
            return "";
        return page.TryGetValue("usage", out var u) ? u?.ToString() ?? "" : "";
    }

    /// Option rows of a command-specific help page, e.g. [build] options.
    public static IReadOnlyList<string> CliOptions(string command)
    {
        if (!Config.TryGetValue(command, out var value)
            || value is not TomlTable page
            || !page.TryGetValue("options", out var raw)
            || raw is not TomlArray options)
            return Array.Empty<string>();

        return options.Select(o => o?.ToString() ?? "").ToArray();
    }

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
        // (e.g. when a config fragment is absent) yields an empty
        // string rather than throwing KeyNotFoundException.
        if (!Config.TryGetValue(section, out var sectionObj) || sectionObj is not TomlTable table)
            return "";
        return table.TryGetValue(key, out var v) ? v?.ToString()?.Trim() ?? "" : "";
    }
}
