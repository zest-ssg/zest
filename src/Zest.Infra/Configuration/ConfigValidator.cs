using Zest.Engine;

#nullable enable

namespace Zest.Infra.Configuration;

/// <summary>
/// Validates the loaded SiteConfig and logs warnings for common misconfigurations.
/// </summary>
public static class ConfigValidator
{
    /// <summary>
    /// Validate the loaded configuration and log warnings for common issues.
    /// </summary>
    public static void Validate(SiteConfig config)
    {
        var contentDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), config.EffectiveContentDir.TrimStart('.', '\\', '/')));
        if (!Directory.Exists(contentDir))
            Services.Logger.Warn("Config", $"Content directory not found: {contentDir}");

        var layoutsDir = Path.GetFullPath(Path.Combine(
            Directory.GetCurrentDirectory(), config.LayoutsDir.TrimStart('.', '\\', '/')));
        if (!Directory.Exists(layoutsDir))
            Services.Logger.Warn("Config", $"Layouts directory not found: {layoutsDir}");

        if (string.IsNullOrEmpty(config.Title))
            Services.Logger.Warn("Config", "Site title is empty");

        if (string.IsNullOrEmpty(config.BaseUrl))
            Services.Logger.Warn("Config", "Base URL is empty; absolute URLs will not be generated");

        if (config.DevServerPort == config.LiveReloadPort)
            Services.Logger.Warn("Config", "Dev server port and live reload port are the same; this may cause conflicts");
    }
}
