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

    [<Fact>]
    let ``F#-style let var with hyphen resolves`` () =
        // §1.10: hyphenated `let` variable names (font-size) must resolve as a
        // whole token, not split at the hyphen.
        let input = "let font-size = 1rem\n.root { font-size: font-size }"
        let result = Processor.processText input
        Assert.Contains("font-size: 1rem", result)
        Assert.DoesNotContain("font-size: font-size", result)

    [<Fact>]
    let ``SCSS-style var with hyphen resolves`` () =
        let input = "$line-height = 1.5\n.p { line-height: $line-height }"
        let result = Processor.processText input
        Assert.Contains("line-height: 1.5", result)

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

// ============================================================
// ZCSS enhancements — function-name passthrough, error guard,
// result cache. See engine upgrade (syntax compat / robustness).
// ============================================================

module ``CSS function passthrough`` =

    [<Fact>]
    let ``color-mix is not resolved as a variable`` () =
        // CSS function names must pass through verbatim even if a same-named
        // `let` binding exists, so `color-mix(...)` never breaks.
        let input = "let color-mix = red\n.x { color: color-mix(in srgb, red, blue) }"
        let result = Processor.processText input
        Assert.Contains("color-mix(in srgb, red, blue)", result)

    [<Fact>]
    let ``calc and clamp pass through`` () =
        let input = ".x { width: calc(100% - 2rem); font-size: clamp(1rem, 2vw, 2rem) }"
        let result = Processor.processText input
        // Function names must survive (not be resolved as variables).
        Assert.Contains("calc(", result)
        Assert.Contains("clamp(", result)
        // Numeric operands preserved.
        Assert.Contains("100%", result)
        Assert.Contains("2rem", result)

    [<Fact>]
    let ``gradient functions pass through`` () =
        let input = ".x { background: linear-gradient(to right, red, blue) }"
        let result = Processor.processText input
        Assert.Contains("linear-gradient(to right, red, blue)", result)

    [<Fact>]
    let ``transform functions pass through`` () =
        let input = ".x { transform: rotate(45deg) scale(1.5) }"
        let result = Processor.processText input
        Assert.Contains("rotate(45deg)", result)
        Assert.Contains("scale(1.5)", result)

module ``Error handling`` =

    [<Fact>]
    let ``malformed input does not crash`` () =
        // Unclosed brace — the parser is resilient and may return partial CSS
        // rather than throwing. The contract is: return a string, don't crash.
        let input = ".x { color: red"
        let result = Processor.processText input
        Assert.NotNull result

    [<Fact>]
    let ``empty input returns empty or minimal output`` () =
        let result = Processor.processText ""
        Assert.NotNull result

module ``Result cache`` =

    [<Fact>]
    let ``same input yields same output`` () =
        let input = ".a { color: red }"
        let r1 = Processor.processText input
        let r2 = Processor.processText input
        Assert.Equal(r1, r2)

