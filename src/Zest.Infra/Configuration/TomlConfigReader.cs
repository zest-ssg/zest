#nullable enable

namespace Zest.Infra.Configuration;

/// <summary>
/// TOML config value extraction helpers.
/// Provides typed accessors for string/int/bool with fallback defaults.
/// </summary>
internal static class TomlConfigReader
{
    public static string GetString(IDictionary<string, object> dict, string key, string fallback)
    {
        if (dict.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
            return s;
        return fallback;
    }

    public static int GetInt(IDictionary<string, object> dict, string key, int fallback)
    {
        if (dict.TryGetValue(key, out var val))
        {
            if (val is int i) return i;
            if (val is long l) return (int)l;
        }
        return fallback;
    }

    public static bool GetBool(IDictionary<string, object> dict, string key, bool fallback)
    {
        if (dict.TryGetValue(key, out var val) && val is bool b)
            return b;
        return fallback;
    }
}
