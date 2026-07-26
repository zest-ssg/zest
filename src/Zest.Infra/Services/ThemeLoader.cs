using System.IO.Compression;
using System.Net;
using Zest.Engine;

#nullable enable

namespace Zest.Infra.Services;

/// <summary>
/// Resolves a theme to a local directory from one of four sources:
/// local (_themes/), git, url (ZIP download), or path.
///
/// All fetched themes are cached under .zest/themes/{name}/ so
/// subsequent builds skip the clone/download step.
/// </summary>
public static class ThemeLoader
{
    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(60),
        DefaultRequestVersion = HttpVersion.Version20
    };

    static ThemeLoader()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Zest-ThemeLoader/1.0");
    }

    /// <summary>
    /// Resolve the theme directory for the given config. Returns null
    /// when no theme is configured.
    /// </summary>
    /// <param name="projectRoot">Project root directory.</param>
    /// <param name="theme">Parsed theme configuration.</param>
    /// <returns>The absolute path to the resolved theme directory, or null.</returns>
    public static string? Resolve(string projectRoot, ThemeConfig theme)
    {
        if (string.IsNullOrEmpty(theme.Name))
            return null;

        var source = string.IsNullOrEmpty(theme.Source) ? "local" : theme.Source.ToLowerInvariant();

        return source switch
        {
            "local" => ResolveLocal(projectRoot, theme.Name),
            "git"   => ResolveGit(projectRoot, theme),
            "url"   => ResolveUrl(projectRoot, theme),
            "path"  => ResolvePath(theme.Path),
            _       => ResolveUnknownSource(projectRoot, theme)
        };
    }

    // ── Local source ──────────────────────────────────────────

    private static string? ResolveLocal(string projectRoot, string name)
    {
        var dir = Path.Combine(projectRoot, "_themes", name);
        if (Directory.Exists(dir))
        {
            LogWriter.WriteDim($"  Theme: _themes/{name}");
            return dir;
        }

        LogWriter.Warn("Theme", $"Directory '_themes/{name}' not found.");
        return null;
    }

    // ── Git source ────────────────────────────────────────────

    private static string? ResolveGit(string projectRoot, ThemeConfig theme)
    {
        var cacheDir = GetCacheDir(projectRoot, theme.Name);
        var version = !string.IsNullOrEmpty(theme.Tag) ? theme.Tag : theme.Branch ?? "main";

        if (Directory.Exists(Path.Combine(cacheDir, ".git")))
        {
            LogWriter.WriteDim($"  Theme: git ({version}) [cached]");
            return cacheDir;
        }

        LogWriter.WriteDim($"  Theme: cloning {theme.Git} ({version})...");

        try
        {
            // Remove stale cache if it exists (incomplete previous clone)
            DeleteDirectory(cacheDir);

            // Clone with minimal depth for speed
            var args = $"clone --depth 1 --branch \"{version}\" --single-branch \"{theme.Git}\" \"{cacheDir}\"";
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                LogWriter.Warn("Theme", "Failed to start git process.");
                return null;
            }

            proc.WaitForExit(60_000);

            if (proc.ExitCode != 0)
            {
                var err = proc.StandardError.ReadToEnd();
                LogWriter.Warn("Theme", $"Git clone failed: {err}");
                DeleteDirectory(cacheDir);
                return null;
            }

            LogWriter.WriteDim($"  Theme: {theme.Name} ({version}) cloned.");
            return cacheDir;
        }
        catch (Exception ex)
        {
            LogWriter.Warn("Theme", $"Git clone failed: {ex.Message}");
            DeleteDirectory(cacheDir);
            return null;
        }
    }

    // ── URL source ────────────────────────────────────────────

    private static string? ResolveUrl(string projectRoot, ThemeConfig theme)
    {
        var cacheDir = GetCacheDir(projectRoot, theme.Name);

        // Quick check: if the directory already has content, assume cached
        if (Directory.Exists(cacheDir) && Directory.GetFiles(cacheDir).Length > 0)
        {
            LogWriter.WriteDim($"  Theme: {theme.Name} (url) [cached]");
            return cacheDir;
        }

        LogWriter.WriteDim($"  Theme: downloading {theme.Url}...");

        try
        {
            DeleteDirectory(cacheDir);
            Directory.CreateDirectory(cacheDir);

            var tmpZip = Path.Combine(Path.GetTempPath(), $"zest-theme-{Guid.NewGuid():N}.zip");

            try
            {
                // Download
                using (var response = _http.GetAsync(theme.Url).Result)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        LogWriter.Warn("Theme", $"Download failed: HTTP {(int)response.StatusCode}");
                        DeleteDirectory(cacheDir);
                        return null;
                    }

                    using var stream = response.Content.ReadAsStream();
                    using var fileStream = File.Create(tmpZip);
                    stream.CopyTo(fileStream);
                }

                // Extract
                ZipFile.ExtractToDirectory(tmpZip, cacheDir);

                // Some ZIP archives wrap everything in a single root folder — 
                // unwrap it so the cache dir is the actual theme root.
                var entries = Directory.GetFileSystemEntries(cacheDir);
                if (entries.Length == 1 && Directory.Exists(entries[0]))
                {
                    var inner = entries[0];
                    var tmpDir = cacheDir + ".unwrap";
                    Directory.Move(cacheDir, tmpDir);
                    Directory.Move(inner, cacheDir);
                    DeleteDirectory(tmpDir);
                }

                LogWriter.WriteDim($"  Theme: {theme.Name} downloaded and extracted.");
                return cacheDir;
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
            }
        }
        catch (Exception ex)
        {
            LogWriter.Warn("Theme", $"Download failed: {ex.Message}");
            DeleteDirectory(cacheDir);
            return null;
        }
    }

    // ── Path source ───────────────────────────────────────────

    private static string? ResolvePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            LogWriter.Warn("Theme", "Source is 'path' but no path specified.");
            return null;
        }

        var fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath))
        {
            LogWriter.WriteDim($"  Theme: {fullPath}");
            return fullPath;
        }

        LogWriter.Warn("Theme", $"Directory '{path}' not found.");
        return null;
    }

    // ── Helpers ──────────────────────────────────────────────

    private static string? ResolveUnknownSource(string projectRoot, ThemeConfig theme)
    {
        LogWriter.Warn("Theme", $"Unknown source '{theme.Source}', falling back to 'local'.");
        return ResolveLocal(projectRoot, theme.Name);
    }

    private static string GetCacheDir(string projectRoot, string name)
    {
        return Path.Combine(projectRoot, ".zest", "themes", name);
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { /* best-effort cleanup */ }
    }
}
