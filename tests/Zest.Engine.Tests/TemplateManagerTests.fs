module Zest.Engine.Tests.TemplateManagerTests

open System.Collections.Generic
open Xunit
open Zest.Engine
open Zest.Engine.Template

// ============================================================
// TemplateManager — Tests for engine initialization, rendering,
// and cache management.
// ============================================================

let private defaultConfig: TemplateConfig = {
    Engine = "nunjucks"
    EnableCache = true
    Extension = FileExtensions.Html
    Filters = []
}

module ``initEngine`` =

    [<Fact>]
    let ``init nunjucks engine succeeds`` () =
        let result = TemplateManager.initEngine "nunjucks" defaultConfig
        Assert.True(result.IsSome)

    [<Fact>]
    let ``init njk alias succeeds`` () =
        let result = TemplateManager.initEngine "njk" defaultConfig
        Assert.True(result.IsSome)

    [<Fact>]
    let ``init unknown engine returns None`` () =
        let result = TemplateManager.initEngine "unknown" defaultConfig
        Assert.True(result.IsNone)

    [<Fact>]
    let ``init with custom filters`` () =
        let shout (v: obj) (_: string list) : obj =
            match v with :? string as s -> box (s.ToUpper() + "!") | _ -> v
        let cfg = { defaultConfig with Filters = [ ("shout", shout) ] }
        let result = TemplateManager.initEngine "nunjucks" cfg
        Assert.True(result.IsSome)

module ``getEngine`` =

    [<Fact>]
    let ``get after init returns engine`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        let result = TemplateManager.getEngine "nunjucks"
        Assert.True(result.IsSome)

    [<Fact>]
    let ``get without init returns None`` () =
        let result = TemplateManager.getEngine "nonexistent_engine_xyz"
        Assert.True(result.IsNone)

module ``getOrCreateEngine`` =

    [<Fact>]
    let ``getOrCreate creates on first call`` () =
        let result = TemplateManager.getOrCreateEngine "nunjucks2" defaultConfig
        Assert.True(result.IsSome)

module ``render`` =

    [<Fact>]
    let ``render simple template succeeds`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        let d = Dictionary<string, obj>()
        d.["name"] <- box "Zest"
        let result = TemplateManager.render "nunjucks" "Hello {{ name }}!" (d :> IDictionary<_,_>)
        Assert.Equal(Result<string, TemplateError>.Ok "Hello Zest!", result)

    [<Fact>]
    let ``render with uninitialized engine fails`` () =
        let d = Dictionary<string, obj>()
        let result = TemplateManager.render "unknown_engine" "text" (d :> IDictionary<_,_>)
        match result with
        | Error _ -> Assert.True(true)
        | _ -> Assert.True(false, "Expected error")

module ``renderLayout`` =

    [<Fact>]
    let ``renderLayout with nunjucks engine`` () =
        // Layout uses template variable "content" as body placeholder
        let layoutText = "<html><body>{{ content | safe }}</body></html>"
        let vars = Dictionary<string, string>()
        vars.["content"] <- "<h1>Hello</h1>"
        let cfg = { defaultConfig with Engine = "nunjucks" }
        TemplateManager.initEngine "nunjucks" cfg |> ignore
        let result = TemplateManager.renderLayout cfg "_layout.html" layoutText (vars :> IDictionary<_,_>)
        match result with
        | Ok s ->
            Assert.Contains("<html>", s)
            Assert.Contains("<h1>Hello</h1>", s)
        | Error e -> failwithf "Render error: %A" e

    [<Fact>]
    let ``renderLayout with native engine returns error`` () =
        let cfg = { defaultConfig with Engine = "native" }
        let vars = Dictionary<string, string>()
        let result = TemplateManager.renderLayout cfg "layout.html" "<html>{{ content }}</html>" (vars :> IDictionary<_,_>)
        match result with
        | Error _ -> Assert.True(true)
        | _ -> Assert.True(false, "Expected error for native engine")

module ``clearCaches`` =

    [<Fact>]
    let ``clearCaches does not throw`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        TemplateManager.clearCaches()
        Assert.True(true)

    [<Fact>]
    let ``clearCaches is idempotent`` () =
        TemplateManager.clearCaches()
        TemplateManager.clearCaches()
        Assert.True(true)

    [<Fact>]
    let ``after clearCaches, engine must be re-initialized`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        TemplateManager.clearCaches()
        // After clear, getOrCreate should work (re-creates)
        let result = TemplateManager.getOrCreateEngine "nunjucks" defaultConfig
        Assert.True(result.IsSome)

module ``buildNestedContext`` =

    [<Fact>]
    let ``flat keys remain flat`` () =
        let pairs = [ "title", box "Hello" ]
        let ctx = TemplateManager.buildNestedContext pairs
        Assert.Equal(box "Hello", ctx.["title"])

    [<Fact>]
    let ``dotted keys become nested`` () =
        let pairs = [ "site.title", box "My Site"; "site.url", box "https://example.com" ]
        let ctx = TemplateManager.buildNestedContext pairs
        let site = ctx.["site"] :?> IDictionary<string, obj>
        Assert.Equal(box "My Site", site.["title"])
        Assert.Equal(box "https://example.com", site.["url"])

    [<Fact>]
    let ``mixed flat and nested keys`` () =
        let pairs = [
            "title", box "Page"
            "author.name", box "John"
            "author.email", box "john@test.com"
        ]
        let ctx = TemplateManager.buildNestedContext pairs
        Assert.Equal(box "Page", ctx.["title"])
        let author = ctx.["author"] :?> IDictionary<string, obj>
        Assert.Equal(box "John", author.["name"])

module ``isEngineAvailable`` =

    [<Fact>]
    let ``available after init`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        Assert.True(TemplateManager.isEngineAvailable "nunjucks")

    [<Fact>]
    let ``not available without init`` () =
        Assert.False(TemplateManager.isEngineAvailable "never_inited_engine")

module ``listEngines`` =

    [<Fact>]
    let ``returns engines after init`` () =
        TemplateManager.initEngine "nunjucks" defaultConfig |> ignore
        TemplateManager.initEngine "njk" defaultConfig |> ignore
        let engs = TemplateManager.listEngines()
        Assert.Contains("nunjucks", engs)
        Assert.Contains("njk", engs)
