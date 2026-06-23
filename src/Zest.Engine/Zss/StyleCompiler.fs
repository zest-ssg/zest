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

    // Cached regex — created once, reused across all calls
    let private usePat = Regex(@"^\s*@use\s+[""']([^""']+)[""'](?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    /// Process ZSS source text → CSS string
    /// Uses AST-level merge: built-in modules parsed with brace parser,
    /// user content parsed with mode-detected parser, then ASTs merged.
    let processText (source: string) : string =

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

        // Re-resolve every declaration's value in the built-in AST with the
        // merged variable dictionary. This makes user-defined variables
        // (e.g. `$primary: #6c63ff`) visible inside utility classes
        // (e.g. `.text-primary { color: $primary }`).
        let rec resolveNodeValues (n: ZssNode) : ZssNode =
            let resolveDeclValue (d: Declaration) : Declaration =
                { d with Value = Evaluator.resolveValue d.Value mergedVars }
            match n with
            | RuleSet(sel, decls, children, pos) ->
                RuleSet(sel, decls |> List.map resolveDeclValue,
                        children |> List.map resolveNodeValues, pos)
            | AtRule(name, prms, body, pos) ->
                AtRule(name, prms, body |> List.map resolveNodeValues, pos)
            | Responsive(bp, body, pos) ->
                Responsive(bp, body |> List.map resolveNodeValues, pos)
            | Mixin(name, parms, body, pos) ->
                Mixin(name, parms, body |> List.map resolveNodeValues, pos)
            | Each(varName, items, body, pos) ->
                Each(varName, items, body |> List.map resolveNodeValues, pos)
            | For(varName, from, through, body, pos) ->
                For(varName, from, through, body |> List.map resolveNodeValues, pos)
            | If(cond, body, eb, pos) ->
                let eb' = eb |> Option.map (fun b -> b |> List.map resolveNodeValues)
                If(cond, body |> List.map resolveNodeValues, eb', pos)
            | Include(name, args, content, pos) ->
                Include(name, args, content |> List.map resolveNodeValues, pos)
            | other -> other

        let builtinNodesResolved = builtinNodes |> List.map resolveNodeValues

        let userNodes =
            match mode with
            | ParserCore.BraceMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 userLines mergedVars
                result
            | ParserCore.IndentMode ->
                let result, _ = ParserIndent.parseIndentBlock 0 userLines 0 mergedVars
                result

        // Step 4: Merge ASTs — builtins first, then user (so user overrides builtins)
        let mergedNodes = builtinNodesResolved @ userNodes

        let css = Compiler.compile mergedNodes mergedVars

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
