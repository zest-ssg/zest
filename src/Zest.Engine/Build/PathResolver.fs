namespace Zest.Engine

open System.IO

/// Path resolution and exclusion rule detection.
module PathResolver =

    let internal resolvePath root dir =
        Path.GetFullPath(Path.Combine(root, dir.ToString().TrimStart('.', '\\', '/')))

    let internal isExcluded (contentDir: string) (filePath: string) =
        Path.GetRelativePath(contentDir, filePath)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        |> Array.exists (fun p -> p.StartsWith("_") || p.StartsWith("."))

    /// Extended exclusion check that also respects config.Include and config.Exclude.
    /// Files matching an include pattern bypass the default auto-exclusion.
    /// Files matching an exclude pattern are always skipped.
    let internal isExcludedWithConfig (contentDir: string) (config: SiteConfig) (filePath: string) =
        let relPath = Path.GetRelativePath(contentDir, filePath)
        let fileName = Path.GetFileName(filePath)
        let segments = relPath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)

        // Check explicit include — if file matches an include pattern, never exclude
        let isIncluded =
            config.Include
            |> List.exists (fun pattern ->
                match pattern with
                | p when p = fileName -> true
                | p when p.StartsWith("*.") ->
                    fileName.EndsWith(p.Substring(1), System.StringComparison.OrdinalIgnoreCase)
                | p when p.EndsWith("/*") ->
                    let dir = p.TrimEnd('/').TrimEnd('*')
                    relPath.Replace(Path.DirectorySeparatorChar, '/').StartsWith(dir) ||
                    relPath.Replace(Path.AltDirectorySeparatorChar, '/').StartsWith(dir)
                | _ -> false)

        if isIncluded then false
        else
            // Check explicit exclude
            let isExcludedByConfig =
                config.Exclude
                |> List.exists (fun pattern ->
                    match pattern with
                    | p when p = fileName -> true
                    | p when p.StartsWith("*.") ->
                        fileName.EndsWith(p.Substring(1), System.StringComparison.OrdinalIgnoreCase)
                    | p when p.EndsWith("/*") ->
                        let dir = p.TrimEnd('/').TrimEnd('*')
                        relPath.Replace(Path.DirectorySeparatorChar, '/').StartsWith(dir) ||
                        relPath.Replace(Path.AltDirectorySeparatorChar, '/').StartsWith(dir)
                    | _ -> false)

            if isExcludedByConfig then true
            else isExcluded contentDir filePath

    /// Resolve the effective content directory based on RootDir configuration.
    let internal resolveEffectiveContentDir (root: string) (config: SiteConfig) =
        let rootDir = config.RootDir.Trim()
        if System.String.IsNullOrEmpty rootDir || rootDir = "." then
            root
        else
            resolvePath root rootDir
