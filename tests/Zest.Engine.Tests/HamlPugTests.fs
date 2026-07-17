module Zest.Engine.Tests.HamlPugTests

open Xunit
open Zest.Engine.Template

// ============================================================
// HamlConverter & PugConverter — Tests for .haml / .pug
// → HTML conversion.
// ============================================================

module ``HamlConverter`` =

    let private haml = HamlConverter.convert

    [<Fact>]
    let ``empty input returns empty`` () =
        Assert.Equal("", haml "")
        Assert.Equal("", haml "   ")

    [<Fact>]
    let ``simple tag`` () =
        let r = haml "%p Hello"
        Assert.Contains("<p>Hello</p>", r)

    [<Fact>]
    let ``tag with class`` () =
        let r = haml "%div.container"
        Assert.Contains("class=\"container\"", r)

    [<Fact>]
    let ``tag with id`` () =
        let r = haml "%section#hero"
        Assert.Contains("id=\"hero\"", r)

    [<Fact>]
    let ``tag with id and class`` () =
        let r = haml "%div#main.content"
        Assert.Contains("id=\"main\"", r)
        Assert.Contains("class=\"content\"", r)

    [<Fact>]
    let ``implicit div by class`` () =
        let r = haml ".sidebar"
        Assert.Contains("<div", r)
        Assert.Contains("class=\"sidebar\"", r)

    [<Fact>]
    let ``implicit div by id`` () =
        let r = haml "#header"
        Assert.Contains("<div", r)
        Assert.Contains("id=\"header\"", r)

    [<Fact>]
    let ``nesting with indent`` () =
        let r = haml "%ul\n  %li Item 1\n  %li Item 2"
        Assert.Contains("<ul>", r)
        Assert.Contains("<li>Item 1</li>", r)
        Assert.Contains("<li>Item 2</li>", r)

    [<Fact>]
    let ``inline attributes`` () =
        let r = haml """%a{href: '/about', class: 'link'} About"""
        Assert.Contains("href=\"/about\"", r)
        Assert.Contains("class=\"link\"", r)
        Assert.Contains("About</a>", r)

    [<Fact>]
    let ``self-closing tags`` () =
        let r = haml "%br\n%hr"
        Assert.Contains("<br />", r)
        Assert.Contains("<hr />", r)

    [<Fact>]
    let ``expression to double braces`` () =
        let r = haml "  %h1= title"
        Assert.Contains("{{ title }}", r)

    [<Fact>]
    let ``silent code is stripped`` () =
        let r = haml "- var x = 1\n%p Text"
        Assert.DoesNotContain("var x", r)
        Assert.Contains("<p>", r)

    [<Fact>]
    let ``comment becomes HTML comment`` () =
        let r = haml "/ This is a comment"
        Assert.Contains("<!-- This is a comment -->", r)

    [<Fact>]
    let ``full layout template`` () =
        let template = "%html\n  %head\n    %title My Site\n  %body\n    #header\n      %h1.site-title= title\n    .content\n      %p Welcome to my site"
        let r = haml template
        Assert.Contains("<html>", r)
        Assert.Contains("<head>", r)
        Assert.Contains("<title>My Site</title>", r)
        Assert.Contains("<body>", r)
        Assert.Contains("id=\"header\"", r)
        Assert.Contains("class=\"site-title\"", r)
        Assert.Contains("{{ title }}", r)
        Assert.Contains("class=\"content\"", r)

module ``PugConverter`` =

    let private pug = PugConverter.convert

    [<Fact>]
    let ``empty input returns empty`` () =
        Assert.Equal("", pug "")
        Assert.Equal("", pug "   ")

    [<Fact>]
    let ``simple tag`` () =
        let r = pug "p Hello world"
        Assert.Contains("<p>Hello world</p>", r)

    [<Fact>]
    let ``tag with class`` () =
        let r = pug "h1.title My Title"
        Assert.Contains("class=\"title\"", r)
        Assert.Contains("My Title</h1>", r)

    [<Fact>]
    let ``tag with id`` () =
        let r = pug "section#hero"
        Assert.Contains("id=\"hero\"", r)

    [<Fact>]
    let ``tag with id and class`` () =
        let r = pug "div#main.content.fluid"
        Assert.Contains("id=\"main\"", r)
        Assert.Contains("class=\"content fluid\"", r)

    [<Fact>]
    let ``implicit div by class`` () =
        let r = pug ".container"
        Assert.Contains("<div", r)
        Assert.Contains("class=\"container\"", r)

    [<Fact>]
    let ``implicit div by id`` () =
        let r = pug "#sidebar"
        Assert.Contains("<div", r)
        Assert.Contains("id=\"sidebar\"", r)

    [<Fact>]
    let ``nesting with indent`` () =
        let r = pug "ul\n  li First\n  li Second"
        Assert.Contains("<ul>", r)
        Assert.Contains("<li>First</li>", r)
        Assert.Contains("<li>Second</li>", r)

    [<Fact>]
    let ``parenthesized attributes`` () =
        let r = pug """a(href='/home' class='link') Home"""
        Assert.Contains("href=\"/home\"", r)
        Assert.Contains("class=\"link\"", r)
        Assert.Contains("Home</a>", r)

    [<Fact>]
    let ``self-closing tags`` () =
        let r = pug "br\nhr\nimg(src='a.png')"
        Assert.Contains("<br />", r)
        Assert.Contains("<hr />", r)
        Assert.Contains("<img", r)

    [<Fact>]
    let ``expression to double braces`` () =
        let r = pug "p= title"
        Assert.Contains("{{ title }}", r)

    [<Fact>]
    let ``pipe text`` () =
        let r = pug "p\n  | Hello World"
        Assert.Contains("Hello World", r)

    [<Fact>]
    let ``silent code is stripped`` () =
        let r = pug "- var x = 1\np Text"
        Assert.DoesNotContain("var x", r)
        Assert.Contains("<p>", r)

    [<Fact>]
    let ``HTML comment`` () =
        let r = pug "// A comment"
        Assert.Contains("<!-- A comment -->", r)

    [<Fact>]
    let ``full layout template`` () =
        let template = "doctype html\nhtml\n  head\n    title My Pug Site\n  body\n    header.header\n      h1#logo= siteTitle\n    main.content\n      p Welcome to my site"
        let r = pug template
        Assert.Contains("<html>", r)
        Assert.Contains("<title>My Pug Site</title>", r)
        Assert.Contains("class=\"header\"", r)
        Assert.Contains("id=\"logo\"", r)
        Assert.Contains("{{ siteTitle }}", r)
        Assert.Contains("class=\"content\"", r)

module ``Haml vs Pug differentiation`` =

    [<Fact>]
    let ``haml uses percent prefix, pug does not`` () =
        let h = HamlConverter.convert "%span Text"
        Assert.Contains("<span>", h)

        let p = PugConverter.convert "span Text"
        Assert.Contains("<span>", p)

    [<Fact>]
    let ``haml attrs in braces, pug in parens`` () =
        let h = HamlConverter.convert "%a{href: '/a'} Link"
        Assert.Contains("href=\"/a\"", h)

        let p = PugConverter.convert "a(href='/a') Link"
        Assert.Contains("href=\"/a\"", p)
