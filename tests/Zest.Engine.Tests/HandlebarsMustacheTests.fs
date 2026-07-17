module Zest.Engine.Tests.HandlebarsMustacheTests

open Xunit
open Zest.Engine
open Zest.Engine.Template

// ============================================================
// HandlebarsMustacheConverter — Tests for .hbs / .mustache
// → Nunjucks syntax conversion.
// ============================================================

module ``Handlebars → Nunjucks`` =

    let private hbs = HandlebarsMustacheConverter.convertHandlebars

    [<Fact>]
    let ``empty input returns empty`` () =
        Assert.Equal("", hbs "")
        Assert.Equal("", hbs "   ")

    [<Fact>]
    let ``variable interpolation passed through`` () =
        let r = hbs "Hello {{ name }}!"
        Assert.Equal("Hello {{ name }}!", r)

    [<Fact>]
    let ``#each list → for item in`` () =
        let r = hbs "{{#each items}}{{ this }}{{/each}}"
        Assert.Contains("{% for item in items %}", r)
        Assert.Contains("{% endfor %}", r)

    [<Fact>]
    let ``#each with as → for in`` () =
        let r = hbs "{{#each items as |post|}}{{post.title}}{{/each}}"
        Assert.Contains("{% for post in items %}", r)

    [<Fact>]
    let ``#if → if tag`` () =
        let r = hbs "<div>{{#if logged_in}}Welcome{{/if}}</div>"
        Assert.Contains("{% if logged_in %}", r)
        Assert.Contains("{% endif %}", r)

    [<Fact>]
    let ``#unless → if not`` () =
        let r = hbs "{{#unless done}}pending{{/unless}}"
        Assert.Contains("{% if not done %}", r)
        Assert.Contains("{% endif %}", r)

    [<Fact>]
    let ``else handler`` () =
        let r = hbs "{{#if flag}}yes{{else}}no{{/if}}"
        Assert.Contains("{% else %}", r)

    [<Fact>]
    let ``@index → loop.index`` () =
        let r = hbs "{{@index}}"
        Assert.Equal("{{ loop.index }}", r)

    [<Fact>]
    let ``@first / @last → loop.first / loop.last`` () =
        Assert.Equal("{{ loop.first }}", hbs "{{@first}}")
        Assert.Equal("{{ loop.last }}",  hbs "{{@last}}")

    [<Fact>]
    let ``this → item`` () =
        let r = hbs "{{this}}"
        Assert.Equal("{{ item }}", r)

    [<Fact>]
    let ``partial → include`` () =
        let r = hbs "{{> header}}"
        Assert.Contains("{% include \"header.njk\" %}", r)

    [<Fact>]
    let ``triple-stash → safe filter`` () =
        let r = hbs "{{{ body }}}"
        Assert.Equal("{{ body | safe }}", r)

    [<Fact>]
    let ``comment → Nunjucks comment`` () =
        let r = hbs "{{! this is a comment }}"
        Assert.Equal("{# this is a comment #}", r)

    [<Fact>]
    let ``log → comment`` () =
        let r = hbs "{{log \"debug message\"}}"
        Assert.Equal("{# debug message #}", r)

    [<Fact>]
    let ``full template conversion`` () =
        let template = """<html>
<body>
  <h1>{{ title }}</h1>
  {{#if items.length}}
  <ul>
    {{#each items}}
    <li>{{ this.name }} (#{{ @index }})</li>
    {{/each}}
  </ul>
  {{else}}
  <p>No items</p>
  {{/if}}
  {{> footer}}
</body>
</html>"""
        let r = hbs template
        Assert.Contains("{{ title }}", r)
        Assert.Contains("{% for item in items %}", r)
        Assert.Contains("{{ item.name }}", r)
        Assert.Contains("{{ loop.index }}", r)
        Assert.Contains("{% include \"footer.njk\" %}", r)
        Assert.Contains("{% else %}", r)

module ``Mustache → Nunjucks`` =

    let private mst = HandlebarsMustacheConverter.convertMustache

    [<Fact>]
    let ``empty input returns empty`` () =
        Assert.Equal("", mst "")
        Assert.Equal("", mst "   ")

    [<Fact>]
    let ``#section → if tag`` () =
        let r = mst "{{#content}}Body{{/content}}"
        Assert.Contains("{% if content %}", r)
        Assert.Contains("{% endif %}", r)

    [<Fact>]
    let ``^inverted → if not`` () =
        let r = mst "{{^active}}inactive{{/active}}"
        Assert.Contains("{% if not active %}", r)
        Assert.Contains("{% endif %}", r)

    [<Fact>]
    let ``partial → include`` () =
        let r = mst "{{> menu}}"
        Assert.Contains("{% include \"menu\" %}", r)

    [<Fact>]
    let ``triple-stash → safe`` () =
        let r = mst "{{{ html }}}"
        Assert.Equal("{{ html | safe }}", r)

    [<Fact>]
    let ``comment → Nunjucks comment`` () =
        let r = mst "{{! hidden note }}"
        Assert.Equal("{# hidden note #}", r)

    [<Fact>]
    let ``full mustache template`` () =
        let template = """{{#layout}}
  <h1>{{ title }}</h1>
  {{#content}}
    {{^empty}}<p>{{ text }}</p>{{/empty}}
  {{/content}}
  {{> footer}}
{{/layout}}"""
        let r = mst template
        Assert.Contains("{% if layout %}", r)
        Assert.Contains("{% if content %}", r)
        Assert.Contains("{% if not empty %}", r)
        Assert.Contains("{% include \"footer\" %}", r)
        Assert.Contains("{{ title }}", r)

module ``convertByExtension`` =

    [<Fact>]
    let ``hbs extension routes to Handlebars`` () =
        let r = HandlebarsMustacheConverter.convertByExtension ".hbs" "{{#each items}}{{/each}}"
        Assert.Contains("{% for ", r)

    [<Fact>]
    let ``mustache extension routes to Mustache`` () =
        let r = HandlebarsMustacheConverter.convertByExtension ".mustache" "{{#section}}x{{/section}}"
        Assert.Contains("{% if section %}", r)

    [<Fact>]
    let ``unknown extension returns unchanged`` () =
        let r = HandlebarsMustacheConverter.convertByExtension ".njk" "{{ name }}"
        Assert.Equal("{{ name }}", r)
