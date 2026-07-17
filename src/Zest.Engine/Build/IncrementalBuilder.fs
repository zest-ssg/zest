namespace Zest.Engine.Build

open System
open System.IO
open Zest.Engine

// ============================================================
// IncrementalBuilder — Dependency-aware minimal rebuild planning
// ============================================================
// Wraps BuildCache's dependency graph to compute the minimal set of
// pages that must be rebuilt when one or more source files change.
// Used by the watch loop and the incremental build path.
//
// Dependency: BuildCache (dependency graph + content hashing).
// ============================================================

module IncrementalBuilder =

    /// Categorize a changed file by its role in the build.
    type FileRole =
        | ContentPage
        | Layout
        | Include
        | Asset
        | Config
        | Other

    /// Extensions treated as layout files even outside _layouts/.
    /// Subset of Nunjucks-family traditionally used for layouts.
    let private layoutExts = [FileExtensions.Nunjucks; FileExtensions.Liquid]

    /// Case-insensitive "ends with any" check for a path against an extension list.
    let private endsWithAny (path: string) (exts: string list) =
        exts |> List.exists (fun e -> path.EndsWith(e, StringComparison.OrdinalIgnoreCase))

    /// Classify a file path by extension and location.
    let classifyFile (filePath: string) : FileRole =
        let name = Path.GetFileName(filePath)
        let dirRaw = Path.GetDirectoryName(filePath)
        let dir = if isNull dirRaw then "" else dirRaw.ToLowerInvariant()
        if name.StartsWith("_config") then Config
        elif dir.Contains("_layouts") || endsWithAny name layoutExts then Layout
        elif dir.Contains("_includes") then Include
        elif FileExtensions.isAsset filePath then Asset
        elif FileExtensions.isContent filePath then ContentPage
        else Other

    /// Compute the set of pages affected by a list of changed files.
    /// Layout/include/config changes fan out to all dependent pages;
    /// content-page changes affect only themselves.
    let getAffectedPages (changedFiles: string seq) : string list =
        let result = System.Collections.Generic.HashSet<string>()
        for f in changedFiles do
            for page in BuildCache.getAffectedPages f do
                result.Add(page) |> ignore
        // A layout change with no recorded dependencies forces a full rebuild
        // signal — return the changed layout path so callers can decide.
        Seq.toList result

    /// Decide whether a full rebuild is required given the changed files.
    /// A config change or an untracked layout change mandates a full rebuild.
    let requiresFullRebuild (changedFiles: string seq) : bool =
        changedFiles
        |> Seq.exists (fun f ->
            match classifyFile f with
            | Config -> true
            | Layout ->
                // If the layout has no recorded dependents, we can't scope
                // the rebuild — fall back to full.
                match BuildCache.getAffectedPages f with
                | [] -> true
                | _ -> false
            | _ -> false)

    /// Given a set of changed files, return the minimal rebuild plan:
    /// either `Full` or `Partial(pagePaths)`.
    type RebuildPlan =
        | Full
        | Partial of pagePaths: string list

    let computePlan (changedFiles: string seq) : RebuildPlan =
        if Seq.isEmpty changedFiles then Partial []
        elif requiresFullRebuild changedFiles then Full
        else Partial (getAffectedPages changedFiles)

    /// Record that a content page depends on a layout file, so future
    /// layout changes scope correctly. Safe to call repeatedly.
    let recordLayoutDependency (pagePath: string) (layoutPath: string) =
        BuildCache.recordDependency pagePath layoutPath

    /// Record that a content page depends on an include file.
    let recordIncludeDependency (pagePath: string) (includePath: string) =
        BuildCache.recordDependency pagePath includePath
