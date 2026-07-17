using Zest.Dsl;
using Zest.Engine;
using Zest.Infra.Services;

namespace Zest.App.Controllers;

/// <summary>
/// Handles `zest convert-config &lt;from&gt; &lt;to&gt;`
/// Converts between YAML and TOML configuration formats in the current
/// directory (e.g. _config.yml → _config.toml).
/// </summary>
public static class ConfigConverter
{
    public static int Execute(string[] args)
    {
        // Args layout: [ "convert-config", from, to ]
        if (args.Length < 3)
        {
            LogWriter.WriteError("  Usage: zest convert-config <from> <to>");
            LogWriter.WriteDim("    from/to: yaml | toml");
            LogWriter.WriteDim("    Example: zest convert-config yaml toml");
            return 1;
        }

        var from = args[1].ToLowerInvariant();
        var to = args[2].ToLowerInvariant();

        if ((from != "yaml" && from != "yml" && from != "toml") ||
            (to != "yaml" && to != "yml" && to != "toml"))
        {
            LogWriter.WriteError("  Error: <from> and <to> must be 'yaml' or 'toml'.");
            return 1;
        }

        // Normalize 'yml' → 'yaml' before comparison so that
        // `zest convert-config yml yaml` is a same-format no-op.
        var fromNorm = from == "yml" ? "yaml" : from;
        var toNorm = to == "yml" ? "yaml" : to;

        if (fromNorm == toNorm)
        {
            LogWriter.WriteWarning("  Source and target formats are identical; nothing to do.");
            return 0;
        }

        var srcExt = fromNorm == "yaml" ? FileExtensions.Yaml : FileExtensions.Toml;
        var dstExt = toNorm == "yaml" ? FileExtensions.Yaml : FileExtensions.Toml;

        // Prefer _config.<ext>, fall back to config.<ext>
        var candidates = new[] { $"_config{srcExt}", $"config{srcExt}" };
        var srcPath = candidates.FirstOrDefault(File.Exists);

        if (srcPath is null)
        {
            LogWriter.WriteError($"  Error: No source config found. Looked for: {string.Join(", ", candidates)}");
            return 1;
        }

        var dstPath = Path.ChangeExtension(srcPath, dstExt);
        if (!dstPath.EndsWith("_config" + dstExt, StringComparison.Ordinal)
            && !dstPath.EndsWith("config" + dstExt, StringComparison.Ordinal))
        {
            dstPath = srcPath.Replace(srcExt, dstExt);
        }

        var sourceText = File.ReadAllText(srcPath, System.Text.Encoding.UTF8);
        string converted;

        try
        {
            converted = fromNorm == "yaml"
                ? DslMigration.convertYamlToToml(sourceText)
                : DslMigration.convertTomlToYaml(sourceText);
        }
        catch (Exception ex)
        {
            LogWriter.WriteError($"  Error converting config: {ex.Message}");
            return 1;
        }

        File.WriteAllText(dstPath, converted, System.Text.Encoding.UTF8);
        LogWriter.WriteSuccess($"  [Zest] Converted {srcPath} → {dstPath}");
        return 0;
    }
}
