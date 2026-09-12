using System.Diagnostics;
using System.Linq;
using System.Text;
using Microsoft.FSharp.Core;
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

    // Every console mutation holds this lock: timer redraws, log lines
    // forwarded through CoordinatedWriter, and Stop all serialize on it,
    // so a log message can never land in the middle of a frame paint.
    private static readonly object _consoleLock = new();

    private static Timer? _timer;
    private static int _frame;
    private static Stopwatch _sw = new();
    private static bool _enabled;

    // Length (in console columns) of the widest frame currently on screen.
    // Each new frame pads to at least this width so shrinking text
    // (e.g. "Initializing" → "Assets") never leaves stale glyphs.
    private static int _lastFrameLength;

    private static TextWriter? _rawOut;
    private static TextWriter? _rawError;
    private static CoordinatedWriter? _outProxy;
    private static CoordinatedWriter? _errorProxy;

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
        // carriage returns would corrupt captured text.
        if (Console.IsOutputRedirected) return;
        if (LogWriter.Quiet) return;

        // Note: the BuildProgress singleton is created inside BuildEngine.execute
        // (ProgressTracker.start), which runs AFTER this method in BuildService.
        // ReadFrame therefore polls ProgressTracker.tryGet() live instead of
        // capturing a snapshot here.
        _enabled = true;
        _frame = 0;
        _lastFrameLength = 0;
        _sw.Restart();

        // Reroute Console.Out/Console.Error while the build runs so engine
        // output (timing lines, warnings, F# eprintfn) erases the animation
        // line first instead of overwriting it mid-frame. The animator and
        // the final summary bypass the wrappers via the saved raw writers.
        _rawOut = Console.Out;
        _rawError = Console.Error;
        _outProxy = new CoordinatedWriter(_rawOut);
        Console.SetOut(_outProxy);
        if (!ReferenceEquals(_rawOut, _rawError))
        {
            _errorProxy = new CoordinatedWriter(_rawError);
            Console.SetError(_errorProxy);
        }

        // Print a leading newline so the animation line stands clear.
        _rawOut.WriteLine();

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

        // Dispose the timer AND wait for an in-flight Redraw callback to
        // finish. Plain Dispose() is not synchronous: a callback already
        // queued on the thread pool could otherwise repaint a frame on top
        // of the summary line during Stop.
        var timer = _timer;
        _timer = null;
        if (timer is not null)
        {
            try
            {
                using var timerStopped = new ManualResetEvent(false);
                timer.Dispose(timerStopped);
                timerStopped.WaitOne(500);
            }
            catch { /* A failed paint tick may already have disposed the timer. */ }
        }

        _enabled = false;

        lock (_consoleLock)
        {
            RestoreWriters();
            ClearAnimationLine();
        }

        _sw.Stop();
        PrintSummary(result);
    }

    // ── Animation rendering ────────────────────────────────────

    /// <summary>
    /// Redraw the progress line in place using a carriage return.
    /// Layout:  ⠹ Building ▏▎▍▌▋▊▉████████░░░░░░░░░░░░ 42/128  ·  12.3s
    /// </summary>
    private static void Redraw()
    {
        // A single IO failure (lost console handle, a redirected stream
        // appearing after start) must never surface on the timer thread,
        // where an unhandled exception would kill the whole process.
        try
        {
            if (!_enabled || _rawOut is null) return;

            var parts = ReadFrame();
            if (parts is not null)
                PaintFrame(parts);
        }
        catch
        {
            DisableAfterPaintingFailure();
        }
    }

    /// <summary>
    /// Snapshot the live progress singleton and turn it into the colored
    /// segments of the current frame. Returns null while the tracker does
    /// not exist yet or the current tick has nothing to render.
    /// </summary>
    private static List<FramePart>? ReadFrame()
    {
        // The singleton is created inside BuildEngine.execute
        // (ProgressTracker.start), which runs after Start, so poll it live.
        var maybe = ProgressTracker.tryGet();
        if (maybe is null || FSharpOption<BuildProgress>.get_IsNone(maybe)) return null;
        var p = maybe.Value;

        int total = p.TotalFiles;
        int done = p.Processed + p.Cached;
        double pct = total > 0 ? (double)done / total : 0.0;
        if (pct > 1.0) pct = 1.0;

        var spinner = _spinFrames[_frame % _spinFrames.Length];
        _frame++;

        var stats = new FrameStats(done, total, pct);
        return BuildParts(p, stats, spinner, FormatElapsed(_sw.Elapsed));
    }

    /// <summary>One colored text segment painted as part of a frame.</summary>
    /// <param name="Text">Literal text; every glyph used here occupies one terminal column.</param>
    /// <param name="Color">Console color applied while writing the text.</param>
    private readonly record struct FramePart(string Text, ConsoleColor Color);

    /// <summary>Progress counters shared while building one frame.</summary>
    private readonly record struct FrameStats(int Done, int Total, double Pct);

    /// <summary>Build the ordered colored segments painted for one frame.</summary>
    private static List<FramePart> BuildParts(BuildProgress progress, FrameStats stats, string spinner, string time)
    {
        var parts = new List<FramePart>(8)
        {
            new($"  {spinner} ", stats.Pct >= 0.99 ? ConsoleColor.Green : ConsoleColor.Cyan),
            new($"{PhaseLabel(progress.Phase)} ", ConsoleColor.White),
            new($"{RenderBar(stats.Pct)} ", ConsoleColor.DarkGray),
            new(stats.Total > 0 ? $"{stats.Done}/{stats.Total}" : $"{stats.Done} files", ConsoleColor.Gray)
        };

        if (progress.Cached > 0)
            parts.Add(new FramePart($" ({progress.Cached} cached)", ConsoleColor.DarkCyan));

        parts.Add(new FramePart($"  ·  {time}", ConsoleColor.DarkGray));
        return parts;
    }

    /// <summary>
    /// Paint the segments over the current frame in place and pad beyond
    /// the widest frame drawn since the last erasure so no glyphs linger.
    /// Callers must run only while animation is active.
    /// </summary>
    private static void PaintFrame(List<FramePart> parts)
    {
        int width = parts.Sum(static part => part.Text.Length);

        lock (_consoleLock)
        {
            _rawOut!.Write('\r');
            foreach (var part in parts)
            {
                Console.ForegroundColor = part.Color;
                _rawOut.Write(part.Text);
            }

            int padding = _lastFrameLength - width;
            if (padding > 0)
                _rawOut.Write(new string(' ', padding));

            Console.ResetColor();
            _rawOut.Flush();
            _lastFrameLength = Math.Max(_lastFrameLength, width);
        }
    }

    /// <summary>
    /// Render a smooth gradient progress bar using Unicode block characters.
    /// Filled portion uses full blocks; empty portion uses shaded blocks for
    /// a subtle texture.
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

    /// <summary>Format the running stopwatch for the live frame.</summary>
    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1}s"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";

    // ── Summary rendering ──────────────────────────────────────

    /// <summary>
    /// Print the final build summary after the animation stops.
    /// Shows: success/fail glyph, file counts, elapsed time, output dir.
    /// </summary>
    private static void PrintSummary(BuildResult result)
    {
        var indent = "  ";
        var durationMs = result.DurationMs;

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
            var outDir = result.OutputDir;
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
    /// Prints a single-line summary without carriage-return control codes.
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

    // ── Console coordination ───────────────────────────────────

    /// <summary>
    /// Turn the indicator off after a painting failure. Painting is
    /// cosmetic, so the build itself continues unaffected; restoring the
    /// console writers keeps all subsequent log output working normally.
    /// </summary>
    private static void DisableAfterPaintingFailure()
    {
        _enabled = false;
        lock (_consoleLock)
        {
            try { _timer?.Dispose(); } catch { /* Timer may already be disposed. */ }
            _timer = null;
            RestoreWriters();
        }
    }

    /// <summary>
    /// Erase the animation frame on screen and flush the stream.
    /// Callers must hold <see cref="_consoleLock"/>.
    /// </summary>
    private static void ClearAnimationLine()
    {
        EraseFrame();
        _rawOut?.Flush();
    }

    /// <summary>
    /// Overwrite the visible frame with spaces. Erasure always targets
    /// stdout: the animation runs only when stdout is a live console, and
    /// a console shares a single cursor between stdout and stderr.
    /// Callers must hold <see cref="_consoleLock"/>.
    /// </summary>
    private static void EraseFrame()
    {
        if (_rawOut is null) return;

        _rawOut.Write('\r');
        _rawOut.Write(new string(' ', ResolveClearWidth()));
        _rawOut.Write('\r');
        _lastFrameLength = 0;
    }

    /// <summary>
    /// Resolve the erasure width. Prefer the full window width when it is
    /// cheap to read; fall back to the last frame width plus a margin.
    /// WindowWidth throws on detached pseudo-consoles, where the fallback
    /// keeps erasure working.
    /// </summary>
    private static int ResolveClearWidth()
    {
        int width = _lastFrameLength + 2;
        try
        {
            if (Console.WindowWidth > 0)
                width = Math.Max(width, Console.WindowWidth - 1);
        }
        catch (IOException) { /* Use the frame-length fallback. */ }
        catch (InvalidOperationException) { /* No window is available. */ }
        return width;
    }

    /// <summary>Restore the original console streams captured in Start.</summary>
    private static void RestoreWriters()
    {
        if (_rawOut is not null)
        {
            _outProxy?.Flush();
            Console.SetOut(_rawOut);
            _rawOut = null;
            _outProxy = null;
        }
        if (_rawError is not null)
        {
            _errorProxy?.Flush();
            Console.SetError(_rawError);
            _rawError = null;
            _errorProxy = null;
        }
    }

    /// <summary>
    /// Proxy writer installed while the animation is active. Build code
    /// writing through <see cref="Console.Out"/> or <see cref="Console.Error"/>
    /// (including F# <c>eprintfn</c> timing lines) is buffered per line:
    /// the frame is erased once when each new line starts, so log output
    /// and the spinner never interleave on the same screen line.
    /// </summary>
    private sealed class CoordinatedWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly StringBuilder _pending = new();
        private bool _atLineStart = true;

        internal CoordinatedWriter(TextWriter inner) => _inner = inner;

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(char value)
        {
            lock (_consoleLock)
            {
                _pending.Append(value);
                if (value == '\n')
                    DrainCompletedLines();
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            lock (_consoleLock)
            {
                _pending.Append(value);
                if (value.Contains('\n'))
                    DrainCompletedLines();
            }
        }

        public override void Flush()
        {
            lock (_consoleLock)
            {
                EmitPending();
                _inner.Flush();
            }
        }

        /// <summary>
        /// Write every fully buffered line (ending in '\n') and keep any
        /// trailing partial line buffered until more text arrives.
        /// Callers must hold <see cref="_consoleLock"/>.
        /// </summary>
        private void DrainCompletedLines()
        {
            var buffered = _pending.ToString();
            int lastNewline = buffered.LastIndexOf('\n');
            if (lastNewline < 0) return;

            EmitText(buffered.Substring(0, lastNewline + 1));
            _pending.Remove(0, lastNewline + 1);
            _inner.Flush();
        }

        /// <summary>
        /// Write a buffered partial line (no terminating newline yet).
        /// Callers must hold <see cref="_consoleLock"/>.
        /// </summary>
        private void EmitPending()
        {
            if (_pending.Length == 0) return;
            EmitText(_pending.ToString());
            _pending.Clear();
        }

        /// <summary>
        /// Emit text one line at a time, erasing the frame at the start of
        /// each new line. Callers must hold <see cref="_consoleLock"/>.
        /// </summary>
        private void EmitText(string text)
        {
            int segmentStart = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != '\n') continue;
                EmitSegment(text, segmentStart, i + 1 - segmentStart);
                segmentStart = i + 1;
                _atLineStart = true;
            }

            if (segmentStart < text.Length)
                EmitSegment(text, segmentStart, text.Length - segmentStart);
        }

        private void EmitSegment(string text, int start, int length)
        {
            if (_atLineStart && _enabled)
                EraseFrame();
            _atLineStart = false;
            _inner.Write(text.AsSpan(start, length));
        }
    }
}
