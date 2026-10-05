namespace Zest.Engine

// ============================================================
// FileExtensions — Central registry of file extensions
// ============================================================
// Single source of truth for all file extensions recognized by
// the Zest build pipeline. Replaces scattered hardcoded strings
// across TemplateCompat, LayoutEngine, ScriptEvaluator, etc.
//
// Usage: reference these constants instead of literal ".zlk" /
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

    /// Plain HTML (Zealucks-preprocessed when template syntax detected).
    [<Literal>]
    let Html = ".html"

    /// HTML alternate extension.
    [<Literal>]
    let HtmlLong = ".htm"

    /// Zealucks template. Zealucks is Zest's Zealucks-compatible template
    /// language; `.zlk` is its file extension (Ze-llucks).
    [<Literal>]
    let Zealucks = ".zlk"

    /// WebC component (SSR-processed, then rendered by the Zealucks engine).
    [<Literal>]
    let WebC = ".webc"

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

    /// All Zealucks-family template extensions (rendered by the Zealucks
    /// engine, with optional pre-processing). Used by ScriptEvaluator,
    /// MetaParser, ContentPipeline, LayoutEngine.
    let ZealucksFamily =
        [ Zealucks; WebC ]

    /// All content extensions processed by the build pipeline.
    /// Includes native F# scripts, Markdown, HTML, and Zealucks-family templates.
    let Content =
        [ ZestScript; FSharpScript; Markdown; MarkdownLong; Html ]
        @ ZealucksFamily

    /// All asset extensions (copied or lightly processed, not rendered as pages).
    let Assets =
        [ Zcss; Css; JavaScript; Png; Jpg; Jpeg; Svg; Gif; Webp ]

    /// <summary>Check if a path has one of the Zealucks-family extensions.</summary>
    /// <param name="path">File path to check (case-insensitive).</param>
    let isZealucksFamily (path: string) =
        ZealucksFamily
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
