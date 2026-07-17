module Zest.Engine.Tests.SiteConfigTests

open Xunit
open Zest.Engine

// ============================================================
// SiteConfig — Sanity checks for default values and helper
// members. Ensures the defaults struct stays consistent.
// ============================================================

module ``SiteConfig defaults`` =

    let defaultConfig = SiteConfigDefaults.create()

    [<Fact>]
    let ``default title is My Zest Site`` () =
        Assert.Equal("My Zest Site", defaultConfig.Title)

    [<Fact>]
    let ``default dev server port is 8080`` () =
        Assert.Equal(8080, defaultConfig.DevServerPort)

    [<Fact>]
    let ``default live reload port is 35729`` () =
        Assert.Equal(35729, defaultConfig.LiveReloadPort)

    [<Fact>]
    let ``default output dir is ./_site`` () =
        Assert.Equal("./_site", defaultConfig.OutputDir)

    [<Fact>]
    let ``default layouts dir is ./_layouts`` () =
        Assert.Equal("./_layouts", defaultConfig.LayoutsDir)

    [<Fact>]
    let ``default content dir is ./content`` () =
        Assert.Equal("./content", defaultConfig.ContentDir)

    [<Fact>]
    let ``default template engine is native`` () =
        Assert.Equal("native", defaultConfig.TemplateEngine)

    [<Fact>]
    let ``nunjucks compatibility defaults to zest`` () =
        Assert.Equal("zest", defaultConfig.NunjucksCompatibility)

    [<Fact>]
    let ``all compat flags default to false`` () =
        Assert.False(defaultConfig.CompatJekyll)
        Assert.False(defaultConfig.CompatHexo)
        Assert.False(defaultConfig.CompatHugo)
        Assert.False(defaultConfig.CompatEleventy)

    [<Fact>]
    let ``parallel and incremental build enabled by default`` () =
        Assert.True(defaultConfig.EnableParallelBuild)
        Assert.True(defaultConfig.EnableIncrementalBuild)

    [<Fact>]
    let ``default taxonomies include tag and category`` () =
        Assert.Equal(2, defaultConfig.Taxonomies.Length)
        Assert.True(defaultConfig.Taxonomies |> List.exists (fun t -> t.Name = "tag"))
        Assert.True(defaultConfig.Taxonomies |> List.exists (fun t -> t.Name = "category"))

module ``SiteConfig members`` =

    let config = SiteConfigDefaults.create()

    [<Fact>]
    let ``WithDevServerPort creates copy with new port`` () =
        let modified = config.WithDevServerPort(3000)
        Assert.Equal(3000, modified.DevServerPort)
        Assert.Equal(8080, config.DevServerPort) // original unchanged

    [<Fact>]
    let ``EffectiveContentDir returns dot for empty RootDir`` () =
        let cfg = { config with RootDir = "" }
        Assert.Equal(".", cfg.EffectiveContentDir)

    [<Fact>]
    let ``EffectiveContentDir returns dot for dot RootDir`` () =
        let cfg = { config with RootDir = "." }
        Assert.Equal(".", cfg.EffectiveContentDir)

    [<Fact>]
    let ``EffectiveContentDir returns RootDir when set to path`` () =
        let cfg = { config with RootDir = "src" }
        Assert.Equal("src", cfg.EffectiveContentDir)
