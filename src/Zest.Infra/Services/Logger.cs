using System.Diagnostics;
using System.Text;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Comprehensive logging system for Zest.
/// Supports four log levels: Debug, Info, Warn, Error.
///
/// Features:
///   - Structured log format: timestamp | level | [module] message
///   - ERROR level includes full exception stack trace
///   - Configurable level, file mirroring, and timestamps via <see cref="Logger.Configure"/>
///   - Debug output only when level is Debug (or verbose)
///   - Thread-safe file logging
/// </summary>
public static class Logger
{
    public enum Level
    {
        Debug = 0,
        Info  = 1,
        Warn  = 2,
        Error = 3,
        Off   = 4
    }

    // ── Configuration ──────────────────────────────────────
    private static Level _minLevel = Level.Info;
    private static bool _quiet;
    private static bool _logToFile;
    private static bool _logTimestamps = true;
    private static string? _logFilePath;
    private static readonly object _fileLock = new();

    /// <summary>Current minimum log level (entries below this are filtered out).</summary>
    public static Level MinLevel => _minLevel;

    /// <summary>Legacy verbose flag — maps to Debug level.</summary>
    public static bool Verbose => _minLevel <= Level.Debug;

    /// <summary>Legacy quiet flag — when true, Info messages are suppressed.</summary>
    public static bool Quiet => _quiet;

    /// <summary>Configure the logger from <see cref="Zest.Engine.SiteConfig"/>.</summary>
    public static void Configure(string level, bool toFile, bool timestamps)
    {
        _minLevel = ParseLevel(level);
        _logToFile = toFile;
        _logTimestamps = timestamps;
    }

    /// <summary>Configure the logger with explicit values.</summary>
    public static void Configure(Level minLevel, bool toFile = false, bool timestamps = true, string? logFilePath = null)
    {
        _minLevel = minLevel;
        _logToFile = toFile;
        _logTimestamps = timestamps;
        _logFilePath = logFilePath;
    }

    public static void SetVerbose(bool enabled) =>
        _minLevel = enabled ? Level.Debug : Level.Info;

    public static void SetQuiet(bool enabled) => _quiet = enabled;

    /// <summary>Parse a level name (case-insensitive). Unknown values fall back to Info.</summary>
    public static Level ParseLevel(string name) =>
        (name ?? "").Trim().ToLowerInvariant() switch
        {
            "debug" or "trace" or "verbose" => Level.Debug,
            "info"  or "information"         => Level.Info,
            "warn"  or "warning"            => Level.Warn,
            "error"                          => Level.Error,
            "off"  or "none"  or "silent"   => Level.Off,
            _ => Level.Info
        };

    // ── Core logging entry points ──────────────────────────

    /// <summary>Lowest level — only emitted when level is Debug (or via --verbose).</summary>
    public static void Debug(string message) => Log(Level.Debug, null, message, null);

    /// <summary>Lowest level with module tag.</summary>
    public static void Debug(string module, string message) => Log(Level.Debug, module, message, null);

    /// <summary>Informational message.</summary>
    public static void Info(string message) => Log(Level.Info, null, message, null);

    /// <summary>Informational message with module tag.</summary>
    public static void Info(string module, string message) => Log(Level.Info, module, message, null);

    /// <summary>Warning — non-fatal issue worth noting.</summary>
    public static void Warn(string message) => Log(Level.Warn, null, message, null);

    /// <summary>Warning with module tag.</summary>
    public static void Warn(string module, string message) => Log(Level.Warn, module, message, null);

    /// <summary>Error with full exception stack trace.</summary>
    public static void Error(string message, Exception? ex = null) =>
        Log(Level.Error, null, message, ex);

    /// <summary>Error with module tag and full exception stack trace.</summary>
    public static void Error(string module, string message, Exception? ex = null) =>
        Log(Level.Error, module, message, ex);

    /// <summary>Legacy verbose-only log (kept for compatibility — now calls Debug).</summary>
    public static void VerboseLog(string message) => Debug(message);

    /// <summary>Log a request with method, path, status, and duration.</summary>
    public static void Request(string method, string path, int status, long durationMs)
    {
        if (_quiet || _minLevel > Level.Info) return;
        var statusColor = status switch
        {
            >= 200 and < 300 => ConsoleColor.Green,
            >= 300 and < 400 => ConsoleColor.Cyan,
            >= 400 and < 500 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{_indent}{method,-6} ");
        Console.ForegroundColor = statusColor;
        Console.Write($"{status} ");
        Console.ForegroundColor = ConsoleColor.Gray;
        WriteDuration(durationMs);
        Console.Write($" {path}");
        Console.ResetColor();
        Console.WriteLine();
    }

    private static void WriteDuration(long ms)
    {
        Console.ForegroundColor = ms < 100 ? ConsoleColor.Green : ms < 500 ? ConsoleColor.Yellow : ConsoleColor.Red;
        Console.Write($"{ms}ms");
        Console.ResetColor();
    }

    // ── Color helpers ──────────────────────────────────────

    private const string _indent = "  ";
    private static readonly string _separator = new('─', 52);

    /// <summary>Write a success message (green text).</summary>
    public static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write a warning message (yellow text).</summary>
    public static void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write an error message (red text).</summary>
    public static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write an info message (default gray text).</summary>
    public static void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write an accent message (cyan text — for headers and emphasis).</summary>
    public static void WriteAccent(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write a dim message (dark gray — for secondary/helper text).</summary>
    public static void WriteDim(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    /// <summary>Write a section header with underline separator.</summary>
    public static void WriteSection(string title)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine();
        Console.WriteLine($"{_indent}── {title} ─{_separator}");
        Console.ResetColor();
    }

    /// <summary>Write a label-value pair with aligned columns.</summary>
    public static void WriteField(string label, string value, int labelWidth = 8)
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{_indent}  {label}");
        Console.ResetColor();
        var pad = labelWidth - label.Length;
        if (pad > 0) Console.Write(new string(' ', pad));
        Console.Write(" ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(value);
        Console.ResetColor();
    }

    /// <summary>Write a compact header footer line using accent color.</summary>
    public static void WriteRule()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{_indent}{new string('─', 52)}");
        Console.ResetColor();
    }

    /// <summary>Print a banner with server info in a clean aligned format.</summary>
    public static void Banner(string title, string url, params (string label, string value)[] info)
    {
        var titlePrefix = $"── {title} ";
        var fillLen = _separator.Length - titlePrefix.Length;
        if (fillLen < 0) fillLen = 0;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"{_indent}{titlePrefix}{new string('─', fillLen)}");
        Console.ResetColor();
        Console.WriteLine();
        WriteField("URL", url);
        foreach (var (label, value) in info)
        {
            WriteField(label, value);
        }
        WriteRule();
        Console.WriteLine();
    }

    // ── Internals ──────────────────────────────────────────

    private static void Log(Level level, string? module, string message, Exception? ex)
    {
        if (level < _minLevel) return;
        if (level == Level.Info && _quiet) return;

        var sb = new StringBuilder(128);
        if (_logTimestamps)
        {
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff"));
            sb.Append(' ');
        }
        sb.Append('[');
        sb.Append(level.ToString().ToUpperInvariant());
        sb.Append("] ");
        if (!string.IsNullOrEmpty(module))
        {
            sb.Append('[');
            sb.Append(module);
            sb.Append("] ");
        }
        sb.Append(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append("       ");
            sb.Append(ex.GetType().FullName);
            sb.Append(": ");
            sb.AppendLine(ex.Message);
            sb.AppendLine(ex.StackTrace ?? "<no stack trace>");
            if (ex.InnerException != null)
            {
                sb.AppendLine("       --- inner exception ---");
                sb.Append("       ");
                sb.Append(ex.InnerException.GetType().FullName);
                sb.Append(": ");
                sb.AppendLine(ex.InnerException.Message);
                sb.AppendLine(ex.InnerException.StackTrace ?? "<no inner stack>");
            }
        }
        var line = sb.ToString();

        // Console output
        var color = level switch
        {
            Level.Debug => ConsoleColor.DarkGray,
            Level.Info  => ConsoleColor.Gray,
            Level.Warn  => ConsoleColor.Yellow,
            Level.Error => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
        try
        {
            Console.ForegroundColor = color;
            if (level == Level.Error)
            {
                Console.Error.WriteLine(line.TrimEnd());
                Console.Error.Flush();
            }
            else
            {
                Console.WriteLine(line.TrimEnd());
                Console.Out.Flush();
            }
            Console.ResetColor();
        }
        catch { /* ignore IO errors during logging */ }

        // File mirror
        if (_logToFile)
        {
            try
            {
                lock (_fileLock)
                {
                    var path = _logFilePath ?? DefaultLogPath();
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { /* never let file logging crash the host */ }
        }
    }

    private static string DefaultLogPath() =>
        Path.Combine(Directory.GetCurrentDirectory(), ".zest", "logs", "zest.log");

    /// <summary>Convenience: measure elapsed time around an action and log it.</summary>
    public static void Time(string module, string label, Action action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            action();
            sw.Stop();
            Debug(module, $"{label} completed in {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Error(module, $"{label} failed after {sw.ElapsedMilliseconds}ms: {ex.Message}", ex);
            throw;
        }
    }

    /// <summary>Format a build summary with nice colors.</summary>
    public static void BuildSummary(string module, int totalPages, int processed, int cached, int assets, long durationMs)
    {
        if (_quiet || _minLevel > Level.Info) return;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write($"{_indent}✓ {module}");
        Console.ResetColor();
        Console.Write($"  {totalPages} pages");
        if (cached > 0) Console.Write($" ({processed} new, {cached} cached)");
        else Console.Write($" ({processed} processed)");
        if (assets > 0) Console.Write($", {assets} assets");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ·  ");
        WriteDuration(durationMs);
        Console.ResetColor();
        Console.WriteLine();
    }
}
