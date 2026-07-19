namespace Zest.Engine.Template

open System
open Zest.Engine

// ============================================================
// TemplateCompat — Template Language Compatibility Registry
// ============================================================
// Registers all 11ty-compatible template language extensions
// and maps them to Zest's processing pipeline.
//
// In `native` template mode (the default), HTML files are routed
// through the Nunjucks compat layer so `{{ }}` / `{% %}` syntax
// works in plain HTML, and `.zest.fsx` files may serve as templates.
//
// Extension  │ Processing Strategy              │ Status
// ────────────────────────────────────────────────────────────
// .njk      │ Nunjucks engine (native support)  │ Full
// .liquid   │ Nunjucks engine + Liquid compat   │ Full
// .hbs      │ Handlebars→Nunjucks converter     │ Full
// .mustache │ Mustache→Nunjucks converter       │ Full
// .webc     │ WebC SSR preprocessor + Nunjucks  │ Full
// .haml     │ HamlConverter → HTML → Nunjucks   │ Full
// .pug      │ PugConverter → HTML → Nunjucks    │ Full
// .html     │ Nunjucks compat layer (native)    │ Full
// .zest.fsx │ F# HTML DSL (native template)     │ Full
// .md       │ Markdown → HTML                   │ Full
// ============================================================

/// Describes how a template language should be processed.
type TemplateStrategy =
    /// Use the Nunjucks engine directly.
    | Nunjucks
    /// Convert first (HAML/Pug → HTML), then optionally Nunjucks process.
    | ConvertThenNunjucks
    /// Markdown processing only (no template engine).
    | MarkdownOnly
    /// HTML passthrough routed through the Nunjucks compat layer
    /// (native mode — `{{ }}` / `{% %}` work in plain HTML).
    | HtmlOnly
    /// F# HTML DSL template (evaluated as a .zest.fsx script).
    | NativeScript

/// Template language compatibility registry.
module TemplateCompat =

    /// Mapping from file extension to template strategy.
    let private strategyMap =
        Map.ofList [
            FileExtensions.Nunjucks,      Nunjucks
            FileExtensions.Liquid,        Nunjucks
            FileExtensions.Handlebars,    Nunjucks
            FileExtensions.Mustache,      Nunjucks
            FileExtensions.Haml,          ConvertThenNunjucks
            FileExtensions.Pug,           ConvertThenNunjucks
            FileExtensions.WebC,          Nunjucks
            FileExtensions.Html,          HtmlOnly
            FileExtensions.HtmlLong,      HtmlOnly
            FileExtensions.ZestScript,    NativeScript
            FileExtensions.FSharpScript,  NativeScript
            FileExtensions.Markdown,      MarkdownOnly
            FileExtensions.MarkdownLong,  MarkdownOnly
        ]

    /// Get the template strategy for a given file extension.
    let strategyFor (ext: string) : TemplateStrategy option =
        strategyMap |> Map.tryFind (ext.ToLowerInvariant())

    /// Check whether an extension is a Nunjucks-compatible template language.
    let isNunjucksCompatible (ext: string) : bool =
        match strategyFor ext with
        | Some Nunjucks -> true
        | _ -> false

    /// Check whether an extension requires pre-conversion (HAML/Pug).
    let needsConversion (ext: string) : bool =
        match strategyFor ext with
        | Some ConvertThenNunjucks -> true
        | _ -> false

    /// Check whether an extension is a recognized template language.
    let isKnownTemplate (ext: string) : bool =
        strategyFor ext |> Option.isSome

    /// List of all supported template extensions.
    let allExtensions : string list =
        strategyMap |> Map.keys |> List.ofSeq

    /// Convert a template body to Nunjucks-compatible syntax based on its
    /// extension. Handles HAML, Pug (→ HTML), Handlebars, and Mustache (→
    /// Nunjucks). Returns the body unchanged for extensions that need no
    /// conversion. Results are cached by content hash (via the converters).
    let convertIfNeeded (ext: string) (body: string) : string =
        match ext.ToLowerInvariant() with
        | FileExtensions.Haml         -> HamlConverter.convert body
        | FileExtensions.Pug          -> PugConverter.convert body
        | FileExtensions.Handlebars
        | FileExtensions.Mustache     -> HandlebarsMustacheConverter.convertByExtension ext body
        | _                           -> body
