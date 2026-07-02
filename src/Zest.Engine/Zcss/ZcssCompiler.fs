namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

// ============================================================
// ZSS Compiler — CSS generation with minification & auto-prefix
// ============================================================

module Compiler =

    // ── Auto-vendor-prefix map ──────────────────────────────

    module AutoPrefixer =
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

        let prefix (prop: string) (value: string) : (string * string) list =
            match prefixMap.TryGetValue prop with
            | true, prefixes when prefixes.Length > 0 ->
                [ for p in prefixes -> (p + prop, value) ]
                @ [(prop, value)]
            | _ -> [(prop, value)]

    // ── CSS minifier ────────────────────────────────────────

    module Minifier =
        let minify (css: string) : string =
            css
            // Remove comments
            |> fun s -> Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline)
            // Collapse whitespace
            |> fun s -> Regex.Replace(s, @"\s+", " ")
            // Remove spaces around braces, colons, semicolons
            |> fun s -> Regex.Replace(s, @"\s*([{}:;,])\s*", "$1")
            // Remove trailing semicolons before }
            |> fun s -> Regex.Replace(s, @";}", "}")
            |> fun s -> s.Trim()

    // ── Main compiler ────────────────────────────────────────

    // ── Utility class registry (for @apply) ─────────────────

    module UtilityRegistry =
        let private classCache = Dictionary<string, Declaration list>()

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
    let private collectExtendDecls (selector: string) (allNodes: ZssNode list) : Declaration list =
        let rec collect nodes =
            [ for n in nodes do
                match n with
                | RuleSet(sel, decls, children, _) when sel.Trim() = selector.Trim() ->
                    yield! decls
                    yield! collect children
                | RuleSet(_, _, children, _) -> yield! collect children
                | _ -> () ]
        collect allNodes

    /// Expand @include by resolving mixin body with argument substitution
    let private expandMixin
        (name: string)
        (args: string list)
        (content: ZssNode list)
        (mixins: IDictionary<string, (string * string option) list * ZssNode list>)
        (vars: IDictionary<string, string>)
        : ZssNode list =

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

    let compile (nodes: ZssNode list) (vars: IDictionary<string, string>) : string =
        let sb = StringBuilder()
        let minify = ref false

        // First pass: collect all mixins
        let mixins = Dictionary<string, (string * string option) list * ZssNode list>()
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

        let rec emitNodes (nodes: ZssNode list) (parent: string) =
            for node in nodes do
                match node with

                | Variable(_, _, _, _) | Mixin _ -> ()

                | Option(key, value, _) ->
                    if key = "minify" && value = "true" then minify := true

                | Warn(msg, _) ->
                    eprintfn "[ZSS WARN] %s" msg

                | Debug(msg, _) ->
                    eprintfn "[ZSS DEBUG] %s" msg

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
                    let fullSel =
                        if String.IsNullOrEmpty parent then selector
                        elif String.IsNullOrEmpty selector then parent
                        elif selector.StartsWith("&") then parent + selector.Substring(1)
                        elif selector.StartsWith(":") || selector.StartsWith("::") then parent + selector
                        elif selector.StartsWith("@") then selector
                        else parent + " " + selector

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
