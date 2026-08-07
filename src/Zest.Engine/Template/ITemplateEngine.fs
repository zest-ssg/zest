namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.IO

// ============================================================
// ITemplateEngine — Generic template engine abstraction
// ============================================================

/// Error that occurred during template processing.
type TemplateError =
    | ParseError   of message: string * line: int * col: int
    | RuntimeError of message: string * line: int
    | NotFound     of name: string
    | UnknownFilter of name: string
    | UnknownTag    of name: string
    | IncludeLoop   of name: string
with
    override this.ToString() =
        match this with
        | ParseError(msg, line, col)   -> sprintf "[Template Parse] %s at line %d:%d" msg line col
        | RuntimeError(msg, line)      -> sprintf "[Template Runtime] %s at line %d" msg line
        | NotFound(name)               -> sprintf "[Template] Template '%s' not found" name
        | UnknownFilter(name)          -> sprintf "[Template] Unknown filter '%s'" name
        | UnknownTag(name)             -> sprintf "[Template] Unknown tag '%s'" name
        | IncludeLoop(name)            -> sprintf "[Template] Circular include detected: '%s'" name

/// Filter function signature: receives value and string args, returns new value.
type FilterFn = obj -> string list -> obj

/// Tag handler for custom tags.
type TagHandler = {
    TagName: string
    ParseArgs: string -> Result<string list, string>
    Execute: string list -> IDictionary<string, obj> -> Result<string, string>
}

/// Unified template engine interface.
/// Every engine must implement these members.
type ITemplateEngine =
    /// Engine name/identifier.
    abstract Name: string

    /// Render a template string with the given variables.
    abstract Render: templateText: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// Render a template file with the given variables.
    abstract RenderFile: filePath: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// Register a custom filter.
    abstract RegisterFilter: name: string -> fn: FilterFn -> unit

    /// Register a custom tag.
    abstract RegisterTag: handler: TagHandler -> unit

    /// Clear all cached templates.
    abstract ClearCache: unit -> unit

// ============================================================
// TemplateUtils — Shared helpers for template converters
// ============================================================

/// Shared utilities used by the Haml/Pug/Handlebars converters and the
/// template manager. Kept here (the first Template/ file) so every converter
/// can reference it without extra coupling.
module TemplateUtils =

    /// HTML-escape text content: & < > " → entities.
    let htmlEncode (s: string) =
        if isNull s then "" else
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")

    /// Escape for use inside a double-quoted attribute value.
    /// Also escapes single quotes for safety in single-quoted contexts.
    let attrEncode (s: string) =
        (htmlEncode s).Replace("'", "&#39;")

    /// HTML5 void elements (self-closing — no end tag).
    let voidElements = set ["area"; "base"; "br"; "col"; "embed"; "hr"; "img";
                            "input"; "link"; "meta"; "param"; "source"; "track"; "wbr"]

    /// Check whether a tag name is a void element.
    let isVoidElement (tag: string) = voidElements.Contains(tag.ToLowerInvariant())

    /// Strict indentation level for the Pug/Haml converters: one tab or two
    /// spaces per level. Returns None when leading whitespace mixes tabs with
    /// spaces, or uses an odd space width — the converter refuses to guess a
    /// nesting level in those cases and reports the line instead, because a
    /// wrong guess silently produces broken HTML.
    let indentLevelStrict (line: string) : int option =
        let mutable spaces = 0
        let mutable tabs = 0
        let mutable i = 0
        let n = line.Length
        while i < n && (line.[i] = ' ' || line.[i] = '\t') do
            if line.[i] = ' ' then spaces <- spaces + 1 else tabs <- tabs + 1
            i <- i + 1
        if spaces > 0 && tabs > 0 then None
        elif spaces > 0 && spaces % 2 <> 0 then None
        else Some (if tabs > 0 then tabs else spaces / 2)

    /// Detect whether a document mixes tab and space indentation across lines.
    /// Pug/Haml require a single indent style; mixing is reported by the
    /// converters so the mistake surfaces instead of silently nesting wrong.
    let mixedIndentStyles (lines: string array) : bool =
        let mutable hasSpaces = false
        let mutable hasTabs = false
        for line in lines do
            if not (String.IsNullOrWhiteSpace line) then
                let mutable i = 0
                let mutable sawSpace = false
                let mutable sawTab = false
                while i < line.Length && (line.[i] = ' ' || line.[i] = '\t') do
                    if line.[i] = ' ' then sawSpace <- true else sawTab <- true
                    i <- i + 1
                hasSpaces <- hasSpaces || sawSpace
                hasTabs <- hasTabs || sawTab
        hasSpaces && hasTabs

    /// Resolve a template-relative path against the working directory and
    /// reject paths that escape it (path-traversal guard for {% include %},
    /// {% extends %}, and partial loaders). A leading '/' or '\' means
    /// "site-root-relative" in template syntax, so it resolves against the
    /// working directory rather than the drive root.
    let resolveWithinRoot (path: string) : Result<string, string> =
        let cwd = Directory.GetCurrentDirectory()
        let root = Path.GetFullPath(cwd)
        let trimmed = path.Trim().TrimStart('/', '\\')
        let full = Path.GetFullPath(Path.Combine(cwd, trimmed))
        if full.Equals(root, StringComparison.OrdinalIgnoreCase)
           || full.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar.ToString(),
                              StringComparison.OrdinalIgnoreCase) then
            Ok full
        else
            Error(sprintf "Template path escapes the site root: %s" path)

    // ── Conversion result cache ────────────────────────────────
    // Converter output is pure (source → HTML/Nunjucks). Caching by content
    // hash avoids re-running regex-heavy conversions on dev-server rebuilds
    // when the source file hasn't changed.
    let private conversionCache = System.Collections.Concurrent.ConcurrentDictionary<int64, string>()

    /// Stable FNV-1a 64-bit hash (process-local cache, not cryptographic).
    let hashSource (s: string) : int64 =
        let mutable h = 0xcbf29ce484222325UL
        for c in s do
            h <- h ^^^ (uint64 c)
            h <- h * 0x100000001b3UL
        int64 h

    /// Get a cached conversion result, or compute+cache it.
    let cachedConvert (source: string) (convert: string -> string) : string =
        if isNull source || source = "" then "" else
        let key = hashSource source
        match conversionCache.TryGetValue key with
        | true, cached -> cached
        | _ ->
            let result = convert source
            conversionCache.[key] <- result
            result

    /// Clear the conversion cache (called on full rebuild / cache clear).
    let clearConversionCache () = conversionCache.Clear()
