#nullable enable

namespace Zest.App.Config;

/// <summary>
/// Thrown when <c>_config.toml</c> exists but cannot be used: it is missing,
/// unreadable, malformed, or it declares values outside the supported
/// contract (for example an output directory that escapes the project root).
///
/// Configuration is a system boundary, so the failure is explicit: a broken
/// config never silently degrades into a default site that looks built.
/// Callers that must keep working regardless (e.g. <c>zest clean</c>) catch
/// this type deliberately.
/// </summary>
public sealed class ConfigException : Exception
{
    public ConfigException(string message)
        : base(message)
    {
    }

    public ConfigException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
