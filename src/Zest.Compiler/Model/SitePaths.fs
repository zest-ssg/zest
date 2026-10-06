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
open System
open System.Collections.Concurrent
open System.IO
open System.Text
open System.Text.RegularExpressions

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

    /// Windows and macOS use case-insensitive file systems; Linux does not.
    /// Path containment checks must follow the platform or they either reject
    /// valid paths or accept escaping ones.
    let internal pathComparison =
        if OperatingSystem.IsLinux() then StringComparison.Ordinal
        else StringComparison.OrdinalIgnoreCase

    /// Convert a path to forward-slash form so glob patterns and stored paths
    /// are OS-independent.
    let internal toSlashes (path: string) =
        if isNull path then ""
        else path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/')

    /// Join a relative directory onto the project root and normalise it.
    ///
    /// A leading "/" or "\" in a config value is decorative — authors write
    /// `output = "/_site"` meaning "the usual place" — so it is stripped rather
    /// than treated as a filesystem root. Leading dots are *not* stripped:
    /// `.well-known` is a legitimate directory name.
    let internal resolvePath root dir =
        let rel =
            (if isNull dir then "" else dir.ToString()).Trim().TrimStart('/', '\\')
        Path.GetFullPath(Path.Combine(root, rel))

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

    // ── Glob matching for include / exclude ──────────────────────

    let private globCache = ConcurrentDictionary<string, Regex>()

    /// Compile a config glob to an anchored regex.
    ///
    ///   `*`   matches within one path segment
    ///   `**`  matches across segments
    ///   `?`   matches a single non-separator character
    ///
    /// A trailing `/*` is read as "this directory and everything under it"
    /// (`tools/*` → `tools/**`), which is what an author means by excluding a
    /// folder. Anchoring is what makes `tools/*` stop matching `toolsfoo/a.md`.
    let private compileGlob (pattern: string) : Regex =
        globCache.GetOrAdd(pattern, fun p ->
            let normalized = if p.EndsWith("/*", StringComparison.Ordinal) then p + "*" else p
            let sb = StringBuilder("^")
            let mutable i = 0
            while i < normalized.Length do
                match normalized.[i] with
                | '*' when i + 1 < normalized.Length && normalized.[i + 1] = '*' ->
                    sb.Append(".*") |> ignore
                    i <- i + 2
                | '*' ->
                    sb.Append("[^/]*") |> ignore
                    i <- i + 1
                | '?' ->
                    sb.Append("[^/]") |> ignore
                    i <- i + 1
                | c ->
                    sb.Append(Regex.Escape(string c)) |> ignore
                    i <- i + 1
            sb.Append('$') |> ignore
            Regex(sb.ToString(), RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant))

    /// Match one config pattern against a content-relative path.
    ///
    /// A pattern with no separator is matched against the bare file name
    /// (`README.md`, `*.md`); a pattern with one is matched against the whole
    /// relative path with forward slashes.
    let internal matchesGlob (pattern: string) (relPath: string) (fileName: string) =
        if String.IsNullOrWhiteSpace pattern then false
        else
            let p = pattern.Trim()
            if p.Contains '/' || p.Contains '\\' then
                (compileGlob (toSlashes p)).IsMatch(toSlashes relPath)
            else
                compileGlob p |> fun re -> re.IsMatch(fileName)

    /// Extended exclusion check that also respects config.Include and config.Exclude.
    /// Files matching an include pattern bypass the default auto-exclusion.
    /// Files matching an exclude pattern are always skipped.
    let internal isExcludedWithConfig (contentDir: string) (config: SiteConfig) (filePath: string) =
        let relPath = Path.GetRelativePath(contentDir, filePath)
        let fileName = Path.GetFileName(filePath)

        // Check explicit include — if file matches an include pattern, never exclude
        if config.Include |> List.exists (fun p -> matchesGlob p relPath fileName) then false
        // Check explicit exclude
        elif config.Exclude |> List.exists (fun p -> matchesGlob p relPath fileName) then true
        else isExcluded contentDir filePath

    // ── Output-path safety ───────────────────────────────────────

    /// True when `candidate` resolves to a path strictly inside `root`.
    let internal isStrictlyWithin (root: string) (candidate: string) =
        let rootWithSep =
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + string Path.DirectorySeparatorChar
        candidate.StartsWith(rootWithSep, pathComparison)

    /// Normalise an output-relative path to forward slashes. Generated URLs
    /// (`page.Url`) and output paths must agree on one separator regardless of
    /// the host OS, otherwise Windows writes `posts\a\index.html` while the URL
    /// says `posts/a/`.
    let normalizeOutputRel (relPath: string) =
        toSlashes (if isNull relPath then "" else relPath)

    /// Resolve `relPath` under `outputDir`, throwing when the result escapes it.
    ///
    /// Every generated output path — permalinks, taxonomy and pagination pages,
    /// asset targets — passes through here. A permalink such as
    /// `/../../etc/passwd` must fail loudly rather than write outside the site.
    /// Returns the absolute target path.
    let assertWithinOutput (outputDir: string) (relPath: string) : string =
        let rootFull = Path.GetFullPath outputDir
        let target = Path.GetFullPath(Path.Combine(rootFull, normalizeOutputRel relPath))
        if not (isStrictlyWithin rootFull target) then
            invalidArg "relPath"
                (sprintf "Output path '%s' escapes the output directory '%s'." relPath rootFull)
        target

    /// True when it is safe to delete `outputDir` wholesale during a clean
    /// build: it must resolve to a strict subdirectory of `root`. Deleting the
    /// project root, or anything outside it, is never safe.
    let isSafeToClean (root: string) (outputDir: string) =
        let rootFull = Path.GetFullPath root
        let outFull = Path.GetFullPath outputDir
        not (String.IsNullOrWhiteSpace outFull) && isStrictlyWithin rootFull outFull
