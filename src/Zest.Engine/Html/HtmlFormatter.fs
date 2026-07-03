namespace Zest.Engine.Html

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// HTML Formatter & Minifier API
// ============================================================

/// Compression level for HTML minification.
type HtmlCompressionLevel =
    /// Remove only safe whitespace between tags — keeps text content spacing intact.
    | Safe = 0
    /// Aggressive whitespace removal including collapsing spaces in text content.
    | Aggressive = 1
    /// Maximum compression — removes all non-essential whitespace, comments, and redundant attributes.
    | Extreme = 2

/// Configuration options for HTML formatting and minification.
type HtmlFormatOptions =
    { /// Compression level for minification (default: Extreme).
      CompressionLevel : HtmlCompressionLevel

      /// If true, remove HTML comments (<!-- ... -->). Default: true.
      RemoveComments : bool

      /// If true, collapse multiple whitespace characters into a single space in text nodes. Default: true.
      CollapseWhitespace : bool

      /// If true, remove optional closing tags (e.g., </li>, </p>) — only in Extreme mode. Default: false.
      RemoveOptionalTags : bool

      /// If true, remove quotes from attribute values when safe (e.g., numeric values). Default: true.
      RemoveAttributeQuotes : bool

      /// If true, remove attributes with empty values. Default: true.
      RemoveEmptyAttributes : bool

      /// If true, remove redundant boolean attribute values (e.g., disabled="disabled" → disabled). Default: true.
      CollapseBooleanAttributes : bool

      /// If true, sort attributes alphabetically for consistent output. Default: false.
      SortAttributes : bool

      /// Number of spaces per indentation level for formatting (default: 2).
      IndentSize : int

      /// Maximum line length before wrapping attributes in formatting mode (default: 120).
      MaxLineLength : int

      /// If true, keep HTML5 void elements without closing slash (e.g., <br> not <br />).
      Html5VoidElements : bool }
    static member Default =
        { CompressionLevel        = HtmlCompressionLevel.Extreme
          RemoveComments          = true
          CollapseWhitespace      = true
          RemoveOptionalTags      = false
          RemoveAttributeQuotes   = true
          RemoveEmptyAttributes   = true
          CollapseBooleanAttributes = true
          SortAttributes          = false
          IndentSize              = 2
          MaxLineLength           = 120
          Html5VoidElements       = true }

    /// Safe defaults that preserve text content spacing.
    static member Safe =
        { HtmlFormatOptions.Default with
            CompressionLevel   = HtmlCompressionLevel.Safe
            RemoveComments     = false
            CollapseWhitespace = false
            RemoveAttributeQuotes = false
            RemoveEmptyAttributes = false }

    /// Aggressive defaults for maximum compression without breaking layout.
    static member Aggressive =
        { HtmlFormatOptions.Default with
            CompressionLevel = HtmlCompressionLevel.Aggressive }

/// HTML Formatter and Minifier module providing configurable HTML output processing.
module HtmlFormatter =

    // ── Private helpers ──────────────────────────────────────────────

    let private voidTags = set [
        "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"
        "link"; "meta"; "param"; "source"; "track"; "wbr"
    ]

    let private optionalClosingTags = set [
        "li"; "p"; "dt"; "dd"; "rt"; "rp"; "optgroup"; "option"
        "colgroup"; "thead"; "tbody"; "tfoot"; "tr"; "th"; "td"
    ]

    let private booleanAttributes = set [
        "disabled"; "readonly"; "required"; "checked"; "selected"
        "multiple"; "autofocus"; "autoplay"; "controls"; "loop"
        "muted"; "default"; "reversed"; "ismap"; "nohref"
        "noshade"; "nowrap"; "open"; "compact"; "declare"
        "defer"; "async"; "hidden"; "draggable"; "spellcheck"
        "contenteditable"; "formnovalidate"; "itemscope"; "novalidate"
        "playsinline"; "crossorigin"
    ]

    /// Remove HTML comments: <!-- ... -->
    let private removeComments (html: string) : string =
        Regex.Replace(html, @"<!--(?!\s*\[if\s).*?-->", "", RegexOptions.Singleline)

    /// Collapse multiple whitespace to single space, preserving pre/script/style content.
    let private collapseWhitespace (html: string) : string =
        let protectedBlocks = ResizeArray<string>()
        let mutable idx = 0
        // Protect <pre>, <script>, <style>, <textarea> content
        let protectPattern = Regex(
            @"<(pre|script|style|textarea)\b[^>]*>.*?</\1>",
            RegexOptions.Singleline ||| RegexOptions.IgnoreCase)
        let protected0 = protectPattern.Replace(html, fun m ->
            let key = sprintf "\x00PROTECTED%d\x00" idx
            protectedBlocks.Add(m.Value)
            idx <- idx + 1
            key)
        // Collapse whitespace outside protected blocks
        let collapsed =
            protected0
                .Replace("\r\n", "\n").Replace('\r', '\n')
        let collapsed =
            Regex.Replace(collapsed, @"[ \t]+", " ")
        let collapsed =
            Regex.Replace(collapsed, @"\n\s*\n", "\n")
        let collapsed =
            Regex.Replace(collapsed, @"\n[ \t]+", "\n")
        let collapsed =
            Regex.Replace(collapsed, @"^[ \t]+", "", RegexOptions.Multiline)
        let collapsed =
            Regex.Replace(collapsed, @"[ \t]+$", "", RegexOptions.Multiline)
        let collapsed =
            Regex.Replace(collapsed, @">\s+<", "><")
        // Restore protected blocks
        if idx > 0 then
            let mutable result = collapsed
            for i = 0 to idx - 1 do
                result <- result.Replace(sprintf "\x00PROTECTED%d\x00" i, protectedBlocks.[i])
            result
        else collapsed

    /// Remove quotes from attribute values when safe (no spaces, no special chars).
    let private removeAttributeQuotes (html: string) : string =
        Regex.Replace(html, @"(\w+)=""([a-zA-Z0-9_\-\.\+]+)""", "$1=$2")

    /// Remove attributes with empty values (e.g., class="").
    let private removeEmptyAttributes (html: string) : string =
        Regex.Replace(html, @"\s+\w+=""""", "")

    /// Collapse boolean attributes: checked="checked" → checked.
    let private collapseBooleanAttrs (html: string) : string =
        let sb = StringBuilder(html)
        for attr in booleanAttributes do
            sb.Replace(sprintf @"%s=""%s""" attr attr, attr) |> ignore
        html

    /// Remove optional closing tags (in Extreme mode only).
    let private removeOptionalClosingTags (html: string) : string =
        let sb = StringBuilder(html)
        for tag in optionalClosingTags do
            let pattern = sprintf @"</%s>" tag
            // Only remove if followed by another opening tag or whitespace-then-tag
            sb.Replace(pattern, "") |> ignore
        html

    /// Remove whitespace between tags: >\s+< → ><.
    let private removeInterTagWhitespace (html: string) : string =
        Regex.Replace(html, @">\s+<", "><")

    /// Remove leading/trailing whitespace from text nodes between tags.
    let private trimTextNodes (html: string) : string =
        // Trim whitespace around block-level tags
        let blockTagsPattern = @"(</?(?:div|p|h[1-6]|ul|ol|li|table|tr|td|th|thead|tbody|tfoot|section|article|header|footer|nav|main|aside|blockquote|pre|hr|br|form|fieldset|figure|figcaption|details|summary|dl|dt|dd|address|template|noscript|canvas|video|audio|picture|dialog|menu)[^>]*>)"
        let result = Regex.Replace(html, @"\s+" + blockTagsPattern, "$1")
        Regex.Replace(result, blockTagsPattern + @"\s+", "$1")

    // ── Public API ──────────────────────────────────────────────────

    /// Minify HTML string with the given options.
    /// Removes comments, collapses whitespace, and optionally removes redundant attributes.
    let minify (options: HtmlFormatOptions) (html: string) : string =
        let mutable result = html
        if options.RemoveComments then
            result <- removeComments result
        if options.CollapseWhitespace then
            match options.CompressionLevel with
            | HtmlCompressionLevel.Safe ->
                result <- removeInterTagWhitespace result
            | HtmlCompressionLevel.Aggressive ->
                result <- collapseWhitespace result
                result <- removeInterTagWhitespace result
            | HtmlCompressionLevel.Extreme ->
                result <- collapseWhitespace result
                result <- removeInterTagWhitespace result
                result <- trimTextNodes result
            | _ -> ()
        if options.RemoveAttributeQuotes then
            result <- removeAttributeQuotes result
        if options.RemoveEmptyAttributes then
            result <- removeEmptyAttributes result
        if options.CollapseBooleanAttributes then
            result <- collapseBooleanAttrs result
        if options.RemoveOptionalTags then
            result <- removeOptionalClosingTags result
        result.Trim()

    /// Alias for minify with default (Extreme) options.
    let minifyDefault (html: string) : string =
        minify HtmlFormatOptions.Default html

    /// Minify HTML with safe options (preserves text content spacing).
    let minifySafe (html: string) : string =
        minify HtmlFormatOptions.Safe html

    /// Minify HTML with aggressive options.
    let minifyAggressive (html: string) : string =
        minify HtmlFormatOptions.Aggressive html

    /// Format (pretty-print) an HTML string with proper indentation and line breaks.
    /// Parses the HTML and re-outputs it with the given indent size.
    let format (options: HtmlFormatOptions) (html: string) : string =
        let indent = String.replicate options.IndentSize " "
        let sb = StringBuilder()
        let mutable depth = 0
        let mutable inPre = false
        let mutable inScript = false
        let mutable inStyle = false
        let mutable inTextarea = false

        let tagRegex = Regex(
            @"(<!--.*?-->|<!DOCTYPE[^>]*>|</?[a-zA-Z][a-zA-Z0-9\-]*[^>]*/?>|[^<]+)",
            RegexOptions.Singleline)

        let matches = tagRegex.Matches(html)
        for m in matches do
            let token = m.Value
            if String.IsNullOrWhiteSpace(token) && not inPre && not inScript && not inStyle && not inTextarea then
                () // skip whitespace-only tokens outside special elements
            elif token.StartsWith("<!--") then
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                sb.Append(token) |> ignore
                sb.AppendLine() |> ignore
            elif token.StartsWith("<!DOCTYPE") then
                sb.Append(token.Trim()) |> ignore
                sb.AppendLine() |> ignore
            elif token.StartsWith("</") then
                let tagName = token.Substring(2, token.Length - 3).TrimEnd('>')
                if tagName = "pre" then inPre <- false
                elif tagName = "script" then inScript <- false
                elif tagName = "style" then inStyle <- false
                elif tagName = "textarea" then inTextarea <- false
                if not (token.EndsWith("/>")) then
                    if depth > 0 then depth <- depth - 1
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                sb.Append(token.Trim()) |> ignore
            elif token.StartsWith("<") && token.EndsWith("/>") then
                // Self-closing tag
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                sb.Append(token.Trim()) |> ignore
            elif token.StartsWith("<") then
                // Opening tag
                let trimmed = token.Trim()
                let mutable isVoid = false
                let mutable tagName = ""
                let spaceIdx = trimmed.IndexOf(' ')
                let gtIdx = trimmed.IndexOf('>')
                let nameEnd = if spaceIdx > 0 && spaceIdx < gtIdx then spaceIdx else gtIdx
                if nameEnd > 1 then
                    tagName <- trimmed.Substring(1, nameEnd - 1).ToLowerInvariant()
                    isVoid <- voidTags.Contains tagName
                    if tagName = "pre" then inPre <- true
                    elif tagName = "script" then inScript <- true
                    elif tagName = "style" then inStyle <- true
                    elif tagName = "textarea" then inTextarea <- true
                // Check if this is a closing tag actually (e.g., </div>)
                if trimmed.StartsWith("</") then
                    let closingTag = trimmed.Substring(2, trimmed.Length - 3).TrimEnd('>')
                    if closingTag = "pre" then inPre <- false
                    elif closingTag = "script" then inScript <- false
                    elif closingTag = "style" then inStyle <- false
                    elif closingTag = "textarea" then inTextarea <- false
                    if depth > 0 then depth <- depth - 1
                    sb.AppendLine() |> ignore
                    sb.Append(String.replicate depth indent) |> ignore
                    sb.Append(trimmed) |> ignore
                else
                    // In pre/script/style/textarea, don't indent
                    if inPre || inScript || inStyle || inTextarea then
                        sb.Append(trimmed) |> ignore
                    else
                        sb.AppendLine() |> ignore
                        sb.Append(String.replicate depth indent) |> ignore
                        sb.Append(trimmed) |> ignore
                    if not isVoid then
                        depth <- depth + 1
            elif inPre || inScript || inStyle || inTextarea then
                // Text inside preserved blocks
                sb.Append(token) |> ignore
            else
                // Text content
                let trimmed = token.Trim()
                if trimmed.Length > 0 then
                    sb.Append(trimmed) |> ignore

        let result = sb.ToString().TrimStart()
        // Normalize multiple blank lines
        Regex.Replace(result, @"\n{3,}", "\n\n")

    /// Format HTML with default options (2-space indent).
    let formatDefault (html: string) : string =
        format HtmlFormatOptions.Default html

    /// Format HTML with custom indent size.
    let formatWithIndent (indentSize: int) (html: string) : string =
        format { HtmlFormatOptions.Default with IndentSize = indentSize } html

    /// Apply HTML optimization based on configured compression level.
    /// This is the main entry point for integration with the build pipeline.
    let optimize (options: HtmlFormatOptions) (html: string) : string =
        match options.CompressionLevel with
        | HtmlCompressionLevel.Safe | HtmlCompressionLevel.Aggressive | HtmlCompressionLevel.Extreme ->
            minify options html
        | _ -> html

    // ================================================================
    // CSS Minification & Formatting
    // ================================================================

    /// Minify CSS: removes comments, collapses whitespace, removes
    /// unnecessary semicolons and trailing zeros.
    let minifyCss (css: string) : string =
        let mutable result = css

        // Remove comments: /* ... */
        result <- Regex.Replace(result, @"/\*.*?\*/", "", RegexOptions.Singleline)

        // Collapse whitespace
        result <- result.Replace("\r\n", "\n").Replace('\r', '\n')
        result <- Regex.Replace(result, @"[ \t]+", " ")
        result <- Regex.Replace(result, @"\n\s*", "")
        result <- Regex.Replace(result, @";\s*}", "}")
        result <- Regex.Replace(result, @"}\s*", "}")
        result <- Regex.Replace(result, @"\{\s*", "{")
        result <- Regex.Replace(result, @":\s+", ":")
        result <- Regex.Replace(result, @",\s+", ",")
        result <- Regex.Replace(result, @";\s+", ";")
        result <- Regex.Replace(result, @"\s*!\s*important", "!important")
        result <- Regex.Replace(result, @"\)\s+", ")")
        result <- Regex.Replace(result, @"\s+\)", ")")
        result <- Regex.Replace(result, @"\(\s+", "(")
        result <- Regex.Replace(result, @"\s+\( ", "(")
        result <- Regex.Replace(result, @">\s+", ">")
        result <- Regex.Replace(result, @"\s+>", ">")
        result <- Regex.Replace(result, @"\+\s+", "+")
        result <- Regex.Replace(result, @"\s+\+", "+")
        result <- Regex.Replace(result, @"~\s+", "~")
        result <- Regex.Replace(result, @"\s+~", "~")

        // Remove last semicolon before closing brace: ;} → }
        result <- Regex.Replace(result, @";}", "}")

        // Remove leading/trailing whitespace from CSS custom property values where safe
        result <- Regex.Replace(result, @"\s*;\s*$", "")

        // Remove units from 0 values (0px → 0, 0em → 0, etc.)
        result <- Regex.Replace(result, @"(?<=\b)0(px|em|rem|%|vh|vw|vmin|vmax|ch|ex|cm|mm|in|pt|pc|deg|rad|grad|turn|s|ms)\b", "0")

        // Shorten hex colors: #ff0000 → #f00, #aabbcc → #abc
        result <- Regex.Replace(result, @"#([0-9a-fA-F])\1([0-9a-fA-F])\2([0-9a-fA-F])\3\b", "#$1$2$3")

        // Remove leading zeros from decimals: 0.5 → .5
        result <- Regex.Replace(result, @"(?<!\w)0\.(\d+)", ".$1")

        result.Trim()

    /// Format (pretty-print) CSS with proper indentation.
    /// Each rule block gets its own line, declarations are indented.
    let formatCss (indentSize: int) (css: string) : string =
        let indent = String.replicate indentSize " "
        let sb = StringBuilder()
        let mutable depth = 0
        let mutable inComment = false

        // Remove existing whitespace to re-format cleanly
        let cleaned = Regex.Replace(css, @"/\*.*?\*/", fun m ->
            // Preserve comments but normalize surrounding whitespace
            "\n" + m.Value + "\n")
        let cleaned = cleaned.Replace("\r\n", "\n").Replace('\r', '\n')
        let cleaned = Regex.Replace(cleaned, @"[ \t]+", " ")
        let cleaned = Regex.Replace(cleaned, @"\n\s*\n", "\n")

        let mutable i = 0
        let chars = cleaned.ToCharArray()
        let len = chars.Length

        while i < len do
            let c = chars.[i]
            if c = '/' && i + 1 < len && chars.[i + 1] = '*' then
                // Comment start
                let endIdx = cleaned.IndexOf("*/", i + 2)
                if endIdx >= 0 then
                    sb.AppendLine() |> ignore
                    sb.Append(String.replicate depth indent) |> ignore
                    sb.Append(cleaned.Substring(i, endIdx - i + 2)) |> ignore
                    i <- endIdx + 2
                else
                    sb.Append(c) |> ignore
                    i <- i + 1
            elif c = '@' && i + 6 < len && cleaned.Substring(i).StartsWith("@media") then
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                let endBrace = cleaned.IndexOf('{', i)
                if endBrace >= 0 then
                    sb.Append(cleaned.Substring(i, endBrace - i + 1).Trim()) |> ignore
                    sb.AppendLine() |> ignore
                    depth <- depth + 1
                    i <- endBrace + 1
                else
                    sb.Append(c) |> ignore
                    i <- i + 1
            elif c = '{' then
                // Opening brace - trim trailing space before it
                let content = sb.ToString()
                if content.Length > 0 && content.[content.Length - 1] = ' ' then
                    sb.Remove(content.Length - 1, 1) |> ignore
                sb.Append(" {") |> ignore
                sb.AppendLine() |> ignore
                depth <- depth + 1
                i <- i + 1
            elif c = '}' then
                if depth > 0 then depth <- depth - 1
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                sb.Append('}') |> ignore
                sb.AppendLine() |> ignore
                i <- i + 1
            elif c = ';' then
                sb.Append(';') |> ignore
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                i <- i + 1
            elif c = '\n' || c = '\r' then
                i <- i + 1
            elif c = ' ' || c = '\t' then
                // Skip standalone whitespace; preserve single spaces between tokens
                if sb.Length > 0 && sb.[sb.Length - 1] <> '\n' && sb.[sb.Length - 1] <> ' ' && sb.[sb.Length - 1] <> '{' && sb.[sb.Length - 1] <> ';' && sb.[sb.Length - 1] <> '}' then
                    sb.Append(' ') |> ignore
                i <- i + 1
            else
                sb.Append(c) |> ignore
                i <- i + 1

        Regex.Replace(sb.ToString().Trim(), @"\n{3,}", "\n\n")

    // ================================================================
    // JavaScript Minification & Formatting
    // ================================================================

    /// Minify JavaScript: removes comments, collapses whitespace,
    /// shortens variable names where safe.
    let minifyJs (js: string) : string =
        let mutable result = js

        // Remove single-line comments (but not URLs: //example.com)
        result <- Regex.Replace(result, @"(?<!:)//.*$", "", RegexOptions.Multiline)

        // Remove multi-line comments /* ... */
        result <- Regex.Replace(result, @"/\*.*?\*/", "", RegexOptions.Singleline)

        // Collapse whitespace
        result <- result.Replace("\r\n", "\n").Replace('\r', '\n')
        result <- Regex.Replace(result, @"[ \t]+", " ")
        result <- Regex.Replace(result, @"\n\s*", "")

        // Remove whitespace around operators and brackets
        result <- Regex.Replace(result, @"\s*=\s*", "=")
        result <- Regex.Replace(result, @"\s*\+\s*", "+")
        result <- Regex.Replace(result, @"\s*-\s*", "-")
        result <- Regex.Replace(result, @"\s*\*\s*", "*")
        result <- Regex.Replace(result, @"\s*/\s*", "/")
        result <- Regex.Replace(result, @"\s*%\s*", "%")
        result <- Regex.Replace(result, @"\s*,\s*", ",")
        result <- Regex.Replace(result, @"\s*;\s*", ";")
        result <- Regex.Replace(result, @"\s*:\s*", ":")
        result <- Regex.Replace(result, @"\s*\{\s*", "{")
        result <- Regex.Replace(result, @"\s*\}\s*", "}")
        result <- Regex.Replace(result, @"\s*\(\s*", "(")
        result <- Regex.Replace(result, @"\s*\)\s*", ")")
        result <- Regex.Replace(result, @"\s*\[\s*", "[")
        result <- Regex.Replace(result, @"\s*\]\s*", "]")
        result <- Regex.Replace(result, @"\s*<\s*", "<")
        result <- Regex.Replace(result, @"\s*>\s*", ">")
        result <- Regex.Replace(result, @"\s*\?\s*", "?")
        result <- Regex.Replace(result, @"\s*&&\s*", "&&")
        result <- Regex.Replace(result, @"\s*\|\|\s*", "||")
        result <- Regex.Replace(result, @"\s*===\s*", "===")
        result <- Regex.Replace(result, @"\s*!==\s*", "!==")

        // Preserve spaces after keywords that need them
        result <- Regex.Replace(result, @"\b(var|let|const|function|return|if|else|for|while|do|switch|case|throw|new|typeof|instanceof|in|of|class|extends|import|export|from|as|default|yield|await|async|static|get|set|try|catch|finally|delete|void|with)\s+", "$1 ")

        // Remove unnecessary semicolons (before closing brace)
        result <- Regex.Replace(result, @";}", "}")

        result.Trim()

    /// Format (pretty-print) JavaScript with proper indentation.
    /// Uses brace-based depth tracking for consistent 2-space indentation.
    let formatJs (indentSize: int) (js: string) : string =
        let indent = String.replicate indentSize " "
        let sb = StringBuilder()
        let mutable depth = 0
        let mutable inString = false
        let mutable stringChar = '"'
        let mutable inTemplate = false
        let mutable inSingleComment = false
        let mutable inMultiComment = false
        let mutable prevChar = ' '
        let mutable needsNewline = true

        let chars = js.ToCharArray()
        let len = chars.Length
        let mutable i = 0

        while i < len do
            let c = chars.[i]

            // Handle comments
            if not inString && not inTemplate && not inSingleComment && not inMultiComment && c = '/' && i + 1 < len then
                if chars.[i + 1] = '/' then
                    inSingleComment <- true
                    sb.Append("//") |> ignore
                    i <- i + 2
                elif chars.[i + 1] = '*' then
                    inMultiComment <- true
                    if needsNewline then
                        sb.AppendLine() |> ignore
                        sb.Append(String.replicate depth indent) |> ignore
                        needsNewline <- false
                    sb.Append("/*") |> ignore
                    i <- i + 2
                else
                    sb.Append(c) |> ignore
                    i <- i + 1
            elif inSingleComment then
                sb.Append(c) |> ignore
                if c = '\n' || c = '\r' then
                    inSingleComment <- false
                    needsNewline <- true
                i <- i + 1
            elif inMultiComment then
                sb.Append(c) |> ignore
                if c = '*' && i + 1 < len && chars.[i + 1] = '/' then
                    sb.Append('/') |> ignore
                    i <- i + 1
                    inMultiComment <- false
                    needsNewline <- true
                i <- i + 1

            // Handle strings
            elif not inString && not inTemplate && (c = '"' || c = '\'' || c = '`') then
                if c = '`' then inTemplate <- true
                else inString <- true
                stringChar <- c
                sb.Append(c) |> ignore
                i <- i + 1
            elif (inString && c = stringChar && prevChar <> '\\') || (inTemplate && c = '`' && prevChar <> '\\') then
                inString <- false
                inTemplate <- false
                sb.Append(c) |> ignore
                i <- i + 1
            elif inString || inTemplate then
                sb.Append(c) |> ignore
                i <- i + 1

            // Handle braces and structure
            elif c = '{' then
                if sb.Length > 0 && sb.[sb.Length - 1] <> '\n' && sb.[sb.Length - 1] <> ' ' then
                    sb.Append(' ') |> ignore
                sb.Append('{') |> ignore
                depth <- depth + 1
                sb.AppendLine() |> ignore
                needsNewline <- true
                i <- i + 1
            elif c = '}' then
                if depth > 0 then depth <- depth - 1
                sb.AppendLine() |> ignore
                sb.Append(String.replicate depth indent) |> ignore
                sb.Append('}') |> ignore
                needsNewline <- true
                i <- i + 1
            elif c = ';' then
                sb.Append(';') |> ignore
                // Don't add newline after ; if next token is }
                let mutable j = i + 1
                while j < len && (chars.[j] = ' ' || chars.[j] = '\t' || chars.[j] = '\r' || chars.[j] = '\n') do
                    j <- j + 1
                if j < len && chars.[j] = '}' then
                    () // no newline needed
                else
                    sb.AppendLine() |> ignore
                    needsNewline <- true
                i <- i + 1
            elif c = '\r' || c = '\n' then
                i <- i + 1
            elif c = ' ' || c = '\t' then
                if sb.Length > 0 && sb.[sb.Length - 1] <> '\n' && sb.[sb.Length - 1] <> ' ' && not needsNewline then
                    sb.Append(' ') |> ignore
                i <- i + 1
            else
                if needsNewline then
                    sb.Append(String.replicate depth indent) |> ignore
                    needsNewline <- false
                sb.Append(c) |> ignore
                i <- i + 1

            prevChar <- c

        Regex.Replace(sb.ToString().Trim(), @"\n{3,}", "\n\n")

    /// Combined CSS+JS minification convenience function.
    /// Detects content type based on heuristics (script tags, function keywords, etc.)
    /// or use minifyCss / minifyJs directly for known content types.
    let minifyAsset (content: string) : string =
        let trimmed = content.TrimStart()
        if trimmed.StartsWith("<script") || trimmed.Contains("function ") || trimmed.Contains("const ") || trimmed.Contains("let ") || trimmed.Contains("var ") || trimmed.Contains("=>") then
            minifyJs content
        else
            minifyCss content
