module Zest.Engine.Tests.DslInlineBlocksTests

open Xunit
open Zest.Dsl.DslUtilities

// ============================================================
// DslInlineBlocks — Tests for the md-like triple-quote inline
// content blocks: `dedent`, `mdDedent`, `js`, `jsModule`,
// `jsonBlock`. See MIGRATION_NOTES §3.2 (dedent) and §九 (L2/L3).
// ============================================================

module ``dedent`` =

    [<Fact>]
    let ``strips common leading whitespace`` () =
        let input = "    line one\n    line two"
        Assert.Equal("line one\nline two", dedent input)

    [<Fact>]
    let ``preserves blank lines without counting them`` () =
        let input = "    a\n\n    b"
        Assert.Equal("a\n\nb", dedent input)

    [<Fact>]
    let ``returns input unchanged when no common indent`` () =
        Assert.Equal("a\nb", dedent "a\nb")

    [<Fact>]
    let ``handles mixed indent using the minimum`` () =
        let input = "        deep\n    shallow"
        Assert.Equal("    deep\nshallow", dedent input)

    [<Fact>]
    let ``empty string returns empty`` () =
        Assert.Equal("", dedent "")

module ``mdDedent`` =

    [<Fact>]
    let ``renders indented markdown headings`` () =
        // §3.2: indented `##` previously failed to render as a heading.
        let html = mdDedent "    ## Title"
        Assert.Contains("<h2", html)
        Assert.Contains("Title", html)

    [<Fact>]
    let ``renders indented fenced code block`` () =
        let html = mdDedent "    ```js\n    var x = 1\n    ```"
        Assert.Contains("<pre>", html)
        Assert.Contains("var x = 1", html)

module ``js`` =

    [<Fact>]
    let ``wraps raw code in script tag`` () =
        let r = js "console.log('hi')"
        Assert.Equal("<script>console.log('hi')</script>", r)

    [<Fact>]
    let ``dedents triple-quoted body`` () =
        let r = js "    alert(1)"
        Assert.Equal("<script>alert(1)</script>", r)

    [<Fact>]
    let ``preserves inner script-breaking sequences verbatim`` () =
        // `js` is raw passthrough (like `md`); authors own their JS.
        let r = js "if (a < b) { c() }"
        Assert.Contains("a < b", r)

module ``jsModule`` =

    [<Fact>]
    let ``emits type=module script`` () =
        let r = jsModule "import { x } from './m.js'"
        Assert.Equal("<script type=\"module\">import { x } from './m.js'</script>", r)

module ``jsonBlock`` =

    [<Fact>]
    let ``serialises data to window assignment`` () =
        let r = jsonBlock "CFG" {| theme = "dark" |}
        Assert.Contains("<script>window.CFG = ", r)
        Assert.Contains("\"theme\":\"dark\"", r)
        Assert.EndsWith("</script>", r)

    [<Fact>]
    let ``neutralises script-breaking sequences`` () =
        // A value containing `</` must not prematurely close the script tag.
        let r = jsonBlock "D" {| html = "</script><b>" |}
        Assert.DoesNotContain("</script><b>", r)
        // The `</` is escaped so the only real </script> is the closing tag.
        let closeCount = r.Split("</script>").Length - 1
        Assert.Equal(1, closeCount)

    [<Fact>]
    let ``handles arrays and numbers`` () =
        let r = jsonBlock "TAGS" {| tags = [|"fsharp"; "ssg"|]; count = 2 |}
        Assert.Contains("[\"fsharp\",\"ssg\"]", r)
        Assert.Contains("\"count\":2", r)

// ============================================================
// DslSugar — Option rendering, joining, text formatting helpers
// ============================================================

module ``DslSugar option helpers`` =

    open Zest.Dsl.DslSugar

    [<Fact>]
    let ``opt_str renders Some and None`` () =
        Assert.Equal("hi", opt_str (Some "hi"))
        Assert.Equal("", opt_str None)

    [<Fact>]
    let ``opt_or uses fallback for None or empty`` () =
        Assert.Equal("hi", opt_or "fallback" (Some "hi"))
        Assert.Equal("fallback", opt_or "fallback" None)
        Assert.Equal("fallback", opt_or "fallback" (Some ""))

    [<Fact>]
    let ``opt_map applies function only for Some`` () =
        Assert.Equal("VAL", opt_map (fun (x: string) -> x.ToUpper()) (Some "val"))
        Assert.Equal("", opt_map (fun (x: string) -> x.ToUpper()) None)

module ``DslSugar joining`` =

    open Zest.Dsl.DslSugar

    [<Fact>]
    let ``intersperse places separator between items`` () =
        Assert.Equal("a, b, c", intersperse ", " ["a"; "b"; "c"])
        Assert.Equal("a", intersperse ", " ["a"])
        Assert.Equal("", intersperse ", " [])

    [<Fact>]
    let ``join_lines and join_comma`` () =
        Assert.Equal("a\nb", join_lines ["a"; "b"])
        Assert.Equal("a, b", join_comma ["a"; "b"])

module ``DslSugar text formatting`` =

    open Zest.Dsl.DslSugar

    [<Fact>]
    let ``truncate_str adds ellipsis when cut`` () =
        Assert.Equal("Hello, wor…", truncate_str 10 "Hello, world!")
        Assert.Equal("Short", truncate_str 10 "Short")

    [<Fact>]
    let ``pluralize appends s for plural`` () =
        Assert.Equal("1 item", pluralize 1 "item")
        Assert.Equal("3 items", pluralize 3 "item")

    [<Fact>]
    let ``pluralize_with uses explicit plural`` () =
        Assert.Equal("1 child", pluralize_with 1 "child" "children")
        Assert.Equal("2 children", pluralize_with 2 "child" "children")

    [<Fact>]
    let ``capitalize uppercases first char`` () =
        Assert.Equal("Hello", capitalize "hello")
        Assert.Equal("", capitalize "")

    [<Fact>]
    let ``titleize converts kebab and snake to title`` () =
        Assert.Equal("Post List", titleize "post-list")
        Assert.Equal("Post List", titleize "post_list")

// ============================================================
// DslHtml attribute sugar & misc helpers
// ============================================================

module ``DslHtml attribute sugar`` =

    open Zest.Dsl.Dsl

    [<Fact>]
    let ``data builds data- attribute`` () =
        Assert.Equal("data-id=\"42\"", data' "id" "42")

    [<Fact>]
    let ``aria builds aria- attribute`` () =
        Assert.Equal("aria-label=\"Close\"", aria "label" "Close")

    [<Fact>]
    let ``boolAttr present when true, absent when false`` () =
        Assert.Equal("disabled=\"disabled\"", boolAttr "disabled" true)
        Assert.Equal("", boolAttr "disabled" false)

    [<Fact>]
    let ``comment wraps body`` () =
        Assert.Equal("<!-- note -->", comment "note")

    [<Fact>]
    let ``fragment concatenates without wrapper`` () =
        Assert.Equal("ab", fragment ["a"; "b"])

    [<Fact>]
    let ``attrsOf builds multiple attrs`` () =
        Assert.Equal("id=\"main\" class=\"page\"", attrsOf [("id","main"); ("class","page")])

    [<Fact>]
    let ``el builds element from pairs`` () =
        let r = el "div" [("id","x")] [text "hi"]
        Assert.Equal("<div id=\"x\">hi</div>", r)

