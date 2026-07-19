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

    [<Fact>]
    let ``range with single arg generates 0..n-1`` () =
        Assert.Equal("012", renderOk "{% for i in range(3) %}{{ i }}{% endfor %}" [])

    [<Fact>]
    let ``range with start and stop`` () =
        Assert.Equal("123", renderOk "{% for i in range(1, 4) %}{{ i }}{% endfor %}" [])

    [<Fact>]
    let ``range with start, stop and step`` () =
        Assert.Equal("0246", renderOk "{% for i in range(0, 8, 2) %}{{ i }}{% endfor %}" [])
        Assert.Equal("3210", renderOk "{% for i in range(3, -1, -1) %}{{ i }}{% endfor %}" [])


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

// A simple POCO used to verify reflection-based property access.
type User() =
    member val Name = "John" with get, set
    member val Age = 30 with get, set

// Local engine with an in-memory loader for inheritance/import tests.
let private memEngine (files: (string * string) list) =
    let e = NunjucksEngine()
    e.SetLoadFile(fun p ->
        let hit = files |> List.tryFind (fun (name, _) -> p.Contains(name))
        match hit with Some(_, txt) -> Ok txt | None -> Error(sprintf "not found: %s" p))
    e

module ``New features and compatibility`` =

    [<Fact>]
    let ``power operator**`` () =
        Assert.Equal("1024", renderOk "{{ 2 ** 10 }}" [])
        Assert.Equal("8", renderOk "{{ 2 ** 3 }}" [])

    [<Fact>]
    let ``whitespace control strips around tags`` () =
        Assert.Equal("AXB", renderOk "A  {{- x -}}  B" ["x", box "X"])

    [<Fact>]
    let ``safe filter preserves safe-ness through transforms`` () =
        // safe -> SafeString, upper must keep it safe (no double escaping)
        Assert.Equal("<B>", renderOk "{{ x | safe | upper }}" ["x", box "<b>"])

    [<Fact>]
    let ``poco property access via reflection`` () =
        let u = User()
        Assert.Equal("John", renderOk "{{ user.Name }}" ["user", box u])
        Assert.Equal("30", renderOk "{{ user.Age }}" ["user", box u])

    [<Fact>]
    let ``set block assignment`` () =
        Assert.Equal("Hello", renderOk "{% set greeting %}Hello{% endset %}{{ greeting }}" [])

    [<Fact>]
    let ``loop previtem nextitem depth`` () =
        let r = renderOk "{% for i in items %}{{ loop.index }}:{{ loop.previtem }}{{ loop.nextitem }} {% endfor %}" ["items", box [|1;2;3|]]
        Assert.Equal("1:2 2:13 3:2 ", r)

    [<Fact>]
    let ``super renders parent block content`` () =
        let e = memEngine [ "parent.njk", "{% block content %}PARENT{% endblock %}" ]
        let r = (e :> ITemplateEngine).Render "{% extends \"parent.njk\" %}{% block content %}{{ super() }}-CHILD{% endblock %}" (ctx [])
        match r with Ok s -> Assert.Equal("PARENT-CHILD", s) | Error er -> failwithf "%A" er

    [<Fact>]
    let ``import registers callable macros`` () =
        let e = memEngine [ "macros.njk", "{% macro hi(n) %}Hi {{ n }}{% endmacro %}" ]
        let r = (e :> ITemplateEngine).Render "{% import \"macros.njk\" as m %}{{ m.hi('Bob') }}" (ctx [])
        match r with Ok s -> Assert.Equal("Hi Bob", s) | Error er -> failwithf "%A" er

    [<Fact>]
    let ``from imports a named macro`` () =
        let e = memEngine [ "macros.njk", "{% macro hi(n) %}Hi {{ n }}{% endmacro %}" ]
        let r = (e :> ITemplateEngine).Render "{% from \"macros.njk\" import hi %}{{ hi('Bob') }}" (ctx [])
        match r with Ok s -> Assert.Equal("Hi Bob", s) | Error er -> failwithf "%A" er

    [<Fact>]
    let ``caller is callable inside a macro`` () =
        let r = renderOk "{% macro wrap() %}BEFORE{{ caller() }}AFTER{% endmacro %}{% call wrap() %}INSIDE{% endcall %}" []
        Assert.Equal("BEFOREINSIDEAFTER", r)

    [<Fact>]
    let ``tojson filter`` () =
        Assert.Equal("[1,2,3]", renderOk "{{ items | tojson }}" ["items", box [|1;2;3|]])

    [<Fact>]
    let ``filesizeformat filter`` () =
        Assert.Equal("2.0 KB", renderOk "{{ 2048 | filesizeformat }}" [])

    [<Fact>]
    let ``loop.cycle alternates values`` () =
        Assert.Equal("abc", renderOk "{% for i in items %}{{ loop.cycle('a', 'b', 'c') }}{% endfor %}" ["items", box [|1;2;3|]])
        Assert.Equal("abcab", renderOk "{% for i in items %}{{ loop.cycle('a', 'b', 'c') }}{% endfor %}" ["items", box [|1;2;3;4;5|]])

    [<Fact>]
    let ``loop.changed returns true on new value`` () =
        let r = renderOk "{% for i in items %}{% if loop.changed(i) %}{{ i }};{% endif %}{% endfor %}" ["items", box [|1;1;2;2;3|]]
        Assert.Equal("1;2;3;", r)

    [<Fact>]
    let ``nested comments are supported`` () =
        Assert.Equal("AB", renderOk "A{# x {# y #} z #}B" [])
        Assert.Equal("Hello World", renderOk "Hello{# a {# b #} c #} World" [])

    [<Fact>]
    let ``unclosed block reports line number`` () =
        match render "{% if x %}yes" [] with
        | Ok _ -> failwith "expected error for unclosed block"
        | Error (TemplateError.RuntimeError(_, line)) -> Assert.Equal(1, line)
        | Error _ -> failwith "unexpected error kind"

    [<Fact>]
    let ``unbalanced parentheses report error with line`` () =
        match render "line1\n{{ (1 + 2 }}" [] with
        | Ok _ -> failwith "expected error for unbalanced expression"
        | Error (TemplateError.RuntimeError(_, line)) -> Assert.Equal(2, line)
        | Error _ -> failwith "unexpected error kind"

// ============================================================
// Migration fixes — regression tests for MIGRATION_NOTES bugs.
// §1.4: pipe inside filter args; §1.5: int filter on decimals;
// §1.7: arithmetic combined with nested pipes.
// ============================================================

module ``Migration - filter and arithmetic fixes`` =

    [<Fact>]
    let ``int filter truncates decimal string (not 0)`` () =
        // §1.5: `int "1.245"` previously failed to 0; now floats first.
        Assert.Equal("1", renderOk "{{ n | int }}" ["n", box "1.245"])
        Assert.Equal("0", renderOk "{{ n | int }}" ["n", box "0.9"])
        Assert.Equal("42", renderOk "{{ n | int }}" ["n", box "42"])

    [<Fact>]
    let ``int filter on float value truncates toward zero`` () =
        Assert.Equal("3", renderOk "{{ n | int }}" ["n", box 3.9])
        Assert.Equal("3", renderOk "{{ n | int }}" ["n", box 3.1])

    [<Fact>]
    let ``filter argument with nested pipe evaluates`` () =
        // §1.4: `default(x | default('fallback'))` — the inner pipe in the
        // filter argument must be evaluated, not boxed as a raw string.
        Assert.Equal("fallback", renderOk "{{ v | default(missing | default('fallback')) }}" [])
        Assert.Equal("real", renderOk "{{ v | default(missing | default('fallback')) }}" ["v", box "real"])

    [<Fact>]
    let ``filter argument resolves a variable path`` () =
        // §1.4: a bare variable path passed as a filter arg must resolve.
        Assert.Equal("Y", renderOk "{{ v | default(fmt) }}" ["v", box "Y"; "fmt", box "X"])

    [<Fact>]
    let ``arithmetic with nested pipe in parentheses`` () =
        // §1.7: `((content | length) + 199) / 200` must evaluate the pipe
        // inside parentheses before the arithmetic.
        let r = renderOk "{{ ((content | length) + 199) / 200 | round }}" ["content", box "abc"]
        // length("abc")=3 → (3+199)/200 = 1.0 → round → 1
        Assert.Equal("1", r)

    [<Fact>]
    let ``pipe inside parentheses is not treated as chain separator`` () =
        // A parenthesised sub-expression containing a pipe evaluates as one atom.
        Assert.Equal("3", renderOk "{{ (content | length) }}" ["content", box "abc"])

