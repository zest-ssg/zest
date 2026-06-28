namespace Zest.Engine

/// <summary>
/// Result of a build operation.
/// </summary>
type BuildResult = {
    TotalPages: int
    ProcessedPages: int
    CachedPages: int
    AssetsCopied: int
    AssetsMinified: int
    DurationMs: int64
    Errors: string list
}
with
    member this.Success = this.Errors.IsEmpty
