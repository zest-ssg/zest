// ContentTypes.fs
//
// The small structural types that wrap a page: page groups, the F# script
// context and the build result. They live together because they are each a
// handful of lines and are always read as a set. ContentPage and ContentMeta
// are defined before this file.
//
// Dependencies: System.Collections.Generic

namespace Zest.Compiler.Model
open System.Collections.Generic

/// <summary>
/// A named collection of pages (like 11ty collections).
/// </summary>
type PageGroup = {
    Name: string
    Pages: ContentPage list
    Type: GroupType
}
and GroupType =
    | Directory
    | Tag
    | Category

/// <summary>
/// Globals injected into the F# script evaluation context.
/// </summary>
type ScriptContext = {
    Site: IDictionary<string, obj>
    Collections: IDictionary<string, obj>
    Data: IDictionary<string, obj>
    Page: ContentPage option
    Content: string option
}

/// <summary>
/// Result of a build operation.
/// </summary>
type BuildResult = {
    TotalPages: int
    ProcessedPages: int
    CachedPages: int
    AssetsCopied: int
    DurationMs: int64
    /// Absolute path to the output directory (for summary display).
    OutputDir: string
    Errors: string list
}
with
    member this.Success = this.Errors.IsEmpty
