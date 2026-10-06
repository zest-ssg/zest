namespace Zest.Compiler.Model
open System.IO

// ============================================================
// FileTypes — Central registry of file types
// ============================================================
// Single source of truth for all file extensions recognized by
// the Zest build pipeline. Replaces scattered hardcoded strings
// across FrontMatterParser, LayoutChain, PageEvaluator, etc.
//
// Usage: reference these constants instead of literal ".ztk" /
// ".zcss" / etc. to keep extension handling consistent and
// discoverable. F# [<Literal>] values are usable in pattern matches.
// ============================================================

/// File extension constants grouped by category.
module FileTypes =

    // ── Template / content extensions ──────────────────────

    /// Zest Page — the only F# script extension that receives a route.
    /// Files ending in this suffix are discovered as Zest pages.
    [<Literal>]
    let ZestScript = ".zest.fsx"

    /// Generic F# script. Never routed: discovery ignores plain `.fsx`
    /// entirely, so helper/build scripts can live beside pages.
    [<Literal>]
    let FSharpScript = ".fsx"

    /// Standard Markdown.
    [<Literal>]
    let Markdown = ".md"

    /// Long-form Markdown extension.
    [<Literal>]
    let MarkdownLong = ".markdown"

    /// Plain HTML (Zestucks-preprocessed when template syntax detected).
    [<Literal>]
    let Html = ".html"

    /// HTML alternate extension.
    [<Literal>]
    let HtmlLong = ".htm"

    /// Zestucks template. Zestucks is Zest's Nunjucks-compatible template
    /// language; `.ztk` is its file extension (Ze-stucks).
    [<Literal>]
    let Zestucks = ".ztk"

    /// Nunjucks-compatible counterpart to `.ztk`. Same engine, same syntax —
    /// files ported from Nunjucks keep working without being renamed.
    [<Literal>]
    let Nunjucks = ".njk"

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

    /// Every extension rendered by the Zestucks engine: the native `.ztk`
    /// and the Nunjucks-compatible `.njk`.
    /// Used by PageEvaluator, FrontMatterParser, PagePipeline, LayoutChain.
    let ZestucksFamily =
        [ Zestucks; Nunjucks ]

    /// Extensions Zestucks reads from disk, in lookup order. Used when a
    /// template refers to another one by bare name (`{% extends "base" %}`).
    let ZestucksFileExtensions =
        [ Zestucks; Nunjucks ]

    /// All content extensions processed by the build pipeline.
    /// Includes Zest Pages, Markdown, HTML, and Zestucks-family templates.
    /// Plain `.fsx` is deliberately absent — those scripts are not content.
    let Content =
        [ ZestScript; Markdown; MarkdownLong; Html ]
        @ ZestucksFamily

    /// All asset extensions (copied or lightly processed, not rendered as pages).
    let Assets =
        [ Zcss; Css; JavaScript; Png; Jpg; Jpeg; Svg; Gif; Webp ]

    /// <summary>Check if a path has one of the Zestucks-family extensions.</summary>
    /// <param name="path">File path to check (case-insensitive).</param>
    let isZestucksFamily (path: string) =
        ZestucksFamily
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

    // ── Routing guards ──────────────────────────────────────

    /// Site configuration file name (project root).
    [<Literal>]
    let ConfigFileName = "_config.toml"

    /// Pre-build script file name (project root, exact match).
    [<Literal>]
    let PrebuildScriptFileName = "_prebuild.fsx"

    /// Post-build script file name (project root, exact match).
    [<Literal>]
    let FinalizeScriptFileName = "_finalize.fsx"

    /// The only three special files Zest recognises. All live at the project
    /// root, are read directly by the CLI/build engine, and must never be
    /// discovered, evaluated, or routed as pages.
    ///
    ///   `_config.toml`   — declarative site configuration
    ///   `_prebuild.fsx`  — dynamic pre-build script (global data, hooks)
    ///   `_finalize.fsx`  — dynamic post-build script (validate, index)
    ///
    /// There is no other reserved name and no compatibility alias.
    let ReservedFileNames = set [ ConfigFileName; PrebuildScriptFileName; FinalizeScriptFileName ]

    /// <summary>True when the file is a Zest Page (<c>*.zest.fsx</c>).</summary>
    /// <remarks>
    /// Matches the whole file name rather than <c>Path.GetExtension</c>, because
    /// the extension of "page.zest.fsx" is ".fsx" — an extension-only test
    /// cannot tell a Zest page from an ordinary F# script.
    /// </remarks>
    let isZestPage (path: string) =
        not (isNull path) &&
        path.EndsWith(ZestScript, System.StringComparison.OrdinalIgnoreCase)

    /// <summary>True when the file is an ordinary F# script (<c>*.fsx</c>),
    /// i.e. not a Zest Page. These files get no route.</summary>
    let isPlainFSharpScript (path: string) =
        not (isNull path) &&
        path.EndsWith(FSharpScript, System.StringComparison.OrdinalIgnoreCase) &&
        not (isZestPage path)

    /// <summary>True when the file is one of the two reserved special files
    /// (<c>_config.toml</c> or <c>_prebuild.fsx</c>). Never routed.</summary>
    let isReservedFile (path: string) =
        not (isNull path) &&
        ReservedFileNames.Contains(Path.GetFileName(path).ToLowerInvariant())
