namespace Zest.Engine.Template

open System

// ============================================================
// TemplateCompat — Template Language Compatibility Registry
// ============================================================
// Registers all 11ty-compatible template language extensions
// and maps them to Zest's processing pipeline.
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
// .html     │ Nunjucks preprocessor (11ty mode) │ Full
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
    /// HTML passthrough (can optionally apply Nunjucks preprocessing).
    | HtmlOnly

/// Template language compatibility registry.
module TemplateCompat =

    /// Mapping from file extension to template strategy.
    let private strategyMap =
        Map.ofList [
            ".njk",      Nunjucks
            ".liquid",   Nunjucks
            ".hbs",      Nunjucks
            ".mustache", Nunjucks
            ".haml",     ConvertThenNunjucks
            ".pug",      ConvertThenNunjucks
            ".webc",     Nunjucks
            ".html",     HtmlOnly
            ".htm",      HtmlOnly
            ".md",       MarkdownOnly
            ".markdown", MarkdownOnly
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

    /// Convert a HAML/Pug body to HTML for further processing.
    /// Returns the same string unchanged for non-conversion extensions.
    let convertIfNeeded (ext: string) (body: string) : string =
        match strategyFor ext with
        | Some ConvertThenNunjucks ->
            match ext.ToLowerInvariant() with
            | ".haml" -> HamlConverter.convert body
            | ".pug"  -> PugConverter.convert body
            | _       -> body
        | _ -> body
