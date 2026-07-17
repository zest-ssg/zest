module Zest.Engine.Tests.FileExtensionsTests

open Xunit
open Zest.Engine

// ============================================================
// FileExtensions — Regression tests for the central extension
// constant registry. Ensures all [<Literal>] values and helper
// functions remain correct after refactoring.
// ============================================================

module ``FileExtensions constants`` =

    [<Fact>]
    let ``ZestScript is .zest.fsx`` () =
        Assert.Equal(".zest.fsx", FileExtensions.ZestScript)

    [<Fact>]
    let ``FSharpScript is .fsx`` () =
        Assert.Equal(".fsx", FileExtensions.FSharpScript)

    [<Fact>]
    let ``Markdown is .md`` () =
        Assert.Equal(".md", FileExtensions.Markdown)

    [<Fact>]
    let ``MarkdownLong is .markdown`` () =
        Assert.Equal(".markdown", FileExtensions.MarkdownLong)

    [<Fact>]
    let ``Html is .html`` () =
        Assert.Equal(".html", FileExtensions.Html)

    [<Fact>]
    let ``Nunjucks is .njk`` () =
        Assert.Equal(".njk", FileExtensions.Nunjucks)

    [<Fact>]
    let ``Liquid is .liquid`` () =
        Assert.Equal(".liquid", FileExtensions.Liquid)

    [<Fact>]
    let ``Handlebars is .hbs`` () =
        Assert.Equal(".hbs", FileExtensions.Handlebars)

    [<Fact>]
    let ``Mustache is .mustache`` () =
        Assert.Equal(".mustache", FileExtensions.Mustache)

    [<Fact>]
    let ``WebC is .webc`` () =
        Assert.Equal(".webc", FileExtensions.WebC)

    [<Fact>]
    let ``Haml is .haml`` () =
        Assert.Equal(".haml", FileExtensions.Haml)

    [<Fact>]
    let ``Pug is .pug`` () =
        Assert.Equal(".pug", FileExtensions.Pug)

    [<Fact>]
    let ``Zcss is .zcss`` () =
        Assert.Equal(".zcss", FileExtensions.Zcss)

    [<Fact>]
    let ``Css is .css`` () =
        Assert.Equal(".css", FileExtensions.Css)

    [<Fact>]
    let ``Toml is .toml`` () =
        Assert.Equal(".toml", FileExtensions.Toml)

    [<Fact>]
    let ``Yaml is .yml`` () =
        Assert.Equal(".yml", FileExtensions.Yaml)

    [<Fact>]
    let ``YamlLong is .yaml`` () =
        Assert.Equal(".yaml", FileExtensions.YamlLong)

    [<Fact>]
    let ``JavaScript is .js`` () =
        Assert.Equal(".js", FileExtensions.JavaScript)

    [<Fact>]
    let ``Image extensions are correct`` () =
        Assert.Equal(".png",  FileExtensions.Png)
        Assert.Equal(".jpg",  FileExtensions.Jpg)
        Assert.Equal(".jpeg", FileExtensions.Jpeg)
        Assert.Equal(".svg",  FileExtensions.Svg)
        Assert.Equal(".gif",  FileExtensions.Gif)
        Assert.Equal(".webp", FileExtensions.Webp)

module ``NunjucksFamily aggregate`` =

    [<Fact>]
    let ``contains all 7 Nunjucks-family extensions`` () =
        let family = FileExtensions.NunjucksFamily
        Assert.Equal(7, family.Length)
        Assert.Contains(FileExtensions.Nunjucks,   family)
        Assert.Contains(FileExtensions.Liquid,     family)
        Assert.Contains(FileExtensions.Handlebars, family)
        Assert.Contains(FileExtensions.Mustache,   family)
        Assert.Contains(FileExtensions.WebC,       family)
        Assert.Contains(FileExtensions.Haml,       family)
        Assert.Contains(FileExtensions.Pug,        family)

    [<Fact>]
    let ``does NOT contain Html or Markdown`` () =
        Assert.DoesNotContain(FileExtensions.Html,     FileExtensions.NunjucksFamily)
        Assert.DoesNotContain(FileExtensions.Markdown, FileExtensions.NunjucksFamily)

module ``Content aggregate`` =

    [<Fact>]
    let ``contains core content extensions`` () =
        let content = FileExtensions.Content
        Assert.Contains(FileExtensions.ZestScript,   content)
        Assert.Contains(FileExtensions.FSharpScript, content)
        Assert.Contains(FileExtensions.Markdown,     content)
        Assert.Contains(FileExtensions.Html,         content)

    [<Fact>]
    let ``contains all NunjucksFamily extensions`` () =
        for ext in FileExtensions.NunjucksFamily do
            Assert.Contains(ext, FileExtensions.Content)

module ``Assets aggregate`` =

    [<Fact>]
    let ``contains asset-only extensions`` () =
        let assets = FileExtensions.Assets
        Assert.Contains(FileExtensions.Zcss,       assets)
        Assert.Contains(FileExtensions.Css,        assets)
        Assert.Contains(FileExtensions.JavaScript, assets)
        Assert.Contains(FileExtensions.Png,        assets)
        Assert.Contains(FileExtensions.Svg,        assets)

    [<Fact>]
    let ``does NOT contain content extensions`` () =
        Assert.DoesNotContain(FileExtensions.Markdown, FileExtensions.Assets)
        Assert.DoesNotContain(FileExtensions.Nunjucks, FileExtensions.Assets)
        Assert.DoesNotContain(FileExtensions.Html,     FileExtensions.Assets)

module ``Helper functions`` =

    [<Fact>]
    let ``isNunjucksFamily matches njk`` () =
        Assert.True(FileExtensions.isNunjucksFamily "layout.njk")

    [<Fact>]
    let ``isNunjucksFamily matches liquid case-insensitive`` () =
        Assert.True(FileExtensions.isNunjucksFamily "page.LIQUID")

    [<Fact>]
    let ``isNunjucksFamily rejects html`` () =
        Assert.False(FileExtensions.isNunjucksFamily "index.html")

    [<Fact>]
    let ``isNunjucksFamily rejects md`` () =
        Assert.False(FileExtensions.isNunjucksFamily "post.md")

    [<Fact>]
    let ``isContent matches markdown`` () =
        Assert.True(FileExtensions.isContent "blog/post.md")

    [<Fact>]
    let ``isContent matches zest script`` () =
        Assert.True(FileExtensions.isContent "index.zest.fsx")

    [<Fact>]
    let ``isContent matches nunjucks template`` () =
        Assert.True(FileExtensions.isContent "base.njk")

    [<Fact>]
    let ``isContent rejects css`` () =
        Assert.False(FileExtensions.isContent "style.css")

    [<Fact>]
    let ``isAsset matches css`` () =
        Assert.True(FileExtensions.isAsset "theme.css")

    [<Fact>]
    let ``isAsset matches zcss`` () =
        Assert.True(FileExtensions.isAsset "main.zcss")

    [<Fact>]
    let ``isAsset matches js`` () =
        Assert.True(FileExtensions.isAsset "app.js")

    [<Fact>]
    let ``isAsset rejects md`` () =
        Assert.False(FileExtensions.isAsset "readme.md")
