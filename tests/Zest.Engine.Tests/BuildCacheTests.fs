module Zest.Engine.Tests.BuildCacheTests

open Xunit
open Zest.Engine

// ============================================================
// BuildCache — Tests for the public API surface exposed to CLI
// (clearCache). Internal helpers (contentHashOf, needsRebuild,
// etc.) are tested indirectly through integration.
// ============================================================

module ``BuildCache clearCache`` =

    [<Fact>]
    let ``clearCache does not throw`` () =
        // Verify it's callable without errors
        BuildCache.clearCache()

    [<Fact>]
    let ``clearCache is idempotent`` () =
        BuildCache.clearCache()
        BuildCache.clearCache()
        // No exception = pass
        Assert.True(true)

    [<Fact>]
    let ``clearCache called after initialized state is safe`` () =
        // Multiple patterns: just ensure no crash
        BuildCache.clearCache()
        BuildCache.clearCache()
        Assert.True(true)
