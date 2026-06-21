namespace Zest.Engine.Zss

open System
open System.IO
open System.Text
open System.Text.RegularExpressions

// ============================================================
// ZSS 2.0 — Public API
// ============================================================
// Backward-compatible entry point. Delegates to the new modular
// pipeline: Parser → Compiler, with @use module resolution.
// ============================================================

module Processor =

    /// Preprocess source: resolve @use directives by inlining built-in modules.
    let private resolveUses (source: string) : string =
        let usePat = Regex(@"^\s*@use\s+[""']([^""']+)[""'](?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.Multiline)
        usePat.Replace(source, fun m ->
            let path = m.Groups.[1].Value
            match Utilities.resolveUse path with
            | Some content -> content
            | None -> m.Value  // keep as-is for external imports
        )

    /// Process ZSS source text → CSS string
    let processText (source: string) : string =
        // Detect mode BEFORE resolving @use, because built-in modules
        // may use brace syntax while the user file uses indent syntax.
        let cleaned = ParserCore.stripComments source
        let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let mode = ParserCore.detectMode lines

        let resolved = resolveUses source
        let cleanedResolved = ParserCore.stripComments resolved
        let linesResolved = cleanedResolved.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let vars = ParserCore.extractVars linesResolved

        let nodes =
            match mode with
            | ParserCore.BraceMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 linesResolved vars
                result
            | ParserCore.IndentMode ->
                let result, _ = ParserIndent.parseIndentBlock 0 linesResolved 0 vars
                result
        let css = Compiler.compile nodes

        // Report any parse errors
        let errors = Parser.getErrors()
        for err in errors do
            eprintfn "%O" err

        css

    /// Process a ZSS file → CSS string
    let processFile (filePath: string) : string =
        File.ReadAllText(filePath) |> processText

    /// Process a ZSS file → write CSS to destination
    let processFileTo (src: string) (dst: string) : string =
        let css = processFile src
        let dir = Path.GetDirectoryName(dst)
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory(dir) |> ignore
        File.WriteAllText(dst, css, Encoding.UTF8)
        css

module BundleService =

    /// Process all .zss files in assetsDir → outputDir/assets/
    let processZssFiles (assetsDir: string) (outputDir: string) : int =
        if not (Directory.Exists assetsDir) then 0
        else
            let files = Directory.GetFiles(assetsDir, "*.zss", SearchOption.AllDirectories)
            if files.Length = 0 then 0
            else
                let outAssets = Path.Combine(outputDir, "assets")
                Directory.CreateDirectory(outAssets) |> ignore
                let mutable count = 0
                for f in files do
                    try
                        let rel = Path.GetRelativePath(assetsDir, f)
                        let cssRel = Path.ChangeExtension(rel, ".css")
                        let target = Path.Combine(outAssets, cssRel)
                        Processor.processFileTo f target |> ignore
                        count <- count + 1
                    with ex ->
                        eprintfn "[ZSS ERROR] '%s': %s" f ex.Message
                count
