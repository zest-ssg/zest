using System.Diagnostics;
using Zest.Compiler.Model;
using Zest.Compiler.Build;

#nullable enable

namespace Zest.App.Runtime;

/// <summary>
/// Encapsulates file-system watching with debounce, extension filtering,
/// CSS-only change tracking, and excluded-directory logic. Shared by
/// <see cref="DevServer"/>, <see cref="PreviewServer"/> and
/// <see cref="BuildWatcher"/> so every watch mode reacts to the same files.
/// </summary>
/// <remarks>
/// <para>Design decisions:</para>
/// <list type="bullet">
///   <item><b>InternalBufferSize = 64 KB</b> — the .NET default (8 KB) overflows
///       easily on large projects, silently dropping events.</item>
///   <item><b>300 ms debounce, capped at 1 s</b> — batches rapid save-events from
///       editors, but a continuously written file cannot postpone the rebuild
///       forever.</item>
///   <item><b>CSS-only tracking</b> — if every changed file in a batch is
///       .css/.zcss, the rebuild callback receives <c>cssOnly = true</c> so the
///       caller can broadcast a style-injection instead of a full-page reload.</item>
/// </list>
/// </remarks>
public sealed class ContentWatcher : IDisposable
{
    private const int DebounceMs = 300;

    /// <summary>
    /// Longest a change may be held back by further events. Without this cap a
    /// process writing to a watched file in a loop would starve the rebuild.
    /// </summary>
    private const int MaxDebounceMs = 1000;

    private readonly string _watchDir;
    private readonly string _outputDir;
    private readonly HashSet<string> _ignoredDirNames;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly Action<bool> _onRebuild;

    private readonly object _changeLock = new();
    private bool _cssOnlyChanges = true;
    private long _windowStartTicks;
    private volatile bool _disposed;

    /// <summary>
    /// Creates and starts a file watcher for the given project directory.
    /// </summary>
    /// <param name="watchDir">Root directory to watch (typically the project root).</param>
    /// <param name="outputDir">Output directory whose changes should be ignored.</param>
    /// <param name="ignoredDirNames">Case-insensitive set of directory names to skip.</param>
    /// <param name="onRebuild">Callback invoked after debounce. Receives <c>true</c>
    /// when the change batch is CSS-only (suitable for style injection).</param>
    public ContentWatcher(
        string watchDir,
        string outputDir,
        HashSet<string> ignoredDirNames,
        Action<bool> onRebuild)
    {
        _watchDir = Path.GetFullPath(watchDir);
        _outputDir = Path.GetFullPath(outputDir);
        _ignoredDirNames = ignoredDirNames;
        _onRebuild = onRebuild;

        _watcher = new FileSystemWatcher(_watchDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            // .NET default is 8 KB; 64 KB handles large projects without overflow.
            InternalBufferSize = 65536
        };

        _debounceTimer = new System.Timers.Timer(DebounceMs) { AutoReset = false };
        _debounceTimer.Elapsed += OnDebounceElapsed;

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;

        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>
    /// Reset the CSS-only flag. Useful after a rebuild that was triggered
    /// outside the watcher (e.g., initial build, manual rebuild).
    /// </summary>
    public void ResetCssOnlyFlag()
    {
        lock (_changeLock) { _cssOnlyChanges = true; }
    }

    private void OnDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        bool cssOnly;
        lock (_changeLock)
        {
            if (_disposed) return;
            cssOnly = _cssOnlyChanges;
            _cssOnlyChanges = true;
            _windowStartTicks = 0;
        }

        // Invoked outside the lock: a rebuild can take seconds and must not
        // block file-change callbacks.
        _onRebuild(cssOnly);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        var fullPath = e?.FullPath;
        if (_disposed || string.IsNullOrEmpty(fullPath)) return;
        if (!ShouldWatch(fullPath, e!.Name)) return;

        var ext = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
        ScheduleRebuild(ext is FileTypes.Css or FileTypes.Zcss);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        var fullPath = e?.FullPath;
        if (_disposed || string.IsNullOrEmpty(fullPath)) return;

        // Filter renames too — otherwise moving a file into .git/ would
        // trigger a spurious rebuild.
        if (!ShouldWatch(fullPath, e!.Name)) return;

        var oldExt = Path.GetExtension(e.OldName ?? "").ToLowerInvariant();
        var newExt = Path.GetExtension(e.Name ?? "").ToLowerInvariant();
        ScheduleRebuild(oldExt is FileTypes.Css or FileTypes.Zcss
                     || newExt is FileTypes.Css or FileTypes.Zcss);
    }

    /// <summary>
    /// Handle FileSystemWatcher.Error. On buffer overflow the watcher becomes
    /// unreliable, so we log a warning and trigger a full rebuild.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        LogWriter.Warn("FileWatch", $"FileSystemWatcher error: {ex.Message}. Triggering rebuild as safety measure.");
        // Force a full rebuild so we don't miss changes.
        ScheduleRebuild(isCssOnly: false);
    }

    /// <summary>
    /// (Re)arm the debounce timer, recording whether the batch was CSS-only.
    /// The window is extended while it stays under <see cref="MaxDebounceMs"/>,
    /// so a long stream of events still results in a rebuild.
    /// </summary>
    private void ScheduleRebuild(bool isCssOnly)
    {
        lock (_changeLock)
        {
            if (_disposed) return;
            if (!isCssOnly) _cssOnlyChanges = false;

            var now = Stopwatch.GetTimestamp();
            var pending = _windowStartTicks != 0;
            var windowAgeMs = pending
                ? (now - _windowStartTicks) * 1000.0 / Stopwatch.Frequency
                : 0;

            if (pending && windowAgeMs >= MaxDebounceMs)
                return; // Already queued and past the cap — let it fire.

            if (!pending) _windowStartTicks = now;

            // Stop/Start is safe here: Dispose takes the same lock before it
            // disposes the timer, so this can never observe a disposed timer.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private bool ShouldWatch(string fullPath, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        // Skip changes inside the output directory.
        if (IsInsideOutput(fullPath)) return false;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!WatchConstants.Extensions.Contains(ext)) return false;

        // Only the directory components below the watch root matter. Testing
        // the absolute path would also reject projects that happen to live
        // under a hidden directory (e.g. ~/.local/src/my-site).
        var relative = Path.GetRelativePath(_watchDir, fullPath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (_ignoredDirNames.Contains(p)) return false;
            // Skip hidden directories (those starting with '.') below the root.
            if (p.StartsWith('.')) return false;
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="fullPath"/> is the output directory or lives
    /// inside it. Uses a relative-path test so a sibling with a shared prefix
    /// (e.g. <c>_site-archive</c> next to <c>_site</c>) is not mistaken for
    /// output, and so case sensitivity follows the platform.
    /// </summary>
    private bool IsInsideOutput(string fullPath)
    {
        var relative = Path.GetRelativePath(_outputDir, fullPath);
        if (relative == ".") return true;
        if (Path.IsPathRooted(relative)) return false;

        return !relative.StartsWith("..", StringComparison.Ordinal);
    }

    public void Dispose()
    {
        lock (_changeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _watcher.EnableRaisingEvents = false;

        // Unsubscribe before dispose to avoid callbacks during cleanup.
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Error -= OnWatcherError;

        _watcher.Dispose();

        lock (_changeLock)
        {
            _debounceTimer.Elapsed -= OnDebounceElapsed;
            _debounceTimer.Stop();
            _debounceTimer.Dispose();
        }
    }
}
