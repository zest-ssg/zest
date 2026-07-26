using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Encapsulates file-system watching with debounce, extension filtering,
/// CSS-only change tracking, and excluded-directory logic. Shared by
/// <see cref="DevServer"/> and <see cref="PreviewService"/> to eliminate
/// duplicated watcher setup.
/// </summary>
/// <remarks>
/// <para>Design decisions:</para>
/// <list type="bullet">
///   <item><b>InternalBufferSize = 64 KB</b> — the .NET default (8 KB) overflows
///       easily on large projects, silently dropping events.</item>
///   <item><b>300 ms debounce</b> — batches rapid save-events from editors.</item>
///   <item><b>CSS-only tracking</b> — if every changed file in a batch is
///       .css/.zcss, the rebuild callback receives <c>cssOnly = true</c> so the
///       caller can broadcast a style-injection instead of a full-page reload.</item>
/// </list>
/// </remarks>
public sealed class FileWatchController : IDisposable
{
    private readonly string _watchDir;
    private readonly string _outputDir;
    private readonly HashSet<string> _ignoredDirNames;
    private readonly FileSystemWatcher _watcher;
    private readonly System.Timers.Timer _debounceTimer;
    private readonly Action<bool> _onRebuild;

    private readonly object _changeLock = new();
    private bool _cssOnlyChanges = true;
    private bool _disposed;

    /// <summary>
    /// Creates and starts a file watcher for the given project directory.
    /// </summary>
    /// <param name="watchDir">Root directory to watch (typically CWD).</param>
    /// <param name="outputDir">Output directory whose changes should be ignored.</param>
    /// <param name="ignoredDirNames">Case-insensitive set of directory names to skip.</param>
    /// <param name="onRebuild">Callback invoked after debounce. Receives <c>true</c>
    /// when the change batch is CSS-only (suitable for style injection).</param>
    public FileWatchController(
        string watchDir,
        string outputDir,
        HashSet<string> ignoredDirNames,
        Action<bool> onRebuild)
    {
        _watchDir = watchDir;
        _outputDir = outputDir;
        _ignoredDirNames = ignoredDirNames;
        _onRebuild = onRebuild;

        _watcher = new FileSystemWatcher(watchDir, "*.*")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            // .NET default is 8 KB; 64 KB handles large projects without overflow.
            InternalBufferSize = 65536
        };

        _debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        _debounceTimer.Elapsed += (_, _) =>
        {
            bool cssOnly;
            lock (_changeLock)
            {
                cssOnly = _cssOnlyChanges;
                _cssOnlyChanges = true;
            }
            _onRebuild(cssOnly);
        };

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

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_disposed || _debounceTimer == null) return;
        if (e == null || string.IsNullOrEmpty(e.FullPath)) return;

        if (!ShouldWatch(e.FullPath, e.Name)) return;

        var ext = (e.Name != null ? Path.GetExtension(e.Name) : null)?.ToLowerInvariant() ?? "";
        var isCss = ext is FileExtensions.Css or FileExtensions.Zcss;
        lock (_changeLock)
        {
            if (!isCss) _cssOnlyChanges = false;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (_disposed || _debounceTimer == null) return;
        if (e == null || string.IsNullOrEmpty(e.FullPath)) return;

        // Filter renames too — otherwise moving a file into .git/ would
        // trigger a spurious rebuild.
        if (!ShouldWatch(e.FullPath, e.Name)) return;

        var oldExt = Path.GetExtension(e.OldName ?? "")?.ToLowerInvariant() ?? "";
        var newExt = Path.GetExtension(e.Name ?? "")?.ToLowerInvariant() ?? "";
        var isCss = oldExt is FileExtensions.Css or FileExtensions.Zcss
                 || newExt is FileExtensions.Css or FileExtensions.Zcss;
        lock (_changeLock)
        {
            if (!isCss) _cssOnlyChanges = false;
        }

        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    /// <summary>
    /// Handle FileSystemWatcher.Error. On buffer overflow the watcher becomes
    /// unreliable, so we log a warning and trigger a full rebuild.
    /// </summary>
    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        LogWriter.Warn("FileWatch", $"FileSystemWatcher error: {ex.Message}. Triggering rebuild as safety measure.");
        // Force a rebuild so we don't miss changes.
        lock (_changeLock) { _cssOnlyChanges = false; }
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private bool ShouldWatch(string fullPath, string? fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;

        // Skip changes inside the output directory.
        if (fullPath.StartsWith(_outputDir, StringComparison.OrdinalIgnoreCase))
            return false;

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!WatchConstants.Extensions.Contains(ext)) return false;

        // Check each directory component in the path.
        var parts = fullPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var p = parts[i];
            if (_ignoredDirNames.Contains(p)) return false;
            // Skip hidden directories (those starting with '.') anywhere in the path.
            if (p.StartsWith('.')) return false;
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher.EnableRaisingEvents = false;
        _debounceTimer.Stop();

        // Unsubscribe before dispose to avoid callbacks during cleanup.
        _watcher.Changed -= OnFileChanged;
        _watcher.Created -= OnFileChanged;
        _watcher.Deleted -= OnFileChanged;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Error -= OnWatcherError;

        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
