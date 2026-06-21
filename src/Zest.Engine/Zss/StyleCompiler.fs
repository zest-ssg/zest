namespace Zest.Engine.Zss

open System
open System.Collections.Generic
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

    /// Process ZSS source text → CSS string
    /// Uses AST-level merge: built-in modules parsed with brace parser,
    /// user content parsed with mode-detected parser, then ASTs merged.
    let processText (source: string) : string =
        let usePat = Regex(@"^\s*@use\s+[""']([^""']+)[""'](?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.Multiline)

        // Step 1: Extract and remove @use lines, collect built-in module contents
        let userSource, builtinContents =
            let uses = usePat.Matches(source)
            let builtins = ResizeArray<string>()
            let userSrc = usePat.Replace(source, "")
            for m in uses do
                match Utilities.resolveUse (m.Groups.[1].Value) with
                | Some content -> builtins.Add(content)
                | None -> ()
            userSrc, List.ofSeq builtins

        // Step 2: Parse built-in modules with brace parser and collect their AST + variables
        let builtinNodes, builtinVars =
            let allNodes = ResizeArray<ZssNode>()
            let allVars = new Dictionary<string, string>()
            for content in builtinContents do
                let cleaned = ParserCore.stripComments content
                let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
                let vars = ParserCore.extractVars lines
                for kv in vars do allVars.[kv.Key] <- kv.Value
                let nodes, _ = ParserBrace.parseBraceBlock 0 lines vars
                allNodes.AddRange(nodes)
            Seq.toList allNodes, (allVars :> IDictionary<string, string>)

        // Step 3: Parse user content (sans @use lines) with mode-detected parser
        let cleanedUser = ParserCore.stripComments userSource
        let userLines = cleanedUser.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let mode = ParserCore.detectMode userLines
        let userVars = ParserCore.extractVars userLines
        // Merge builtin vars into user vars (user vars take precedence for !default)
        let mergedVars =
            let d = new Dictionary<string, string>()
            for kv in builtinVars do d.[kv.Key] <- kv.Value
            for kv in userVars do d.[kv.Key] <- kv.Value
            d :> IDictionary<string, string>

        let userNodes =
            match mode with
            | ParserCore.BraceMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 userLines mergedVars
                result
            | ParserCore.IndentMode ->
                let result, _ = ParserIndent.parseIndentBlock 0 userLines 0 mergedVars
                result

        // Step 4: Merge ASTs — builtins first, then user (so user overrides builtins)
        let mergedNodes = builtinNodes @ userNodes

        let css = Compiler.compile mergedNodes

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
