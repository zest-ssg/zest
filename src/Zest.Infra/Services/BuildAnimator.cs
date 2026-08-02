using System.Diagnostics;
using System.Text;
using Zest.Engine;
using Zest.Engine.Build;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Animated terminal build indicator with live progress, spinner, and summary.
/// Runs a background timer that redraws a compact progress line every ~80ms,
/// polling the F# BuildProgress singleton for real-time counts.
/// Inspired by modern CLI tools (Vercel, Cargo) but with Zest's own flair:
/// a gradient bar, rotating glyph, and phase-aware labels.
/// </summary>
public static class BuildAnimator
{
    // ── Spinner glyphs (braille rotation) ──────────────────────
    // These cycle frame-by-frame to give a smooth spinning effect.
    private static readonly string[] _spinFrames =
        { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };

    // ── Bar segments (partial fill blocks) ─────────────────────
    // Combined with full/empty blocks to render a smooth progress bar.
    private static readonly string[] _barPartial =
        { "▏", "▎", "▍", "▌", "▋", "▊", "▉" };

    private const int _barWidth = 28;
    private const int _pollIntervalMs = 80;

    private static Timer? _timer;
    private static int _frame;
    private static Stopwatch _sw = new();
    private static bool _enabled;
    private static BuildProgress? _progress;

    /// <summary>Whether animation is currently active.</summary>
    public static bool IsActive => _enabled;

    /// <summary>
    /// Start the animated build indicator. Launches a background timer
    /// that redraws progress every 80ms. No-op if stdout is redirected
    /// (piped) or the logger is in quiet mode.
    /// </summary>
    public static void Start()
    {
        if (_enabled) return;

        // Skip animation when output is piped (CI, file redirect) —
        // ANSI control sequences would corrupt captured text.
        if (Console.IsOutputRedirected) return;
        if (LogWriter.Quiet) return;

        var p = ProgressTracker.tryGet();
        if (p is null || Microsoft.FSharp.Core.FSharpOption<BuildProgress>.get_IsNone(p)) return;

        _progress = p.Value;
        _enabled = true;
        _frame = 0;
        _sw.Restart();

        // Print a leading newline so the animation line stands clear.
        Console.WriteLine();

        // Use ThreadPool timer for non-blocking periodic redraw.
        _timer = new Timer(_ => Redraw(), null, 0, _pollIntervalMs);
    }

    /// <summary>
    /// Stop the animation and print the final build summary line.
    /// Removes the animation line and replaces it with a clean result.
    /// </summary>
    public static void Stop(BuildResult result)
    {
        if (!_enabled)
        {
            // Even without animation, print a clean summary.
            PrintPlainSummary(result);
            return;
        }

        if (_timer != null)
        {
            _timer.Dispose();
            _timer = null;
        }

        _enabled = false;

        // Clear the animation line.
        ClearAnimationLine();

        _sw.Stop();
        PrintSummary(result);
        _progress = null;
    }

    // ── Animation rendering ────────────────────────────────────

    /// <summary>
    /// Redraw the progress line in-place using carriage return.
    /// Layout:  ⠹ Building ▏▎▍▌▋▊▉████████░░░░░░░░░░░░ 42/128 · evaluating
    /// </summary>
    private static void Redraw()
    {
        if (!_enabled || _progress is null) return;

        var p = _progress;
        int total = p.TotalFiles;
        int done = p.Processed + p.Cached;
        double pct = total > 0 ? (double)done / total : 0;
        if (pct > 1.0) pct = 1.0;

        var spinner = _spinFrames[_frame % _spinFrames.Length];
        _frame++;

        var bar = RenderBar(pct);
        var phaseLabel = PhaseLabel(p.Phase);

        // Elapsed time (mm:ss.f)
        var elapsed = _sw.Elapsed;
        var timeStr = elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";

        // ── Color segments ──
        // Spinner: cyan when < 99%, green when effectively done.
        Console.ForegroundColor = pct >= 0.99 ? ConsoleColor.Green : ConsoleColor.Cyan;
        Console.Write($"\r  {spinner} ");

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write($"{phaseLabel} ");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"{bar} ");

        // Count
        Console.ForegroundColor = ConsoleColor.Gray;
        if (total > 0)
            Console.Write($"{done}/{total}");
        else
            Console.Write($"{done} files");

        // Cached indicator
        if (p.Cached > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write($" ({p.Cached} cached)");
        }

        // Time
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write($"  ·  {timeStr}");

        // Pad to overwrite any leftover chars from the previous frame.
        // The padding ensures stale characters don't linger at line end.
        Console.ResetColor();
        Console.Write("   \r");
    }

    /// <summary>
    /// Render a smooth gradient progress bar using Unicode block characters.
    /// Filled portion uses a cyan→green gradient feel (via ConsoleColor);
    /// empty portion uses dark gray partial blocks for a subtle texture.
    /// </summary>
    private static string RenderBar(double pct)
    {
        var sb = new StringBuilder(_barWidth + 2);
        sb.Append('▏');
        int filled = (int)(pct * _barWidth);
        double remainder = (pct * _barWidth) - filled;

        for (int i = 0; i < _barWidth; i++)
        {
            if (i < filled)
                sb.Append('█');
            else if (i == filled && remainder > 0)
            {
                int idx = (int)(remainder * _barPartial.Length);
                if (idx >= _barPartial.Length) idx = _barPartial.Length - 1;
                sb.Append(_barPartial[idx]);
            }
            else
                sb.Append('░');
        }
        sb.Append('▕');
        return sb.ToString();
    }

    /// <summary>Human-readable label for each build phase.</summary>
    private static string PhaseLabel(BuildPhase phase) => phase switch
    {
        BuildPhase.Initializing => "Initializing",
        BuildPhase.Discovering => "Scanning",
        BuildPhase.Evaluating  => "Building",
        BuildPhase.Writing     => "Writing",
        BuildPhase.Assets      => "Assets",
        BuildPhase.Finalizing  => "Finalizing",
        _                      => "Building"
    };

    // ── Summary rendering ──────────────────────────────────────

    /// <summary>
    /// Print the final build summary after the animation stops.
    /// Shows: success/fail glyph, file counts, elapsed time, output dir.
    /// </summary>
    private static void PrintSummary(BuildResult result)
    {
        var indent = "  ";
        var durationMs = result.DurationMs;
        var durationStr = FormatDuration(durationMs);

        // ── Status line ──
        if (result.Errors.IsEmpty)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{indent}✓ Build complete");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{indent}✗ Build failed ({result.Errors.Length} error(s))");
        }
        Console.ResetColor();

        // File counts
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"  {result.TotalPages} files");
        if (result.CachedPages > 0)
            Console.Write($" ({result.ProcessedPages} built, {result.CachedPages} cached)");
        else
            Console.Write($" ({result.ProcessedPages} built)");
        if (result.AssetsCopied > 0)
            Console.Write($", {result.AssetsCopied} assets");

        // Duration
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ·  ");
        WriteDurationColored(durationMs);
        Console.ResetColor();
        Console.WriteLine();

        // ── Detail lines ──
        if (result.Errors.IsEmpty)
        {
            // Output directory (only show on success to reduce noise)
            var outDir = _progress?.OutputDir;
            if (string.IsNullOrEmpty(outDir) && result.TotalPages > 0)
                outDir = Path.GetFullPath(Path.Combine(
                    Directory.GetCurrentDirectory(), "_site"));
            if (!string.IsNullOrEmpty(outDir))
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"{indent}  → ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(outDir);
                Console.ResetColor();
            }
        }
        else
        {
            // Print up to 5 error lines
            int shown = 0;
            foreach (var err in result.Errors)
            {
                if (shown >= 5) break;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.Write($"{indent}  ! ");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.WriteLine(err);
                Console.ResetColor();
                shown++;
            }
            if (result.Errors.Length > 5)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"{indent}  … and {result.Errors.Length - 5} more error(s)");
                Console.ResetColor();
            }
        }
    }

    /// <summary>
    /// Fallback summary when animation was skipped (piped output, quiet mode).
    /// Prints a single-line summary without ANSI control sequences.
    /// </summary>
    private static void PrintPlainSummary(BuildResult result)
    {
        if (LogWriter.Quiet || LogWriter.MinLevel > LogWriter.Level.Info) return;

        var indent = "  ";
        if (result.Errors.IsEmpty)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{indent}✓ Build");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"{indent}✗ Build");
        }
        Console.ResetColor();

        Console.ForegroundColor = ConsoleColor.Gray;
        Console.Write($"  {result.TotalPages} pages");
        if (result.CachedPages > 0)
            Console.Write($" ({result.ProcessedPages} built, {result.CachedPages} cached)");
        else
            Console.Write($" ({result.ProcessedPages} processed)");
        if (result.AssetsCopied > 0)
            Console.Write($", {result.AssetsCopied} assets");

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.Write("  ·  ");
        WriteDurationColored(result.DurationMs);
        Console.ResetColor();
        Console.WriteLine();
    }

    /// <summary>Write duration with color based on speed thresholds.</summary>
    private static void WriteDurationColored(long ms)
    {
        Console.ForegroundColor = ms < 100 ? ConsoleColor.Green
                             : ms < 500 ? ConsoleColor.Yellow
                             : ConsoleColor.Red;
        Console.Write(FormatDuration(ms));
        Console.ResetColor();
    }

    /// <summary>Format milliseconds into a human-readable duration string.</summary>
    private static string FormatDuration(long ms)
    {
        if (ms < 1000) return $"{ms}ms";
        if (ms < 60_000) return $"{ms / 1000.0:F2}s";
        int m = (int)(ms / 60_000);
        int s = (int)((ms % 60_000) / 1000);
        return $"{m}m{s:D2}s";
    }

    /// <summary>Clear the current animation line using ANSI or backspaces.</summary>
    private static void ClearAnimationLine()
    {
        // \r + spaces is the most portable way to clear the line.
        Console.Write("\r");
        Console.Write(new string(' ', Console.WindowWidth > 0 ? Math.Min(Console.WindowWidth - 1, 100) : 80));
        Console.Write("\r");
    }
}
