using System.Text;

namespace Zest.Infra.Services;

/// <summary>
/// Three-level logger (INFO / WARN / ERROR) with color-coded console output.
/// Supports a verbose mode for detailed FSI output.
/// </summary>
public static class Logger
{
    public enum Level
    {
        Info,
        Warn,
        Error
    }

    private static bool _verbose;
    private static bool _quiet;

    public static bool Verbose => _verbose;
    public static bool Quiet => _quiet;

    public static void SetVerbose(bool enabled) => _verbose = enabled;
    public static void SetQuiet(bool enabled) => _quiet = enabled;

    public static void Info(string message)
    {
        if (_quiet) return;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[INFO]  {message}");
        Console.Out.Flush();
        Console.ResetColor();
    }

    public static void Info(string tag, string message)
    {
        if (_quiet) return;
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"[INFO]  [{tag}] {message}");
        Console.Out.Flush();
        Console.ResetColor();
    }

    public static void Warn(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN]  {message}");
        Console.Out.Flush();
        Console.ResetColor();
    }

    public static void Warn(string tag, string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN]  [{tag}] {message}");
        Console.Out.Flush();
        Console.ResetColor();
    }

    public static void Error(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] {message}");
        Console.Error.Flush();
        Console.ResetColor();
    }

    public static void Error(string tag, string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] [{tag}] {message}");
        Console.Error.Flush();
        Console.ResetColor();
    }

    /// <summary>
    /// Log a detailed message only when --verbose is enabled.
    /// </summary>
    public static void VerboseLog(string message)
    {
        if (!_verbose) return;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[DEBUG] {message}");
        Console.ResetColor();
    }

    /// <summary>
    /// Log a request with method, path, status, and duration.
    /// </summary>
    public static void Request(string method, string path, int status, long durationMs)
    {
        if (_quiet) return;
        var statusColor = status switch
        {
            >= 200 and < 300 => ConsoleColor.Green,
            >= 300 and < 400 => ConsoleColor.Cyan,
            >= 400 and < 500 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        var statusStr = status.ToString();
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{method,-6} ");
        Console.ForegroundColor = statusColor;
        Console.Write($"{statusStr} ");
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"{path} ({durationMs}ms)");
        Console.ResetColor();
    }

    /// <summary>
    /// Print a banner with server info.
    /// </summary>
    public static void Banner(string title, string url, params (string label, string value)[] info)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine("  ┌─────────────────────────────────────────────┐");
        Console.WriteLine($"  │  {title,-43} │");
        Console.WriteLine("  ├─────────────────────────────────────────────┤");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  │  URL:  {url,-37} │");
        foreach (var (label, value) in info)
        {
            var line = $"  {label}: {value}";
            if (line.Length <= 47)
                Console.WriteLine($"  │  {label,-5}: {value,-37} │");
            else
                Console.WriteLine($"  │  {label,-5}: {value} │");
        }
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("  └─────────────────────────────────────────────┘");
        Console.ResetColor();
        Console.WriteLine();
    }
}
