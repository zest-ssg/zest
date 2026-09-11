namespace Zest.Engine.Zcss

open System
open System.Text.RegularExpressions

// ============================================================
// ZCSS Parser — Main entry point
// ============================================================

module Parser =

    open CoreParser
    open System.Collections.Concurrent

    /// Parse result cache for improved performance.
    /// Maps from source hash + mode to parsed AST nodes.
    let private parseCache = ConcurrentDictionary<string, ZcssNode list>()
    
    /// Simple hash function for cache keys.
    let private hashSource (source: string) : string =
        if String.IsNullOrWhiteSpace source then ""
        else
            use sha1 = System.Security.Cryptography.SHA1.Create()
            let bytes = System.Text.Encoding.UTF8.GetBytes(source)
            let hashBytes = sha1.ComputeHash(bytes)
            System.BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant()

    /// Parses ZCSS source text into an abstract syntax tree (AST).
    /// Supports three syntax modes automatically detected from the source:
    /// - BraceMode: CSS/SCSS-style with braces {}
    /// - IndentMode: Python-style with indentation
    /// - BracketMode: F#-style with brackets []
    /// Returns a list of ZcssNode representing the parsed structure.
    /// Uses caching to avoid re-parsing identical sources.
    let parse (source: string) : ZcssNode list =
        if String.IsNullOrWhiteSpace source then []
        else
            let sourceHash = hashSource source
            // Try to get from cache
            match parseCache.TryGetValue(sourceHash) with
            | true, cachedResult -> cachedResult
            | false, _ ->
                clearErrors()
                let cleaned = stripComments source
                let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
                let vars = extractVars lines
                let mode = detectMode lines
                let result =
                    match mode with
                    | BraceMode ->
                        let res, _ = BraceParser.parseBraceBlock 0 lines vars
                        res
                    | IndentMode ->
                        let res, _ = IndentParser.parseIndentBlock 0 lines 0 vars
                        res
                    | BracketMode ->
                        // F#-style bracket syntax: `[ ... ]` blocks (F# list literals).
                        // Convert block brackets to `{}` then reuse the brace parser.
                        let converted = CoreParser.toBraceLines lines
                        let res, _ = BraceParser.parseBraceBlock 0 converted vars
                        res
                // Store in cache
                parseCache.[sourceHash] <- result
                result

    /// Clears the parse cache. Useful for testing or when memory is a concern.
    let clearCache () = parseCache.Clear()

    /// Returns all parsing errors collected during the last parse operation.
    let getErrors () = CoreParser.getErrors()
