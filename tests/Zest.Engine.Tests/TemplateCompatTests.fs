module Zest.Engine.Tests.TemplateCompatTests

open Xunit
open Zest.Engine
open Zest.Engine.Template

// ============================================================
// TemplateCompat — Tests for strategy registry, extension
// routing, and the convertIfNeeded pipeline.
// ============================================================

module ``strategyFor`` =

    [<Fact>]
    let ``njk → Nunjucks`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor FileExtensions.Nunjucks)

    [<Fact>]
    let ``liquid → Nunjucks`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor FileExtensions.Liquid)

    [<Fact>]
    let ``hbs → Nunjucks`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor FileExtensions.Handlebars)

    [<Fact>]
    let ``mustache → Nunjucks`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor FileExtensions.Mustache)

    [<Fact>]
    let ``webc → Nunjucks`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor FileExtensions.WebC)

    [<Fact>]
    let ``haml → ConvertThenNunjucks`` () =
        Assert.Equal(Some TemplateStrategy.ConvertThenNunjucks,
                     TemplateCompat.strategyFor FileExtensions.Haml)

    [<Fact>]
    let ``pug → ConvertThenNunjucks`` () =
        Assert.Equal(Some TemplateStrategy.ConvertThenNunjucks,
                     TemplateCompat.strategyFor FileExtensions.Pug)

    [<Fact>]
    let ``html → HtmlOnly`` () =
        Assert.Equal(Some TemplateStrategy.HtmlOnly,
                     TemplateCompat.strategyFor FileExtensions.Html)

    [<Fact>]
    let ``zest.fsx → NativeScript`` () =
        Assert.Equal(Some TemplateStrategy.NativeScript,
                     TemplateCompat.strategyFor FileExtensions.ZestScript)

    [<Fact>]
    let ``md → MarkdownOnly`` () =
        Assert.Equal(Some TemplateStrategy.MarkdownOnly,
                     TemplateCompat.strategyFor FileExtensions.Markdown)

    [<Fact>]
    let ``unknown extension returns None`` () =
        Assert.Equal(None, TemplateCompat.strategyFor ".xlsx")
        Assert.Equal(None, TemplateCompat.strategyFor ".pdf")

    [<Fact>]
    let ``case insensitivity`` () =
        Assert.Equal(Some TemplateStrategy.Nunjucks,
                     TemplateCompat.strategyFor ".NJK")
        Assert.Equal(Some TemplateStrategy.ConvertThenNunjucks,
                     TemplateCompat.strategyFor ".HAML")

module ``isNunjucksCompatible`` =

    [<Fact>]
    let ``nunjucks-family extensions are compatible`` () =
        Assert.True(TemplateCompat.isNunjucksCompatible FileExtensions.Nunjucks)
        Assert.True(TemplateCompat.isNunjucksCompatible FileExtensions.Liquid)
        Assert.True(TemplateCompat.isNunjucksCompatible FileExtensions.Handlebars)
        Assert.True(TemplateCompat.isNunjucksCompatible FileExtensions.Mustache)
        Assert.True(TemplateCompat.isNunjucksCompatible FileExtensions.WebC)

    [<Fact>]
    let ``haml and pug are not nunjucks compatible (need conversion)`` () =
        Assert.False(TemplateCompat.isNunjucksCompatible FileExtensions.Haml)
        Assert.False(TemplateCompat.isNunjucksCompatible FileExtensions.Pug)

    [<Fact>]
    let ``html and md are not nunjucks compatible`` () =
        Assert.False(TemplateCompat.isNunjucksCompatible FileExtensions.Html)
        Assert.False(TemplateCompat.isNunjucksCompatible FileExtensions.Markdown)

module ``needsConversion`` =

    [<Fact>]
    let ``haml and pug need conversion`` () =
        Assert.True(TemplateCompat.needsConversion FileExtensions.Haml)
        Assert.True(TemplateCompat.needsConversion FileExtensions.Pug)

    [<Fact>]
    let ``nunjucks family does not need conversion`` () =
        Assert.False(TemplateCompat.needsConversion FileExtensions.Nunjucks)
        Assert.False(TemplateCompat.needsConversion FileExtensions.Liquid)

module ``isKnownTemplate`` =

    [<Fact>]
    let ``all registered extensions are known`` () =
        Assert.True(TemplateCompat.isKnownTemplate FileExtensions.Nunjucks)
        Assert.True(TemplateCompat.isKnownTemplate FileExtensions.Markdown)
        Assert.True(TemplateCompat.isKnownTemplate FileExtensions.ZestScript)

    [<Fact>]
    let ``unknown extensions are not known`` () =
        Assert.False(TemplateCompat.isKnownTemplate ".txt")
        Assert.False(TemplateCompat.isKnownTemplate ".dat")

module ``allExtensions`` =

    [<Fact>]
    let ``returns all 14 supported extensions`` () =
        let all = TemplateCompat.allExtensions
        Assert.True(all.Length >= 12)
        Assert.Contains(FileExtensions.Nunjucks, all)
        Assert.Contains(FileExtensions.Haml, all)
        Assert.Contains(FileExtensions.Markdown, all)
        Assert.Contains(FileExtensions.ZestScript, all)

module ``convertIfNeeded`` =

    [<Fact>]
    let ``haml body is converted to HTML`` () =
        let r = TemplateCompat.convertIfNeeded FileExtensions.Haml "%p Hello"
        Assert.Contains("<p>Hello</p>", r)

    [<Fact>]
    let ``pug body is converted to HTML`` () =
        let r = TemplateCompat.convertIfNeeded FileExtensions.Pug "p Hello"
        Assert.Contains("<p>Hello</p>", r)

    [<Fact>]
    let ``nunjucks body is unchanged`` () =
        let r = TemplateCompat.convertIfNeeded FileExtensions.Nunjucks "{{ title }}"
        Assert.Equal("{{ title }}", r)

    [<Fact>]
    let ``unknown extension body is unchanged`` () =
        let r = TemplateCompat.convertIfNeeded ".txt" "plain text"
        Assert.Equal("plain text", r)

    [<Fact>]
    let ``html body is unchanged`` () =
        let r = TemplateCompat.convertIfNeeded FileExtensions.Html "<h1>Hi</h1>"
        Assert.Equal("<h1>Hi</h1>", r)
