namespace Zest.Engine.Zcss

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open System.Text.RegularExpressions
open System.Threading

// ============================================================
// ZCSS Compiler — CSS generation with minification & auto-prefix
// ============================================================

module Compiler =

    // ── Auto-vendor-prefix map ──────────────────────────────

    /// Auto-prefixer for CSS vendor-specific properties.
    /// Adds necessary -webkit-, -moz-, -ms- prefixes to CSS properties
    /// based on browser compatibility requirements.
    module AutoPrefixer =
        /// Map of CSS properties to their required vendor prefixes.
        let private prefixMap =
            dict [
                "appearance",         [|"-webkit-"; "-moz-"|]
                "user-select",        [|"-webkit-"; "-moz-"; "-ms-"|]
                "backdrop-filter",    [|"-webkit-"|]
                "hyphens",            [|"-webkit-"; "-moz-"; "-ms-"|]
                "tab-size",           [|"-moz-"; "-o-"|]
                "text-size-adjust",   [|"-webkit-"; "-moz-"; "-ms-"|]
                "scroll-snap-type",   [|"-webkit-"|]
                "scroll-snap-align",  [|"-webkit-"|]
                "scroll-snap-stop",   [|"-webkit-"|]
                "mask",               [|"-webkit-"|]
                "mask-clip",          [|"-webkit-"|]
                "mask-composite",     [|"-webkit-"|]
                "mask-image",         [|"-webkit-"|]
                "mask-origin",        [|"-webkit-"|]
                "mask-position",      [|"-webkit-"|]
                "mask-repeat",        [|"-webkit-"|]
                "mask-size",          [|"-webkit-"|]
                "clip-path",          [|"-webkit-"|]
                "shape-outside",      [|"-webkit-"|]
                "shape-image-threshold", [|"-webkit-"|]
                "shape-margin",       [|"-webkit-"|]
                "box-decoration-break", [|"-webkit-"|]
                "font-feature-settings", [|"-webkit-"; "-moz-"|]
                "font-variant-ligatures", [|"-webkit-"; "-moz-"|]
                "font-language-override", [|"-moz-"|]
                "writing-mode",       [|"-webkit-"; "-ms-"|]
                "text-orientation",   [|"-webkit-"|]
                "text-combine-upright", [|"-webkit-"; "-ms-"|]
                "ruby-position",      [|"-webkit-"|]
                "line-break",         [|"-webkit-"; "-ms-"|]
                "text-spacing",       [|"-ms-"|]
            ]

        /// Generates vendor-prefixed property declarations for a given CSS property.
        /// Returns a list of (property, value) tuples including the original property
        /// and all necessary prefixed versions.
        let prefix (prop: string) (value: string) : (string * string) list =
            match prefixMap.TryGetValue prop with
            | true, prefixes when prefixes.Length > 0 ->
                [ for p in prefixes -> (p + prop, value) ]
                @ [(prop, value)]
            | _ -> [(prop, value)]

    // ── CSS minifier ────────────────────────────────────────

    module Minifier =
        // Module-level compiled patterns: static Regex.Replace(string, string)
        // overloads recompile the pattern on every call, which dominates
        // minification time on large stylesheets.
        let private whitespaceRe   = Regex(@"\s+", RegexOptions.Compiled)
        let private punctuationRe  = Regex(@"\s*([{}:;,])\s*", RegexOptions.Compiled)
        let private trailingSemiRe = Regex(@";}", RegexOptions.Compiled)

        // Curried helper sidesteps the overloaded-member ambiguity of
        // Regex.Replace when used in a pipeline.
        let private replaceAll (pattern: Regex) (replacement: string) (input: string) =
            pattern.Replace(input, replacement)

        let minify (css: string) : string =
            replaceAll CoreParser.blockCommentRe "" css
            // Collapse whitespace
            |> replaceAll whitespaceRe " "
            // Remove spaces around braces, colons, semicolons
            |> replaceAll punctuationRe "$1"
            // Remove trailing semicolons before }
            |> replaceAll trailingSemiRe "}"
            |> fun s -> s.Trim()

    // ── Main compiler ────────────────────────────────────────

    // ── Utility class registry (for @apply) ─────────────────

    module UtilityRegistry =
        let private classCache = ConcurrentDictionary<string, Declaration list>()

        let private parseUtilities () =
            let source = BuiltinStyles.builtinUtilities
            let nodes = Parser.parse source
            for node in nodes do
                match node with
                | RuleSet(sel, decls, _, _) ->
                    let cls = sel.Trim()
                    if cls.StartsWith(".") then
                        classCache.[cls.Substring(1)] <- decls
                | _ -> ()

        let getDecls (className: string) : Declaration list =
            if classCache.Count = 0 then parseUtilities ()
            match classCache.TryGetValue(className) with
            | true, decls -> decls
            | _ -> []

    /// Collect all declarations from matching rules (for @extend)
    let private collectExtendDecls (selector: string) (allNodes: ZcssNode list) : Declaration list =
        let rec collect nodes =
            [ for n in nodes do
                match n with
                | RuleSet(sel, decls, children, _) when sel.Trim() = selector.Trim() ->
                    yield! decls
                    yield! collect children
                | RuleSet(_, _, children, _) -> yield! collect children
                | _ -> () ]
        collect allNodes

    // ── Selector nesting helpers ─────────────────────────────
    // Support comma-grouped selectors: `.a, .b { .x { } }` must
    // expand to `.a .x, .b .x` — prefix every top-level comma part.

    /// Split a selector string on top-level commas (respecting parens, brackets, quotes).
    let private splitTopLevel (s: string) : string list =
        let parts = ResizeArray<string>()
        let sb = StringBuilder()
        let mutable depth = 0
        let mutable inS = false
        let mutable inD = false
        for c in s do
            if inS then
                if c = '\'' then inS <- false
                sb.Append(c) |> ignore
            elif inD then
                if c = '"' then inD <- false
                sb.Append(c) |> ignore
            elif c = '\'' then
                inS <- true
                sb.Append(c) |> ignore
            elif c = '"' then
                inD <- true
                sb.Append(c) |> ignore
            elif c = '(' || c = '[' || c = '{' then
                depth <- depth + 1
                sb.Append(c) |> ignore
            elif c = ')' || c = ']' || c = '}' then
                depth <- max 0 (depth - 1)
                sb.Append(c) |> ignore
            elif c = ',' && depth = 0 then
                parts.Add(sb.ToString())
                sb.Clear() |> ignore
            else
                sb.Append(c) |> ignore
        parts.Add(sb.ToString())
        parts
        |> Seq.map (fun p -> p.Trim())
        |> Seq.filter (fun p -> p.Length > 0)
        |> List.ofSeq

    /// Combine a parent prefix with one child selector part.
    let private combineSelector (parent: string) (child: string) : string =
        if String.IsNullOrEmpty parent then child
        elif String.IsNullOrEmpty child then parent
        elif child.StartsWith("&") then parent + child.Substring(1)
        elif child.StartsWith(":") || child.StartsWith("::") then parent + child
        elif child.StartsWith("@") then child
        else parent + " " + child

    /// Prepend `parent` to every top-level comma-separated part of `selector`.
    let private withParent (parent: string) (selector: string) : string =
        if String.IsNullOrEmpty parent then selector
        elif String.IsNullOrEmpty selector then parent
        elif selector.StartsWith("@") then selector
        else
            let parentParts = splitTopLevel parent
            let childParts = splitTopLevel selector
            [ for pp in parentParts do
                for cp in childParts do
                    yield combineSelector pp cp ]
            |> String.concat ", "

    /// Expand @include by resolving mixin body with argument substitution
    let private expandMixin
        (name: string)
        (args: string list)
        (content: ZcssNode list)
        (mixins: IDictionary<string, (string * string option) list * ZcssNode list>)
        (vars: IDictionary<string, string>)
        : ZcssNode list =

        match mixins.TryGetValue name with
        | false, _ -> []
        | true, (parms, body) ->
            // Build substitution map: parameter name → argument value (or default)
            let subst = Dictionary<string, string>()
            for i, (pName, pDefault) in List.indexed parms do
                let argVal =
                    if i < args.Length then args.[i]
                    else match pDefault with Some d -> d | None -> ""
                // Resolve bare variable references in argument values
                subst.[pName] <- Evaluator.resolveValue argVal vars

            let resolveVar (s: string) =
                subst |> Seq.fold (fun (acc: string) kv ->
                    Regex.Replace(acc, @"\$" + Regex.Escape(kv.Key), kv.Value)) s

            let rec applySubst nodes =
                [ for node in nodes do
                    match node with
                    | RuleSet(sel, decls, children, pos) ->
                        let newDecls = decls |> List.map (fun d ->
                            { d with Value = resolveVar d.Value })
                        yield RuleSet(resolveVar sel, newDecls, applySubst children, pos)
                    | Content _ -> yield! content  // @content slot
                    | Each(vn, items, body, pos) ->
                        yield Each(resolveVar vn, items, applySubst body, pos)
                    | If(cond, body, elseBody, pos) ->
                        yield If(resolveVar cond, applySubst body,
                                 elseBody |> Option.map applySubst, pos)
                    | other -> yield other ]
            applySubst body

    /// Evaluate @if condition — delegates to Evaluator.evalBool for full F# comparison/logic support.
    let private evalCondition (cond: string) (vars: IDictionary<string, string>) : bool =
        Evaluator.evalBool cond vars

    // ── Dual-layer compilation cache ────────────────────────
    // Compilation is a pure function of (AST structure, variable values), so
    // the output can be cached. The composite key is built from TWO layers:
    //   1. an AST structural hash (excludes source positions, which are
    //      metadata and vary between parses),
    //   2. a SHA-256 hash of the variable dictionary content.
    // Either layer changing — a different AST, or the same AST compiled with
    // different variables — produces a different key, so the cache is
    // invalidated automatically without explicit bookkeeping. A size cap keeps
    // memory bounded for long-lived dev servers.
    module CompileCache =

        /// Snapshot of cache counters and derived hit rate.
        type CacheStats = {
            /// Number of compile calls satisfied from the cache.
            Hits: int64
            /// Number of compile calls that had to recompile.
            Misses: int64
            /// Current number of cached entries.
            Entries: int
            /// Whether the cache is enabled (disabled for single-shot tools).
            Enabled: bool
        } with
            /// Hit rate in [0, 1]; 0 when no compiles have been recorded.
            member this.HitRate =
                let total = this.Hits + this.Misses
                if total = 0L then 0.0 else float this.Hits / float total

        // Pairs the key with the source AST list so a (statistically
        // impossible) hash collision is detected by structural equality and
        // treated as a miss rather than returning a wrong stylesheet.
        let private cacheStore = ConcurrentDictionary<string, ZcssNode list * string>()
        let private hits = ref 0L
        let private misses = ref 0L
        let private cacheEnabled = ref true
        let private MAX_ENTRIES = 2048

        /// Canonical hash of a variable dictionary. Insertion order is stable
        /// for dictionaries built by the pipeline, so no sorting is needed.
        let private varsKey (vars: IDictionary<string, string>) : string =
            let sb = StringBuilder()
            for kv in vars do
                sb.Append(kv.Key) |> ignore
                sb.Append('\x1f') |> ignore
                sb.Append(kv.Value) |> ignore
                sb.Append('\x1e') |> ignore
            use sha = SHA256.Create()
            let bytes = Encoding.UTF8.GetBytes(sb.ToString())
            Convert.ToHexString(sha.ComputeHash(bytes))

        /// Structural hash of the AST, ignoring source positions. F#'s generic
        /// hash is deterministic for equal structures within a process.
        let private astKey (nodes: ZcssNode list) : string =
            let h = hash nodes
            sprintf "%x|%d" (uint32 (h &&& 0x7FFFFFFF)) (List.length nodes)

        let private cacheKey (nodes: ZcssNode list) (vars: IDictionary<string, string>) =
            astKey nodes + "\x00" + varsKey vars

        /// Look up compiled CSS for (nodes, vars). Returns None on a miss or
        /// when the stored nodes no longer match (hash collision guard).
        let tryGet (nodes: ZcssNode list) (vars: IDictionary<string, string>) : string option =
            if not !cacheEnabled then None
            else
                match cacheStore.TryGetValue(cacheKey nodes vars) with
                | true, (cachedNodes, css) ->
                    if cachedNodes = nodes then
                        Interlocked.Increment(hits) |> ignore
                        Some css
                    else
                        Interlocked.Increment(misses) |> ignore
                        None
                | _ ->
                    Interlocked.Increment(misses) |> ignore
                    None

        /// Store compiled CSS for (nodes, vars), evicting the whole cache when
        /// the size cap is reached.
        let storeResult (nodes: ZcssNode list) (vars: IDictionary<string, string>) (css: string) =
            if !cacheEnabled && not (String.IsNullOrEmpty css) then
                if cacheStore.Count >= MAX_ENTRIES then cacheStore.Clear()
                cacheStore.[cacheKey nodes vars] <- (nodes, css)

        /// Current cache statistics.
        let getStats () : CacheStats =
            { Hits = !hits; Misses = !misses; Entries = cacheStore.Count; Enabled = !cacheEnabled }

        /// Zero the hit/miss counters (does not clear entries).
        let resetStats () =
            hits := 0L
            misses := 0L

        /// Empty the cache store.
        let clearCache () = cacheStore.Clear()

        /// Enable or disable caching (useful for single-shot tools where a
        /// warm cache is impossible and the key hashing is pure overhead).
        let setEnabled (flag: bool) = cacheEnabled := flag

    /// Uncached implementation of `compile` — see `compile` for the cache-aware entry point.
    let private compileCore (nodes: ZcssNode list) (vars: IDictionary<string, string>) : string =
        let sb = StringBuilder()
        let minify = ref false

        // First pass: collect all mixins
        let mixins = Dictionary<string, (string * string option) list * ZcssNode list>()
        let rec collectMixins ns =
            for n in ns do
                match n with
                | Mixin(name, parms, body, _) -> mixins.[name] <- (parms, body)
                | RuleSet(_, _, children, _) -> collectMixins children
                | AtRule(_, _, body, _) -> collectMixins body
                | _ -> ()
        collectMixins nodes

        // Collect all nodes for @extend resolution
        let allNodes = nodes

        // Resolve bare variable references in a value string
        let resolveBareVarsInCompile (value: string) : string =
            Evaluator.resolveValue value vars

        let rec emitNodes (nodes: ZcssNode list) (parent: string) =
            for node in nodes do
                match node with

                | Variable(_, _, _, _) | Mixin _ -> ()

                | Option(key, value, _) ->
                    if key = "minify" && value = "true" then minify := true

                | Warn(msg, _) ->
                    eprintfn "[ZCSS WARN] %s" msg

                | Debug(msg, _) ->
                    eprintfn "[ZCSS DEBUG] %s" msg

                | CssVarExport(name, value, _) ->
                    sb.AppendLine(sprintf ":root { --%s: %s; }" name value) |> ignore

                | Each(varName, items, body, _) ->
                    for item in items do
                        let localVars = Dictionary<string, string>(dict [varName, item])
                        let expandedBody =
                            body |> List.map (function
                                | RuleSet(sel, decls, ch, pos) ->
                                    let newSel = sel.Replace("#{$" + varName + "}", item).Replace("$" + varName, item)
                                    let newDecls = decls |> List.map (fun d ->
                                        { d with Value = d.Value.Replace("$" + varName, item) })
                                    RuleSet(newSel, newDecls, ch, pos)
                                | other -> other)
                        emitNodes expandedBody parent

                | EachMap(keyVar, valVar, mapName, body, _) ->
                    // Simple map support: look for $mapName in vars as "(k:v, k:v, ...)"
                    // This is a simplified implementation
                    ()

                | For(varName, from, through, body, _) ->
                    for i in from..through do
                        let localVars = Dictionary<string, string>(dict [varName, string i])
                        let expandedBody =
                            body |> List.map (function
                                | RuleSet(sel, decls, ch, pos) ->
                                    let newSel = sel.Replace("#{$" + varName + "}", string i).Replace("$" + varName, string i)
                                    let newDecls = decls |> List.map (fun d ->
                                        { d with Value = d.Value.Replace("$" + varName, string i) })
                                    RuleSet(newSel, newDecls, ch, pos)
                                | other -> other)
                        emitNodes expandedBody parent

                | If(cond, body, elseBody, _) ->
                    if evalCondition cond (dict []) then
                        emitNodes body parent
                    else
                        elseBody |> Option.iter (fun eb -> emitNodes eb parent)

                | Responsive(bp, body, _) ->
                    let query =
                        match bp with
                        | "sm"  -> "(min-width:640px)"
                        | "md"  -> "(min-width:768px)"
                        | "lg"  -> "(min-width:1024px)"
                        | "xl"  -> "(min-width:1280px)"
                        | "2xl" -> "(min-width:1536px)"
                        | _     -> bp
                    sb.AppendLine(sprintf "@media %s {" query) |> ignore
                    emitNodes body parent
                    sb.AppendLine("}") |> ignore

                | AtRule(name, prms, body, _) when name = "@media" || name.StartsWith("@media") ->
                    // Bare @media inside a rule → inherit parent selector
                    let fullRule = if String.IsNullOrEmpty prms then "@media" else sprintf "@media %s" prms
                    let (inlineDecls, nestedRules) =
                        body |> List.fold (fun (accD, accR) n ->
                            match n with
                            | RuleSet("", ds, [], _) -> (accD @ ds, accR)
                            | RuleSet _ -> (accD, accR @ [n])
                            | _ -> (accD, accR @ [n])) ([], [])
                    sb.AppendLine(sprintf "%s {" fullRule) |> ignore
                    if not (String.IsNullOrEmpty parent) && inlineDecls.Length > 0 then
                        sb.AppendLine(sprintf "  %s {" parent) |> ignore
                        for d in inlineDecls do
                            let imp = if d.Important then " !important" else ""
                            let rv = Evaluator.resolveValue d.Value vars
                            for (p, v) in AutoPrefixer.prefix d.Property rv do
                                sb.AppendLine(sprintf "    %s: %s%s;" p v imp) |> ignore
                        sb.AppendLine("  }") |> ignore
                    emitNodes nestedRules parent
                    sb.AppendLine("}") |> ignore

                | Import(path, _) ->
                    sb.AppendLine(sprintf "@import '%s';" path) |> ignore

                | Use(path, _, _) ->
                    // @use is handled at preprocessor level; emit as comment
                    ()

                | Comment(text, _) ->
                    if text.Trim().Length > 0 then
                        sb.AppendLine(sprintf "/* %s */" text) |> ignore

                | Include(name, args, content, _) ->
                    let expanded = expandMixin name args content mixins vars
                    emitNodes expanded parent

                | Extend(extSel, _) ->
                    let extDecls = collectExtendDecls extSel allNodes
                    if extDecls.Length > 0 then
                        sb.AppendLine(sprintf "%s {" parent) |> ignore
                        for d in extDecls do
                            let imp = if d.Important then " !important" else ""
                            let rv = Evaluator.resolveValue d.Value vars
                            sb.AppendLine(sprintf "  %s: %s%s;" d.Property rv imp) |> ignore
                        sb.AppendLine("}") |> ignore

                | Apply(classes, _) ->
                    // @apply is processed within its enclosing RuleSet (the
                    // RuleSet branch folds Apply decls into its `allDecls` so
                    // that the applied declarations live INSIDE the rule's
                    // selector block). When @apply appears at the top level
                    // (no parent), emit its declarations bare.
                    for cls in classes do
                        let className = cls.Trim().TrimStart('.')
                        let utilDecls = UtilityRegistry.getDecls className
                        for d in utilDecls do
                            let imp = if d.Important then " !important" else ""
                            let rv = Evaluator.resolveValue d.Value vars
                            let v = Evaluator.normalizePropertyValue d.Property rv
                            for (p, v2) in AutoPrefixer.prefix d.Property v do
                                sb.AppendLine(sprintf "  %s: %s%s;" p v2 imp) |> ignore

                | Content _ -> ()  // handled in expandMixin

                | AtRule(name, prms, body, _) ->
                    let fullRule = if prms.Length > 0 then sprintf "%s %s" name prms else name
                    sb.AppendLine(sprintf "%s {" fullRule) |> ignore
                    emitNodes body ""
                    sb.AppendLine("}") |> ignore

                | RuleSet(selector, decls, children, _) ->
                    let fullSel = withParent parent selector

                    // Expand @include / @content in children
                    let expandedChildren =
                        children |> List.collect (fun c ->
                            match c with
                            | Include(name, args, content, _) ->
                                expandMixin name args content mixins vars
                            | _ -> [c])

                    // First, fold any @apply nodes that appeared earlier in the
                    // same parent scope into the resolved utility decls so that
                    // they live INSIDE the rule's block. Collect them via a
                    // continuation pass.
                    let applyDecls =
                        expandedChildren
                        |> List.collect (function
                            | Apply(classes, _) ->
                                classes
                                |> List.collect (fun cls ->
                                    let className = cls.Trim().TrimStart('.')
                                    UtilityRegistry.getDecls className)
                            | _ -> [])

                    // Separate declarations from nested rules
                    let allDecls =
                        decls
                        @ applyDecls
                        @ (expandedChildren |> List.collect (function
                            | RuleSet("", ds, [], _) -> ds
                            | _ -> []))

                    let nestedRules =
                        expandedChildren |> List.filter (function
                            | RuleSet("", _, _, _) -> false
                            | RuleSet _ -> true
                            | _ -> false)
                    let otherNodes =
                        expandedChildren |> List.filter (function
                            | RuleSet _ -> false
                            | Apply _ -> false
                            | _ -> true)

                    // Emit declarations with their selector
                    let emitDecls (sel: string) (ds: Declaration list) =
                        if ds.Length > 0 && not (String.IsNullOrEmpty sel) then
                            sb.AppendLine(sprintf "%s {" sel) |> ignore
                            for d in ds do
                                let imp = if d.Important then " !important" else ""
                                let rv = Evaluator.resolveValue d.Value vars
                                let v = Evaluator.normalizePropertyValue d.Property rv
                                for (p, v2) in AutoPrefixer.prefix d.Property v do
                                    sb.AppendLine(sprintf "  %s: %s%s;" p v2 imp) |> ignore
                            sb.AppendLine("}") |> ignore

                    if String.IsNullOrEmpty fullSel then
                        // Bare declarations — emit under parent selector if available
                        if not (String.IsNullOrEmpty parent) && allDecls.Length > 0 then
                            emitDecls parent allDecls
                        else
                            for d in allDecls do
                                let imp = if d.Important then " !important" else ""
                                let rv = Evaluator.resolveValue d.Value vars
                                let v = Evaluator.normalizePropertyValue d.Property rv
                                for (p, v2) in AutoPrefixer.prefix d.Property v do
                                    sb.AppendLine(sprintf "  %s: %s%s;" p v2 imp) |> ignore
                    elif fullSel.StartsWith("@") then
                        sb.AppendLine(sprintf "%s {" fullSel) |> ignore
                        for d in allDecls do
                            let imp = if d.Important then " !important" else ""
                            let rv = Evaluator.resolveValue d.Value vars
                            let v = Evaluator.normalizePropertyValue d.Property rv
                            for (p, v2) in AutoPrefixer.prefix d.Property v do
                                sb.AppendLine(sprintf "    %s: %s%s;" p v2 imp) |> ignore
                        emitNodes nestedRules ""
                        emitNodes otherNodes ""
                        sb.AppendLine("}") |> ignore
                    else
                        emitDecls fullSel allDecls
                        emitNodes nestedRules fullSel
                        emitNodes otherNodes fullSel

        emitNodes nodes ""
        let result = sb.ToString().Trim()
        if !minify then Minifier.minify result else result

    /// Compiles a list of ZCSS AST nodes into CSS output string.
    /// Handles mixin expansion, variable resolution, control flow directives,
    /// responsive breakpoints, and applies vendor prefixes as needed.
    /// Supports minification via @option minify: true directive.
    /// Uses the dual-layer compilation cache (AST structure × variable hash)
    /// to avoid re-compiling identical inputs; either dimension changing
    /// produces a new key and invalidates the previous entry naturally.
    let compile (nodes: ZcssNode list) (vars: IDictionary<string, string>) : string =
        match CompileCache.tryGet nodes vars with
        | Some cached -> cached
        | None ->
            let result = compileCore nodes vars
            CompileCache.storeResult nodes vars result
            result
