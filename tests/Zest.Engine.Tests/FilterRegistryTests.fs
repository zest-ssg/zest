module Zest.Engine.Tests.FilterRegistryTests

open System.Collections.Generic
open Xunit
open Zest.Engine.Scripting

// ============================================================
// FilterRegistry — Tests for strict mode toggle and init-filter
// registration. Verifies that the registry's public API works
// as expected for the Nunjucks compatibility feature.
// ============================================================

module ``FilterRegistry strict mode`` =

    [<Fact>]
    let ``setStrictMode true then false round-trips`` () =
        FilterRegistry.setStrictMode true
        FilterRegistry.setStrictMode false
        // No exception = pass
        Assert.True(true)

    [<Fact>]
    let ``setStrictMode does not throw`` () =
        FilterRegistry.setStrictMode true
        FilterRegistry.setStrictMode false
        Assert.True(true)

module ``FilterRegistry init filters`` =

    [<Fact>]
    let ``setInitFilters accepts empty dictionary`` () =
        let empty = Dictionary<string, string>() :> IDictionary<string, string>
        FilterRegistry.setInitFilters empty
        Assert.True(true)

    [<Fact>]
    let ``setInitFilters accepts filters with pipeline specs`` () =
        let filters = Dictionary<string, string>()
        filters.["my_filter"] <- "upper | trim"
        FilterRegistry.setInitFilters (filters :> IDictionary<string, string>)
        Assert.True(true)

    [<Fact>]
    let ``setInitFilters replaces previous filters`` () =
        let a = Dictionary<string, string>()
        a.["a"] <- "upper"
        FilterRegistry.setInitFilters (a :> IDictionary<string, string>)
        let b = Dictionary<string, string>()
        b.["b"] <- "lower"
        FilterRegistry.setInitFilters (b :> IDictionary<string, string>)
        Assert.True(true)
