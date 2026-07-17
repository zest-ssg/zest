module Zest.Engine.Tests.ZcssProcessorTests

open Xunit
open Zest.Engine.Zcss

// ============================================================
// ZcssProcessor — Tests for the ZCSS parsing and compilation
// pipeline. Covers basic CSS passthrough, variable resolution,
// and built-in module (@use) imports.
// ============================================================

module ``Processor basic`` =

    [<Fact>]
    let ``simple CSS passthrough`` () =
        let input = "body { color: red; }"
        let result = Processor.processText input
        Assert.Contains("body {", result)
        Assert.Contains("color: red;", result)

    [<Fact>]
    let ``empty input produces empty output`` () =
        let result = Processor.processText ""
        Assert.True(String.length result = 0 || result.Trim() = "")

    [<Fact>]
    let ``CSS with nesting via indent syntax`` () =
        let input = """.card {
  color: blue
  .title {
    font-size: 2rem
  }
}"""
        let result = Processor.processText input
        Assert.Contains(".card", result)
        Assert.Contains(".card .title", result)
        Assert.Contains("font-size: 2rem", result)

    [<Fact>]
    let ``property without semicolon still works`` () =
        let input = ".hero { color: #333 }"
        let result = Processor.processText input
        Assert.Contains(".hero {", result)
        Assert.Contains("color: #333", result)

module ``Variable resolution`` =

    [<Fact>]
    let ``variable defined and used`` () =
        let input = "$primary: #3b82f6\n.btn { color: $primary }"
        let result = Processor.processText input
        Assert.Contains("color: #3b82f6", result)

    [<Fact>]
    let ``variable with !default is respected`` () =
        let input = "$primary: #3b82f6\n$primary: #ef4444 !default\n.btn { color: $primary }"
        let result = Processor.processText input
        // First definition wins when second is !default
        Assert.Contains("color: #3b82f6", result)

module ``Multiple selectors`` =

    [<Fact>]
    let ``multiple class selectors`` () =
        let input = ".h1 { font-size: 3rem }\n.p { color: #555 }\na { color: blue }"
        let result = Processor.processText input
        Assert.Contains(".h1", result)
        Assert.Contains(".p", result)
        Assert.Contains("a", result)

module ``@use built-in modules`` =

    [<Fact>]
    let ``@use zest:utilities imports utility classes`` () =
        let input = """@use "zest:utilities";
.card { background: white }"""
        let result = Processor.processText input
        // Should contain imported utility + user class
        Assert.Contains(".card", result)
        Assert.Contains(".display-none", result)

    [<Fact>]
    let ``@use zest:reset imports CSS reset`` () =
        let input = """@use "zest:reset";
p { margin: 1rem }"""
        let result = Processor.processText input
        Assert.Contains("p", result)
        // Reset should include box-sizing
        Assert.Contains("box-sizing", result.ToLower())

    [<Fact>]
    let ``@use with alias enables namespaced refs`` () =
        let input = """@use "zest:palette" as p;
.btn { color: $p.primary }"""
        let result = Processor.processText input
        Assert.Contains(".btn", result)
        Assert.Contains("color:", result)
        // Should resolve to a real color value (not $p.primary)
        Assert.DoesNotContain("$p.primary", result)

    [<Fact>]
    let ``multiple @use imports`` () =
        let input = """@use "zest:utilities";
@use "zest:reset";
body { margin: 0 }"""
        let result = Processor.processText input
        Assert.Contains("body", result)
        Assert.Contains(".display-block", result)
        Assert.Contains("box-sizing", result.ToLower())
