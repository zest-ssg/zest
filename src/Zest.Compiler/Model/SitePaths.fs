// SitePaths.fs
//
// Owns the conventional directory names of a Zest site and every path /
// exclusion decision the build pipeline makes. Convention is fixed rather
// than configurable so a site needs no _config.toml to build.
//
// Invariant: every path returned here is absolute.
//
// Dependencies: System.IO

namespace Zest.Compiler.Model
open System.IO

/// Conventional directory names, relative to the project root.
module SiteDirectories =

    /// Content pages (.md, .zest.fsx, .ztk, .html). Falls back to the root.
    let Content = "content"

    /// Zestucks layouts (.ztk).
    let Layouts = "_layouts"

    /// Zestucks partials (.ztk).
    let Includes = "_includes"

    /// Global data files (.toml).
    let Data = "_data"

    /// Static assets, copied (and .zcss compiled) to the output.
    let Assets = "assets"

    /// i18n string tables (.toml / .json).
    let Locales = "_locales"

module SitePaths =

    /// Join a relative directory onto the project root and normalise it.
    let internal resolvePath root dir =
        Path.GetFullPath(Path.Combine(root, dir.ToString().TrimStart('.', '\\', '/')))

    /// Resolve the content directory. An empty config value auto-detects:
    /// content/ when it exists, otherwise the project root itself.
    let resolveContentDir (root: string) (config: SiteConfig) =
        if System.String.IsNullOrWhiteSpace config.ContentDir then
            let conventional = Path.Combine(root, SiteDirectories.Content)
            if Directory.Exists conventional then conventional else root
        else
            resolvePath root config.ContentDir

    /// Resolve a conventional directory (for example SiteDirectories.Layouts).
    let internal resolveSiteDir (root: string) (name: string) =
        resolvePath root name

    let internal isExcluded (contentDir: string) (filePath: string) =
        // The two reserved files (_config.toml / _prebuild.fsx) sit next to
        // content but are entry points, not pages — they never receive a route.
        if FileTypes.isReservedFile filePath then true
        else
            let segments =
                Path.GetRelativePath(contentDir, filePath)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            segments |> Array.exists (fun p -> p.StartsWith("_") || p.StartsWith("."))

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
