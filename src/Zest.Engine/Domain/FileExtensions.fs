namespace Zest.Engine

// ============================================================
// FileExtensions — Central registry of file extensions
// ============================================================
// Single source of truth for all file extensions recognized by
// the Zest build pipeline. Replaces scattered hardcoded strings
// across TemplateCompat, LayoutEngine, ScriptEvaluator, etc.
//
// Usage: reference these constants instead of literal ".njk" /
// ".zcss" / etc. to keep extension handling consistent and
// discoverable. F# [<Literal>] values are usable in pattern matches.
// ============================================================

/// File extension constants grouped by category.
module FileExtensions =

    // ── Template / content extensions ──────────────────────

    /// F# HTML DSL template (Zest's native template format).
    [<Literal>]
    let ZestScript = ".zest.fsx"

    /// Generic F# script (treated as content when not a .zest.fsx).
    [<Literal>]
    let FSharpScript = ".fsx"

    /// Standard Markdown.
    [<Literal>]
    let Markdown = ".md"

    /// Long-form Markdown extension.
    [<Literal>]
    let MarkdownLong = ".markdown"

    /// Plain HTML (Nunjucks-preprocessed when template syntax detected).
    [<Literal>]
    let Html = ".html"

    /// HTML alternate extension.
    [<Literal>]
    let HtmlLong = ".htm"

    /// Nunjucks template.
    [<Literal>]
    let Nunjucks = ".njk"

    /// Liquid template (Jinja2 family).
    [<Literal>]
    let Liquid = ".liquid"

    /// Handlebars template (auto-converted to Nunjucks).
    [<Literal>]
    let Handlebars = ".hbs"

    /// Mustache template (auto-converted to Nunjucks).
    [<Literal>]
    let Mustache = ".mustache"

    /// WebC component (SSR-processed).
    [<Literal>]
    let WebC = ".webc"

    /// HAML template (auto-converted).
    [<Literal>]
    let Haml = ".haml"

    /// Pug template (auto-converted).
    [<Literal>]
    let Pug = ".pug"

    // ── Style extensions ───────────────────────────────────

    /// Zest Stylesheet (CSS superset with nesting/vars).
    [<Literal>]
    let Zcss = ".zcss"

    /// Plain CSS.
    [<Literal>]
    let Css = ".css"

    // ── Data / config extensions ───────────────────────────

    /// TOML (site config & global data).
    [<Literal>]
    let Toml = ".toml"

    /// YAML short form (alternate config format, used by migration tooling).
    [<Literal>]
    let Yaml = ".yml"

    /// YAML long-form extension.
    [<Literal>]
    let YamlLong = ".yaml"

    // ── Script / asset extensions ──────────────────────────

    /// Client-side JavaScript (copied as-is to output).
    [<Literal>]
    let JavaScript = ".js"

    // ── Image extensions ───────────────────────────────────

    [<Literal>]
    let Png = ".png"

    [<Literal>]
    let Jpg = ".jpg"

    [<Literal>]
    let Jpeg = ".jpeg"

    [<Literal>]
    let Svg = ".svg"

    [<Literal>]
    let Gif = ".gif"

    [<Literal>]
    let Webp = ".webp"

    // ── Aggregate sets ─────────────────────────────────────

    /// All Nunjucks-family template extensions (rendered via the Nunjucks
    /// engine, with optional syntax conversion). Used by ScriptEvaluator,
    /// MetaParser, ContentPipeline, LayoutEngine.
    let NunjucksFamily =
        [ Nunjucks; Liquid; Handlebars; Mustache; WebC; Haml; Pug ]

    /// All content extensions processed by the build pipeline.
    /// Includes native F# scripts, Markdown, HTML, and Nunjucks-family templates.
    let Content =
        [ ZestScript; FSharpScript; Markdown; MarkdownLong; Html ]
        @ NunjucksFamily

    /// All asset extensions (copied or lightly processed, not rendered as pages).
    let Assets =
        [ Zcss; Css; JavaScript; Png; Jpg; Jpeg; Svg; Gif; Webp ]

    /// <summary>Check if a path has one of the Nunjucks-family extensions.</summary>
    /// <param name="path">File path to check (case-insensitive).</param>
    let isNunjucksFamily (path: string) =
        NunjucksFamily
        |> List.exists (fun ext -> path.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase))

    /// <summary>Check if a path is a content/template file.</summary>
    /// <param name="path">File path to check (case-insensitive).</param>
    let isContent (path: string) =
        Content
        |> List.exists (fun ext -> path.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase))

    /// <summary>Check if a path is an asset file.</summary>
    /// <param name="path">File path to check (case-insensitive).</param>
    let isAsset (path: string) =
        Assets
        |> List.exists (fun ext -> path.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase))
