namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// ZCSS — Public API
// ============================================================
// Backward-compatible entry point. Delegates to the new modular
// pipeline: Parser → Compiler, with @use module resolution.
// ============================================================

module Processor =

    // Cached regex — created once, reused across all calls
    let private usePat = Regex(@"^\s*@use\s+[""']([^""']+)[""'](?:\s+as\s+(\w+))?\s*;?\s*$", RegexOptions.Compiled ||| RegexOptions.Multiline)

    // ── Result cache ────────────────────────────────────────────
    // ZCSS processing is pure (source → CSS). Caching by a hash of the
    // source avoids re-parsing/re-compiling unchanged files during dev-server
    // rebuilds triggered by non-ZCSS changes. The cache is keyed on the
    // (baseDir, source) hash.
    let private resultCache = System.Collections.Concurrent.ConcurrentDictionary<int64, string>()

    /// Stable 64-bit hash of a string (FNV-1a variant). Good enough for a
    /// process-local cache; not cryptographic.
    let private hashSource (s: string) : int64 =
        let mutable h = 0xcbf29ce484222325UL
        for c in s do
            h <- h ^^^ (uint64 c)
            h <- h * 0x100000001b3UL
        int64 h

    /// Resolve user file @use imports relative to a base directory.
    let private resolveUserImport (baseDir: string option) (path: string) : string option =
        // Try built-in modules first
        match Utilities.resolveUse path with
        | Some _ as result -> result
        | None ->
            // Not a built-in — try file path relative to baseDir
            match baseDir with
            | Some dir ->
                let fullPath = Path.GetFullPath(Path.Combine(dir, path))
                if File.Exists fullPath then
                    Some (File.ReadAllText(fullPath))
                else
                    eprintfn "[ZCSS WARN] @use import not found: '%s' (resolved: %s)" path fullPath
                    None
            | None ->
                eprintfn "[ZCSS WARN] @use import '%s' skipped — no source file context" path
                None

    /// Process ZCSS source text with a known base directory for @use resolution.
    /// (Uncached inner implementation — the public `processText` wraps this with
    /// a content-hash cache and error guard.)
    let private processTextWithBaseDirUncached (baseDir: string option) (source: string) : string =

        // Step 1: Extract and remove @use lines, collect imported contents.
        // Capture aliases (group 2) so namespaced variables (e.g. `p.primary`
        // from `@use "zest:palette" as p;`) can be resolved later.
        let userSource, importedContents, useDirectives =
            let uses = usePat.Matches(source)
            let imported = ResizeArray<string>()
            let directives = ResizeArray<Modules.UseDirective>()
            let userSrc = usePat.Replace(source, "")
            for m in uses do
                let path = m.Groups.[1].Value
                let alias = if m.Groups.[2].Success then Some m.Groups.[2].Value else None
                directives.Add({ Modules.Path = path; Modules.Alias = alias })
                match resolveUserImport baseDir path with
                | Some content -> imported.Add(content)
                | None -> ()
            userSrc, List.ofSeq imported, List.ofSeq directives

        // Step 2: Parse imported modules with mode-detected parser and collect their AST + variables
        let importedNodes, importedVars =
            let allNodes = ResizeArray<ZcssNode>()
            let allVars = new Dictionary<string, string>()
            for content in importedContents do
                let cleaned = ParserCore.stripComments content
                let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
                let vars = ParserCore.extractVars lines
                for kv in vars do allVars.[kv.Key] <- kv.Value
                let importMode = ParserCore.detectMode lines
                let nodes =
                    match importMode with
                    | ParserCore.BraceMode ->
                        let result, _ = ParserBrace.parseBraceBlock 0 lines vars
                        result
                    | ParserCore.IndentMode ->
                        let result, _ = ParserIndent.parseIndentBlock 0 lines 0 vars
                        result
                    | ParserCore.BracketMode ->
                        let result, _ = ParserBrace.parseBraceBlock 0 (ParserCore.toBraceLines lines) vars
                        result
                allNodes.AddRange(nodes)
            Seq.toList allNodes, (allVars :> IDictionary<string, string>)

        // Step 3: Parse user content (sans @use lines) with mode-detected parser
        let cleanedUser = ParserCore.stripComments userSource
        let userLines = cleanedUser.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let mode = ParserCore.detectMode userLines
        let userVars = ParserCore.extractVars userLines

        // Merge imported vars + namespaced vars + user vars (user vars take
        // precedence for !default). Namespaced vars register `alias.name`
        // keys so `@use "zest:palette" as p;` makes `p.primary` resolvable.
        let namespacedVars = Modules.buildNamespacedVars baseDir useDirectives
        let mergedVars =
            let d = new Dictionary<string, string>()
            for kv in importedVars do d.[kv.Key] <- kv.Value
            for kv in namespacedVars do d.[kv.Key] <- kv.Value
            for kv in userVars do d.[kv.Key] <- kv.Value
            d :> IDictionary<string, string>

        // Re-resolve every declaration's value in the built-in AST with the
        // merged variable dictionary. This makes user-defined variables
        // (e.g. `$primary: #6c63ff`) visible inside utility classes
        // (e.g. `.text-primary { color: $primary }`).
        let rec resolveNodeValues (n: ZcssNode) : ZcssNode =
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

        let importedNodesResolved = importedNodes |> List.map resolveNodeValues

        let userNodes =
            match mode with
            | ParserCore.BraceMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 userLines mergedVars
                result
            | ParserCore.IndentMode ->
                let result, _ = ParserIndent.parseIndentBlock 0 userLines 0 mergedVars
                result
            | ParserCore.BracketMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 (ParserCore.toBraceLines userLines) mergedVars
                result

        // Step 4: Merge ASTs — imports first, then user (so user overrides imports)
        let mergedNodes = importedNodesResolved @ userNodes

        let css = Compiler.compile mergedNodes mergedVars

        // Report any parse errors
        let errors = Parser.getErrors()
        for err in errors do
            eprintfn "%O" err

        css

    /// Process ZCSS source text with a known base directory (cached + guarded).
    /// Results are cached by a content hash so unchanged files are not
    /// re-parsed on dev-server rebuilds; malformed input yields an error
    /// comment instead of crashing the build.
    let processTextWithBase (baseDir: string option) (source: string) : string =
        let key = hashSource ((defaultArg baseDir "") + "\x00" + source)
        match resultCache.TryGetValue(key) with
        | true, cached -> cached
        | _ ->
            let result =
                try processTextWithBaseDirUncached baseDir source
                with ex ->
                    eprintfn "[ZCSS ERROR] %s" ex.Message
                    sprintf "/* ZCSS ERROR: %s */" (ex.Message.Replace("*/", "*\\/"))
            resultCache.[key] <- result
            result

    /// Process ZCSS source text → CSS string
    /// Uses AST-level merge: built-in modules parsed with brace parser,
    /// user content parsed with mode-detected parser, then ASTs merged.
    let processText (source: string) : string =
        processTextWithBase None source

    /// Process a ZCSS file → CSS string
    let processFile (filePath: string) : string =
        let baseDir = Some (Path.GetDirectoryName(Path.GetFullPath(filePath)))
        File.ReadAllText(filePath) |> processTextWithBase baseDir

    /// Process a ZCSS file → write CSS to destination
    let processFileTo (src: string) (dst: string) : string =
        let css = processFile src
        let dir = Path.GetDirectoryName(dst)
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory(dir) |> ignore
        File.WriteAllText(dst, css, Encoding.UTF8)
        css

module BundleService =

    /// Process all .zcss files in assetsDir → outputDir/assets/
    let processZcssFiles (assetsDir: string) (outputDir: string) : int =
        if not (Directory.Exists assetsDir) then 0
        else
            let files = Directory.GetFiles(assetsDir, "*.zcss", SearchOption.AllDirectories)
            if files.Length = 0 then 0
            else
                let outAssets = Path.Combine(outputDir, "assets")
                Directory.CreateDirectory(outAssets) |> ignore
                let mutable count = 0
                for f in files do
                    try
                        let rel = Path.GetRelativePath(assetsDir, f)
                        let cssRel = Path.ChangeExtension(rel, FileExtensions.Css)
                        let target = Path.Combine(outAssets, cssRel)
                        Processor.processFileTo f target |> ignore
                        count <- count + 1
                    with ex ->
                        eprintfn "[ZCSS ERROR] '%s': %s" f ex.Message
                count
