namespace Zest.Engine

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http

/// <summary>
/// Resolves a theme to a local directory from one of four sources:
/// local (_themes/), git, url (ZIP download), or path.
/// </summary>
///
/// All fetched themes are cached under .zest/themes/{name}/ so subsequent
/// builds skip the clone/download step.
module ThemeResolver =

    let private httpClient = lazy (
        let client = new HttpClient(Timeout = TimeSpan.FromSeconds(60.))
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Zest-ThemeLoader/1.0")
        client)

    /// Helper: delete a directory recursively, ignoring errors.
    let private deleteDir (path: string) =
        try if Directory.Exists path then Directory.Delete(path, recursive = true) with _ -> ()

    /// Cache directory for fetched themes.
    let private cacheDir (projectRoot: string) (name: string) =
        Path.Combine(projectRoot, ".zest", "themes", name)

    // ── Local source ──────────────────────────────────────────

    let private resolveLocal (projectRoot: string) (name: string) =
        let dir = Path.Combine(projectRoot, "_themes", name)
        if Directory.Exists dir then
            eprintfn "  Theme: _themes/%s" name
            Some dir
        else
            eprintfn "[Zest] Theme: directory '_themes/%s' not found." name
            None

    // ── Git source ────────────────────────────────────────────

    let private resolveGit (projectRoot: string) (theme: ThemeConfig) =
        let cached = cacheDir projectRoot theme.Name
        let version =
            if not (String.IsNullOrEmpty theme.Tag) then theme.Tag
            elif not (String.IsNullOrEmpty theme.Branch) then theme.Branch
            else "main"

        if Directory.Exists (Path.Combine(cached, ".git")) then
            eprintfn "  Theme: git (%s) [cached]" version
            Some cached
        else
            eprintfn "  Theme: cloning %s (%s)..." theme.Git version
            try
                deleteDir cached
                let args = sprintf "clone --depth 1 --branch \"%s\" --single-branch \"%s\" \"%s\"" version theme.Git cached
                let psi = Diagnostics.ProcessStartInfo("git", args)
                psi.UseShellExecute <- false
                psi.RedirectStandardOutput <- true
                psi.RedirectStandardError <- true
                psi.CreateNoWindow <- true

                use proc = Diagnostics.Process.Start(psi)
                if isNull proc then
                    eprintfn "[Zest] Theme: failed to start git process."
                    None
                else
                    proc.WaitForExit(60_000) |> ignore
                    if proc.ExitCode <> 0 then
                        let err = proc.StandardError.ReadToEnd()
                        eprintfn "[Zest] Theme: git clone failed: %s" err
                        deleteDir cached
                        None
                    else
                        eprintfn "  Theme: %s (%s) cloned." theme.Name version
                        Some cached
            with ex ->
                eprintfn "[Zest] Theme: git clone failed: %s" ex.Message
                deleteDir cached
                None

    // ── URL source ────────────────────────────────────────────

    let private resolveUrl (projectRoot: string) (theme: ThemeConfig) =
        let cached = cacheDir projectRoot theme.Name

        // Quick-check: if directory has content, assume cached
        if Directory.Exists cached && Directory.GetFiles(cached).Length > 0 then
            eprintfn "  Theme: %s (url) [cached]" theme.Name
            Some cached
        else
            eprintfn "  Theme: downloading %s..." theme.Url
            try
                deleteDir cached
                Directory.CreateDirectory(cached) |> ignore

                let tmpZip = Path.Combine(Path.GetTempPath(), sprintf "zest-theme-%s.zip" (Guid.NewGuid().ToString("N")))
                try
                    use response = httpClient.Value.GetAsync(theme.Url : string).Result
                    if not response.IsSuccessStatusCode then
                        eprintfn "[Zest] Theme: download failed — HTTP %d" (int response.StatusCode)
                        deleteDir cached
                        None
                    else
                        use stream = response.Content.ReadAsStream()
                        use fileStream = File.Create(tmpZip)
                        stream.CopyTo(fileStream)
                        fileStream.Close()

                        ZipFile.ExtractToDirectory(tmpZip, cached)

                        // Unwrap single-root-folder ZIP archives.
                        let entries = Directory.GetFileSystemEntries(cached)
                        if entries.Length = 1 && Directory.Exists(entries.[0]) then
                            let inner = entries.[0]
                            let tmpDir = cached + ".unwrap"
                            Directory.Move(cached, tmpDir)
                            Directory.Move(inner, cached)
                            deleteDir tmpDir

                        eprintfn "  Theme: %s downloaded and extracted." theme.Name
                        Some cached
                finally
                    try File.Delete(tmpZip) with _ -> ()
            with ex ->
                eprintfn "[Zest] Theme: download failed: %s" ex.Message
                deleteDir cached
                None

    // ── Path source ───────────────────────────────────────────

    let private resolvePath (path: string) =
        if String.IsNullOrEmpty path then
            eprintfn "[Zest] Theme: source is 'path' but no path specified."
            None
        else
            let fullPath = Path.GetFullPath(path)
            if Directory.Exists fullPath then
                eprintfn "  Theme: %s" fullPath
                Some fullPath
            else
                eprintfn "[Zest] Theme: directory '%s' not found." path
                None

    // ── Public entry point ────────────────────────────────────

    /// Resolve the theme directory. Returns None when no theme is
    /// configured or the source can't be resolved.
    let resolve (projectRoot: string) (theme: ThemeConfig) : string option =
        if String.IsNullOrEmpty theme.Name then None
        else
            let source = if String.IsNullOrEmpty theme.Source then "local" else theme.Source.ToLowerInvariant()
            match source with
            | "local" -> resolveLocal projectRoot theme.Name
            | "git"   -> resolveGit projectRoot theme
            | "url"   -> resolveUrl projectRoot theme
            | "path"  -> resolvePath theme.Path
            | _ ->
                eprintfn "[Zest] Theme: unknown source '%s', falling back to 'local'." source
                resolveLocal projectRoot theme.Name
