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

    /// Resolve the effective content directory based on RootDir configuration.
    let internal resolveEffectiveContentDir (root: string) (config: SiteConfig) =
        let rootDir = config.RootDir.Trim()
        if System.String.IsNullOrEmpty rootDir || rootDir = "." then
            root
        else
            resolvePath root rootDir
