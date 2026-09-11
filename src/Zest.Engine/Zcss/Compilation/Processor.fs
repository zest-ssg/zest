namespace Zest.Engine.Zcss

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.RegularExpressions
open System.Threading
open System.Threading.Tasks

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
    let private resultCache = ConcurrentDictionary<int64, string>()

    // ── File-level cache with modification time tracking ────────
    // Delegates to ZcssCache (mtime + SHA-256 content hash). The hybrid
    // strategy skips file reads entirely when the mtime is unchanged, and
    // still catches files that were touched but whose content did not change.

    /// Stable 64-bit hash of a string (FNV-1a variant). Good enough for a
    /// process-local cache; not cryptographic.
    let private hashSource (s: string) : int64 =
        let mutable h = 0xcbf29ce484222325UL
        for c in s do
            h <- h ^^^ (uint64 c)
            h <- h * 0x100000001b3UL
        int64 h

    /// Resolve user file @use imports relative to a base directory.
    /// Single source of truth lives in Modules.getModuleSource.
    let private resolveUserImport = Modules.getModuleSource

    /// Process ZCSS source text with a known base directory for @use resolution.
    /// (Uncached inner implementation — the public `processText` wraps this with
    /// a content-hash cache and error guard.)
    let private processTextWithBaseDirUncached (baseDir: string option) (source: string) : string =

        // Step 1: Extract and remove @use lines, collect imported contents.
        // Capture aliases (group 2) so namespaced variables (e.g. `p.primary`
        // from `@use "zest:palette" as p;`) can be resolved later.
        let userSource, importedContents, importedByPath, useDirectives =
            let uses = usePat.Matches(source)
            let imported = ResizeArray<string>()
            let byPath = Dictionary<string, string>()
            let directives = ResizeArray<Modules.UseDirective>()
            let userSrc = usePat.Replace(source, "")
            for m in uses do
                let path = m.Groups.[1].Value
                let alias = if m.Groups.[2].Success then Some m.Groups.[2].Value else None
                directives.Add({ Modules.Path = path; Modules.Alias = alias })
                match resolveUserImport baseDir path with
                | Some content ->
                    imported.Add(content)
                    byPath.[path] <- content
                | None -> ()
            userSrc, List.ofSeq imported, Map.ofSeq (seq { for kv in byPath -> kv.Key, kv.Value }), List.ofSeq directives

        // Step 2: Parse imported modules with mode-detected parser and collect their AST + variables
        let importedNodes, importedVars =
            let allNodes = ResizeArray<ZcssNode>()
            let allVars = new Dictionary<string, string>()
            for content in importedContents do
                let cleaned = CoreParser.stripComments content
                let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
                let vars = CoreParser.extractVars lines
                for kv in vars do allVars.[kv.Key] <- kv.Value
                let importMode = CoreParser.detectMode lines
                let nodes =
                    match importMode with
                    | CoreParser.BraceMode ->
                        let result, _ = BraceParser.parseBraceBlock 0 lines vars
                        result
                    | CoreParser.IndentMode ->
                        let result, _ = IndentParser.parseIndentBlock 0 lines 0 vars
                        result
                    | CoreParser.BracketMode ->
                        let result, _ = BraceParser.parseBraceBlock 0 (CoreParser.toBraceLines lines) vars
                        result
                allNodes.AddRange(nodes)
            Seq.toList allNodes, (allVars :> IDictionary<string, string>)

        // Step 3: Parse user content (sans @use lines) with mode-detected parser
        let cleanedUser = CoreParser.stripComments userSource
        let userLines = cleanedUser.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        let mode = CoreParser.detectMode userLines
        let userVars = CoreParser.extractVars userLines

        // Merge imported vars + namespaced vars + user vars (user vars take
        // precedence for !default). Namespaced vars register `alias.name`
        // keys so `@use "zest:palette" as p;` makes `p.primary` resolvable.
        let namespacedVars = Modules.buildNamespacedVars baseDir useDirectives (Some importedByPath)
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
        // A declaration value only needs re-resolution when it may reference
        // variables (SCSS `$name`, namespaced `alias.name`, pipes, conditionals
        // or let-bindings). Plain CSS values skip the whole resolver pipeline.
        let needsResolve (v: string) =
            v.Contains('$') || v.Contains("|>") || v.Contains('(') ||
            v.StartsWith("if ", StringComparison.Ordinal) || v.Contains("let ")

        let rec resolveNodeValues (n: ZcssNode) : ZcssNode =
            let resolveDeclValue (d: Declaration) : Declaration =
                if needsResolve d.Value then { d with Value = Evaluator.resolveValue d.Value mergedVars }
                else d
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
            | CoreParser.BraceMode ->
                let result, _ = BraceParser.parseBraceBlock 0 userLines mergedVars
                result
            | CoreParser.IndentMode ->
                let result, _ = IndentParser.parseIndentBlock 0 userLines 0 mergedVars
                result
            | CoreParser.BracketMode ->
                let result, _ = BraceParser.parseBraceBlock 0 (CoreParser.toBraceLines userLines) mergedVars
                result

        // Step 4: Merge ASTs — imports first, then user (so user overrides imports)
        let mergedNodes = importedNodesResolved @ userNodes

        let css = Compiler.compile mergedNodes mergedVars

        // Report any parse errors
        let errors = Parser.getErrors()
        for err in errors do
            eprintfn "%O" err

        css

    /// Resolve user-file @use imports (built-ins excluded) to absolute paths
    /// with their current mtime. Built-in modules are static and form no file
    /// dependency; only real files can invalidate a dependent.
    let private resolveUserDeps (baseDir: string option) (source: string) : (string * int64) list =
        usePat.Matches(source)
        |> Seq.choose (fun m ->
            let path = m.Groups.[1].Value
            if ZcssHelpers.resolveUse path |> Option.isSome then None
            else
                match baseDir with
                | Some dir ->
                    let fullPath = Path.GetFullPath(Path.Combine(dir, path))
                    if File.Exists fullPath then Some(fullPath, File.GetLastWriteTimeUtc(fullPath).Ticks)
                    else None
                | None -> None)
        |> Seq.toList

    /// Process ZCSS source text with a known base directory (cached + guarded).
    /// Results are cached by a content hash so unchanged files are not
    /// re-parsed on dev-server rebuilds; malformed input yields an error
    /// comment instead of crashing the build. The cache key includes the
    /// @use module fingerprint (paths + mtimes) so a changed import
    /// invalidates the result even when the dependent source text is identical.
    let processTextWithBase (baseDir: string option) (source: string) : string =
        let moduleFingerprint =
            resolveUserDeps baseDir source
            |> List.map (fun (p, m) -> p + ":" + string m)
            |> String.concat ","
        let key = hashSource ((defaultArg baseDir "") + "\x00" + moduleFingerprint + "\x00" + source)
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
    /// Uses file-level caching with modification time tracking to avoid
    /// re-processing unchanged files. This significantly improves build performance
    /// during development when only a subset of files change. A file whose
    /// @use imports changed is recompiled even when its own mtime is unchanged.
    let processFile (filePath: string) : string =
        let baseDir = Some (Path.GetDirectoryName(Path.GetFullPath(filePath)))
        let fileLastWrite = File.GetLastWriteTimeUtc(filePath).Ticks

        // Fast path: mtime unchanged AND no @use dependency changed → skip
        // file read and hashing entirely.
        match ZcssCache.tryGet filePath fileLastWrite "" with
        | Some cached when not (ZcssCache.dependenciesChanged filePath) -> cached
        | _ ->
            let source = File.ReadAllText(filePath)
            let contentHash = ZcssCache.hashContent source
            // Slow path: mtime changed. If the content hash matches the cached
            // one (file touched, not edited), reuse the compiled output.
            match ZcssCache.tryGet filePath fileLastWrite contentHash with
            | Some cached when not (ZcssCache.dependenciesChanged filePath) -> cached
            | _ ->
                let result = processTextWithBase baseDir source
                let deps = resolveUserDeps baseDir source
                ZcssCache.set filePath fileLastWrite contentHash result deps
                result

    /// Explicitly clear all in-memory ZCSS caches (result-level and file-level).
    /// On-disk cache files are preserved unless `ZcssCache.clearDiskCache` is used.
    let clearCaches () =
        resultCache.Clear()
        ZcssCache.clearCache ()

    /// Process a ZCSS file → write CSS to destination
    let processFileTo (src: string) (dst: string) : string =
        let css = processFile src
        let dir = Path.GetDirectoryName(dst)
        if not (String.IsNullOrEmpty dir) then Directory.CreateDirectory(dir) |> ignore
        File.WriteAllText(dst, css, Encoding.UTF8)
        css

module BundleService =

    /// Process all .zcss files in assetsDir → outputDir/assets/
    /// Uses parallel processing for improved performance on multi-core systems.
    /// Loads the file-level cache from disk before processing and persists it
    /// afterwards, so unchanged files are never re-read or recompiled across
    /// application restarts. Only recompiles changed files.
    let processZcssFiles (assetsDir: string) (outputDir: string) : int =
        if not (Directory.Exists assetsDir) then 0
        else
            let files = Directory.GetFiles(assetsDir, "*.zcss", SearchOption.AllDirectories)
            if files.Length = 0 then 0
            else
                let outAssets = Path.Combine(outputDir, "assets")
                let cacheFile = Path.Combine(outAssets, ".zcss-cache.log")
                Directory.CreateDirectory(outAssets) |> ignore
                // Warm the in-memory file cache from the previous run so
                // untouched sources skip the read + compile entirely.
                ZcssCache.load cacheFile
                let mutable count = 0
                
                // Process files in parallel for better performance
                // Use Parallel.ForEach with custom partitioning for better load balancing
                Parallel.ForEach(files, fun f ->
                    try
                        let rel = Path.GetRelativePath(assetsDir, f)
                        let cssRel = Path.ChangeExtension(rel, Zest.Engine.FileExtensions.Css)
                        let target = Path.Combine(outAssets, cssRel)
                        Processor.processFileTo f target |> ignore
                        Interlocked.Increment(&count) |> ignore
                    with ex ->
                        eprintfn "[ZCSS ERROR] '%s': %s" f ex.Message
                ) |> ignore

                // Persist the file cache so the next process start is warm.
                ZcssCache.save cacheFile
                count
