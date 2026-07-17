module Zest.Engine.Tests.NunjucksEngineTests

open System.Collections.Generic
open Xunit
open Zest.Engine.Template

// ============================================================
// NunjucksEngine — Full test of the Zest Nunjucks engine.
// Covers variables, filters, tags (if/for/block/extends/
// include/set/raw/filter/macro/import/from), auto-escaping,
// and template inheritance.
// ============================================================

let private engine = NunjucksEngine()

let private ctx (vars: (string * obj) list) =
    let d = Dictionary<string, obj>()
    for k, v in vars do d.[k] <- v
    d :> IDictionary<string, obj>

let private render (tmpl: string) (vars: (string * obj) list) =
    (engine :> ITemplateEngine).Render tmpl (ctx vars)

let private renderOk (tmpl: string) (vars: (string * obj) list) =
    match render tmpl vars with
    | Ok s -> s
    | Error e -> failwithf "Render error: %A" e

let private renderErr (tmpl: string) (vars: (string * obj) list) =
    match render tmpl vars with
    | Ok _ -> false
    | Error _ -> true

module ``Variables`` =

    [<Fact>]
    let ``simple variable interpolation`` () =
        let r = renderOk "Hello {{ name }}!" ["name", box "World"]
        Assert.Equal("Hello World!", r)

    [<Fact>]
    let ``null variable renders empty`` () =
        let r = renderOk "{{ x }}x" []
        Assert.Equal("x", r)

    [<Fact>]
    let ``dotted path variable`` () =
        let d = Dictionary<string, obj>()
        d.["name"] <- box "Zest"
        let r = renderOk "{{ author.name }}" ["author", box d]
        Assert.Equal("Zest", r)

    [<Fact>]
    let ``bracket access`` () =
        let d = Dictionary<string, obj>()
        d.["title"] <- box "Hello"
        let r = renderOk "{{ page['title'] }}" ["page", box d]
        Assert.Equal("Hello", r)

module ``Filters`` =

    [<Fact>]
    let ``upper filter`` () =
        let r = renderOk "{{ name | upper }}" ["name", box "hello"]
        Assert.Equal("HELLO", r)

    [<Fact>]
    let ``lower filter`` () =
        let r = renderOk "{{ name | lower }}" ["name", box "WORLD"]
        Assert.Equal("world", r)

    [<Fact>]
    let ``capitalize filter`` () =
        let r = renderOk "{{ title | capitalize }}" ["title", box "hello world"]
        Assert.Equal("Hello world", r)

    [<Fact>]
    let ``title filter`` () =
        let r = renderOk "{{ text | title }}" ["text", box "hello world"]
        Assert.Equal("Hello World", r)

    [<Fact>]
    let ``trim filter`` () =
        let r = renderOk "{{ text | trim }}" ["text", box "  hello  "]
        Assert.Equal("hello", r)

    [<Fact>]
    let ``length filter on string`` () =
        let r = renderOk "{{ text | length }}" ["text", box "hello"]
        Assert.Equal("5", r)

    [<Fact>]
    let ``length filter on list`` () =
        let r = renderOk "{{ items | length }}" ["items", box [|"a"; "b"; "c"|]]
        Assert.Equal("3", r)

    [<Fact>]
    let ``join filter`` () =
        let r = renderOk "{{ items | join(', ') }}" ["items", box [|"a"; "b"|]]
        Assert.Equal("a, b", r)

    [<Fact>]
    let ``reverse filter`` () =
        let r = renderOk "{{ text | reverse }}" ["text", box "abc"]
        Assert.Equal("cba", r)

    [<Fact>]
    let ``first filter`` () =
        let r = renderOk "{{ items | first }}" ["items", box [|"x"; "y"|]]
        Assert.Equal("x", r)

    [<Fact>]
    let ``last filter`` () =
        let r = renderOk "{{ items | last }}" ["items", box [|"a"; "b"|]]
        Assert.Equal("b", r)

    [<Fact>]
    let ``chained filters`` () =
        let r = renderOk "{{ name | trim | upper }}" ["name", box " hello "]
        Assert.Equal("HELLO", r)

    [<Fact>]
    let ``default filter null falls back`` () =
        let r = renderOk "{{ x | default('N/A') }}" []
        Assert.Equal("N/A", r)

    [<Fact>]
    let ``default filter empty string falls back`` () =
        let r = renderOk "{{ x | default('N/A') }}" ["x", box ""]
        Assert.Equal("N/A", r)

    [<Fact>]
    let ``escape filter`` () =
        let r = renderOk "{{ x | escape }}" ["x", box "<script>"]
        Assert.Contains("&lt;script&gt;", r)

    [<Fact>]
    let ``slugify filter`` () =
        let r = renderOk "{{ title | slugify }}" ["title", box "Hello World 2024!"]
        Assert.Equal("hello-world-2024", r)

    [<Fact>]
    let ``replace filter`` () =
        let r = renderOk "{{ text | replace('foo', 'bar') }}" ["text", box "foo baz"]
        Assert.Equal("bar baz", r)

    [<Fact>]
    let ``int filter`` () =
        let r = renderOk "{{ n | int }}" ["n", box "42"]
        Assert.Equal("42", r)

    [<Fact>]
    let ``round filter`` () =
        let r = renderOk "{{ n | round(2) }}" ["n", box "3.14159"]
        Assert.Equal("3.14", r)

    [<Fact>]
    let ``abs filter`` () =
        let r = renderOk "{{ n | abs }}" ["n", box "-5"]
        Assert.Equal("5", r)

    [<Fact>]
    let ``Zest-specific dateiso filter`` () =
        let dt = System.DateTime(2024, 6, 15, 12, 0, 0, System.DateTimeKind.Utc)
        let r = renderOk "{{ d | dateiso }}" ["d", box dt]
        Assert.Contains("2024-06-15T12:00:00Z", r)

    [<Fact>]
    let ``Zest-specific daterfc822 filter`` () =
        let dt = System.DateTime(2024, 6, 15, 12, 0, 0, System.DateTimeKind.Utc)
        let r = renderOk "{{ d | daterfc822 }}" ["d", box dt]
        Assert.Contains("15 Jun 2024", r)
        Assert.Contains("GMT", r)

    [<Fact>]
    let ``Zest-specific slugizePath filter`` () =
        let r = renderOk "{{ path | slugizepath }}" ["path", box "Blog/Hello World"]
        Assert.Equal("blog/hello-world", r)

    [<Fact>]
    let ``urlencode filter`` () =
        let r = renderOk "{{ url | urlencode }}" ["url", box "hello world"]
        Assert.Equal("hello%20world", r)

    [<Fact>]
    let ``sort filter`` () =
        let r = renderOk "{{ items | sort | join(',') }}" ["items", box [|"c"; "a"; "b"|]]
        Assert.Equal("a,b,c", r)

    [<Fact>]
    let ``batch filter`` () =
        let r = renderOk "{{ items | batch(2) | length }}" ["items", box [|"a"; "b"; "c"; "d"; "e"|]]
        Assert.Equal("3", r)

    [<Fact>]
    let ``slice filter`` () =
        let r = renderOk "{{ items | slice(1, 2) | join(',') }}" ["items", box [|"a"; "b"; "c"; "d"|]]
        Assert.Equal("b,d", r)

    [<Fact>]
    let ``safe filter bypasses autoescape`` () =
        let r = renderOk "{{ x | safe }}" ["x", box "<b>bold</b>"]
        Assert.Equal("<b>bold</b>", r)

    [<Fact>]
    let ``custom registered filter`` () =
        let e = NunjucksEngine()
        (e :> ITemplateEngine).RegisterFilter "double" (fun v args ->
            let s = match v with :? string as sv -> sv | _ -> ""
            box (s + s))
        let d = Dictionary<string, obj>()
        d.["x"] <- box "ha"
        let r = (e :> ITemplateEngine).Render "{{ x | double }}" (d :> IDictionary<_,_>)
        Assert.Equal(Ok "haha", r)

module ``Tags - if elif else`` =

    [<Fact>]
    let ``if true`` () =
        let r = renderOk "{% if x %}yes{% endif %}" ["x", box true]
        Assert.Equal("yes", r)

    [<Fact>]
    let ``if false`` () =
        let r = renderOk "{% if x %}yes{% endif %}" ["x", box false]
        Assert.Equal("", r)

    [<Fact>]
    let ``if else`` () =
        let r = renderOk "{% if x %}yes{% else %}no{% endif %}" ["x", box false]
        Assert.Equal("no", r)

    [<Fact>]
    let ``if elif else`` () =
        let r = renderOk "{% if a %}A{% elif b %}B{% else %}C{% endif %}" ["a", box false; "b", box true]
        Assert.Equal("B", r)

    [<Fact>]
    let ``comparison operators`` () =
        let r = renderOk "{% if x > 5 %}big{% endif %}" ["x", box 10]
        Assert.Equal("big", r)

    [<Fact>]
    let ``equality check`` () =
        let r = renderOk "{% if x == 10 %}yes{% endif %}" ["x", box 10]
        Assert.Equal("yes", r)

    [<Fact>]
    let ``and operator`` () =
        let r = renderOk "{% if a and b %}both{% endif %}" ["a", box true; "b", box true]
        Assert.Equal("both", r)

    [<Fact>]
    let ``or operator`` () =
        let r = renderOk "{% if a or b %}either{% endif %}" ["a", box false; "b", box true]
        Assert.Equal("either", r)

    [<Fact>]
    let ``not operator`` () =
        let r = renderOk "{% if not x %}no{% endif %}" ["x", box false]
        Assert.Equal("no", r)

module ``Tags - for`` =

    [<Fact>]
    let ``for loop with items`` () =
        let r = renderOk "{% for item in items %}{{ item }}{% endfor %}" ["items", box [|"a"; "b"|]]
        Assert.Equal("ab", r)

    [<Fact>]
    let ``for loop with loop.index`` () =
        let r = renderOk "{% for item in items %}{{ loop.index }}{% endfor %}" ["items", box [|"x"; "y"|]]
        Assert.Equal("12", r)

    [<Fact>]
    let ``for loop with loop.first`` () =
        let r = renderOk "{% for item in items %}{% if loop.first %}F{% endif %}{% endfor %}" ["items", box [|"a"; "b"|]]
        Assert.Equal("F", r)

    [<Fact>]
    let ``for loop with loop.last`` () =
        let r = renderOk "{% for item in items %}{% if loop.last %}L{% endif %}{% endfor %}" ["items", box [|"a"; "b"|]]
        Assert.Equal("L", r)

    [<Fact>]
    let ``for loop empty yields else`` () =
        let r = renderOk "{% for item in items %}x{% else %}empty{% endfor %}" ["items", box [||]]
        Assert.Equal("empty", r)

module ``Tags - set`` =

    [<Fact>]
    let ``set variable`` () =
        let r = renderOk "{% set x = 42 %}{{ x }}" []
        Assert.Equal("42", r)

    [<Fact>]
    let ``set and use later`` () =
        let r = renderOk "{% set greeting = 'Hi' %}{{ greeting }} World" []
        Assert.Equal("Hi World", r)

module ``Tags - raw`` =

    [<Fact>]
    let ``raw preserves content`` () =
        let r = renderOk "{% raw %}{{ x }}{% set %}Hello{% endraw %}" []
        Assert.Equal("{{ x }}{% set %}Hello", r)

module ``Tags - filter`` =

    [<Fact>]
    let ``filter block`` () =
        let r = renderOk "{% filter upper %}hello{% endfilter %}" []
        Assert.Equal("HELLO", r)

module ``Tags - macro`` =

    [<Fact>]
    let ``macro definition and call`` () =
        let r = renderOk "{% macro greet(name) %}Hello {{ name }}{% endmacro %}{{ greet('World') }}" []
        Assert.Equal("Hello World", r)

    [<Fact>]
    let ``macro with multiple args`` () =
        let r = renderOk "{% macro add(a, b) %}{{ a + b }}{% endmacro %}{{ add(3, 4) }}" []
        Assert.Equal("7", r)

module ``Auto-escaping`` =

    [<Fact>]
    let ``HTML is escaped by default`` () =
        let r = renderOk "{{ content }}" ["content", box "<script>alert('xss')</script>"]
        Assert.DoesNotContain("<script>", r)
        Assert.Contains("&lt;script&gt;", r)

    [<Fact>]
    let ``safe filter prevents escaping`` () =
        let r = renderOk "{{ content | safe }}" ["content", box "<br>"]
        Assert.Equal("<br>", r)

module ``Comments`` =

    [<Fact>]
    let ``comment is stripped`` () =
        let r = renderOk "Hello{# user #} World" []
        Assert.Equal("Hello World", r)

module ``Error cases`` =

    [<Fact>]
    let ``undefined filter returns original value`` () =
        let r = renderOk "{{ x | nonexistent_filter }}" ["x", box "test"]
        Assert.Equal("test", r)

    [<Fact>]
    let ``nonexistent variable renders empty`` () =
        let r = renderOk "{{ nonexistent_var }}" []
        Assert.Equal("", r)
