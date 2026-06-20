namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

// ============================================================
// ZSS AST
// ============================================================

type Declaration = {
    Property : string
    Value    : string
    Important: bool
}

type ZssNode =
    | RuleSet  of selector: string * declarations: Declaration list * children: ZssNode list
    | Variable of name: string * value: string
    | Mixin    of name: string * parameters: string list * body: ZssNode list
    | Include  of name: string * arguments: string list
    | Extend   of selector: string
    | Apply    of classes: string list
    | Import   of path: string
    | Comment  of text: string
    | RawBlock of atRule: string * content: string
    // New: emit $var as CSS custom property --var in :root
    | CssVarExport of name: string * value: string
    // New: @each $item in (a,b,c) { ... } loop
    | Each of varName: string * items: string list * body: ZssNode list
    // New: responsive shorthand  @sm/@md/@lg/@xl wrapping
    | Responsive of breakpoint: string * body: ZssNode list

// ============================================================
// Shorthand property map
// ============================================================

module ShorthandMap =
    let private map =
        dict [
            // Spacing
            "m",   "margin";       "mt", "margin-top";     "mr", "margin-right"
            "mb",  "margin-bottom";"ml", "margin-left";    "mx", "margin-inline"
            "my",  "margin-block"
            "p",   "padding";      "pt", "padding-top";    "pr", "padding-right"
            "pb",  "padding-bottom";"pl","padding-left";   "px", "padding-inline"
            "py",  "padding-block"
            // Background
            "bg",  "background";   "bgc","background-color";"bgi","background-image"
            "bgr", "background-repeat";"bgp","background-position";"bgs","background-size"
            // Color / Text
            "c",   "color";        "fs", "font-size";      "fw", "font-weight"
            "ff",  "font-family";  "f",  "font";           "lh", "line-height"
            "ta",  "text-align";   "td", "text-decoration";"ts", "text-shadow"
            "tt",  "text-transform";"ls","letter-spacing"; "ws", "word-spacing"
            "wsb", "word-break";   "ww", "word-wrap"
            // Layout
            "d",   "display";      "pos","position";       "t",  "top"
            "r",   "right";        "b",  "bottom";         "l",  "left"
            "z",   "z-index";      "o",  "opacity";        "ov", "overflow"
            "ovx", "overflow-x";   "ovy","overflow-y";     "cur","cursor"
            // Sizing
            "w",   "width";        "h",  "height";         "mw", "max-width"
            "mh",  "max-height";   "mnw","min-width";      "mnh","min-height"
            // Border
            "bd",  "border";       "bdc","border-color";   "bds","border-style"
            "bdw", "border-width"; "bdr","border-radius";  "bdrs","border-radius"
            // Effects
            "bxsh","box-shadow";   "bxz","box-sizing";     "tr", "transition"
            "trf", "transform";    "trfo","transform-origin";"anim","animation"
            // Misc
            "us",  "user-select";  "pe", "pointer-events"; "cl", "clear"
            "fl",  "float";        "vis","visibility";     "va", "vertical-align"
            "ls",  "list-style";   "gap","gap";            "ac", "align-content"
            "ai",  "align-items";  "as", "align-self";     "jc", "justify-content"
            "ji",  "justify-items";"js", "justify-self";   "fo", "flex-flow"
            "fb",  "flex-basis";   "fg", "flex-grow";      "fsh","flex-shrink"
            "gtc", "grid-template-columns";"gtr","grid-template-rows"
            "gc",  "grid-column";  "gr", "grid-row";       "ga", "grid-area"
            "co",  "content";      "rs", "resize";         "app","appearance"
            "out", "outline";      "asp","aspect-ratio"
        ]

    let resolve (prop: string) : string =
        match map.TryGetValue prop with
        | true, v -> v
        | _       -> prop

// ============================================================
// Value Shorthands — e.g. "1r" → "1rem", "50p" → "50%"
// ============================================================

module ValueShorthand =

    let private unitPattern = Regex(@"^(-?[\d.]+)(r|p|v|vh|vw|em|s|ms)$", RegexOptions.Compiled)

    let private unitMap =
        dict [ "r","rem"; "p","%"; "v","vh"; "s","s"; "ms","ms" ]

    let resolveToken (token: string) : string =
        let m = unitPattern.Match(token)
        if m.Success then
            let num  = m.Groups.[1].Value
            let unit = m.Groups.[2].Value
            match unitMap.TryGetValue unit with
            | true, expanded -> num + expanded
            | _              -> num + unit
        else token

    /// Resolve all space-separated value tokens.
    let resolve (value: string) : string =
        value.Split(' ')
        |> Array.map resolveToken
        |> String.concat " "

// ============================================================
// Color helper functions  (lighten / darken / alpha / mix)
// ============================================================

module ColorFunctions =

    let private hexToRgb (hex: string) : (int * int * int) option =
        let h = hex.TrimStart('#')
        if h.Length = 6 then
            try Some(Convert.ToInt32(h.[0..1], 16),
                     Convert.ToInt32(h.[2..3], 16),
                     Convert.ToInt32(h.[4..5], 16))
            with _ -> None
        elif h.Length = 3 then
            try Some(Convert.ToInt32(string h.[0] + string h.[0], 16),
                     Convert.ToInt32(string h.[1] + string h.[1], 16),
                     Convert.ToInt32(string h.[2] + string h.[2], 16))
            with _ -> None
        else None

    let private clamp v = max 0 (min 255 v)
    let private toHex r g b = sprintf "#%02x%02x%02x" (clamp r) (clamp g) (clamp b)

    let lighten (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r+d) (g+d) (b+d)

    let darken (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r-d) (g-d) (b-d)

    let alpha (hex: string) (a: float) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> sprintf "rgba(%d,%d,%d,%.2f)" r g b a

    let mix (hex1: string) (hex2: string) (pct: int) =
        match hexToRgb hex1, hexToRgb hex2 with
        | Some(r1,g1,b1), Some(r2,g2,b2) ->
            let w = float pct / 100.0
            toHex (int (float r1*w + float r2*(1.0-w)))
                  (int (float g1*w + float g2*(1.0-w)))
                  (int (float b1*w + float b2*(1.0-w)))
        | _ -> hex1

    let private lightenPat = Regex(@"lighten\(([^,)]+),\s*(\d+)%\)", RegexOptions.Compiled)
    let private darkenPat  = Regex(@"darken\(([^,)]+),\s*(\d+)%\)",  RegexOptions.Compiled)
    let private alphaPat   = Regex(@"alpha\(([^,)]+),\s*([\d.]+)\)", RegexOptions.Compiled)
    let private mixPat     = Regex(@"mix\(([^,)]+),\s*([^,)]+),\s*(\d+)%\)", RegexOptions.Compiled)

    let resolve (value: string) : string =
        value
        |> fun s -> lightenPat.Replace(s, fun m -> lighten m.Groups.[1].Value (int m.Groups.[2].Value))
        |> fun s -> darkenPat.Replace( s, fun m -> darken  m.Groups.[1].Value (int m.Groups.[2].Value))
        |> fun s -> alphaPat.Replace(  s, fun m -> alpha   m.Groups.[1].Value (float m.Groups.[2].Value))
        |> fun s -> mixPat.Replace(    s, fun m -> mix m.Groups.[1].Value m.Groups.[2].Value (int m.Groups.[3].Value))

// ============================================================
// ZSS Parser
// ============================================================

module Parser =

    let private stripComments (text: string) =
        text
        |> fun s -> Regex.Replace(s, @"//[^\n]*", "", RegexOptions.Multiline)
        |> fun s -> Regex.Replace(s, @"/\*.*?\*/", "", RegexOptions.Singleline)

    let private varPattern   = Regex(@"^\s*\$([\w-]+)\s*:\s*(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let private mixinDefPat  = Regex(@"^@mixin\s+([\w-]+)\s*(?:\(([^)]*)\))?\s*\{?\s*$", RegexOptions.Compiled)
    let private includePat   = Regex(@"^@include\s+([\w-]+)\s*(?:\(([^)]*)\))?\s*;?\s*$", RegexOptions.Compiled)
    let private extendPat    = Regex(@"^@extend\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let private applyPat     = Regex(@"^@apply\s+(.+?)\s*;?\s*$", RegexOptions.Compiled)
    let private importPat    = Regex(@"^@import\s+[""'](.+?)[""']\s*;?\s*$", RegexOptions.Compiled)
    let private atRulePat    = Regex(@"^(@[\w-]+[^{]*)\{?\s*$", RegexOptions.Compiled)

    let private eachPat    = Regex(@"^@each\s+\$([\w-]+)\s+in\s+\(([^)]+)\)\s*\{?\s*$", RegexOptions.Compiled)
    let private exportPat  = Regex(@"^@export\s+\$([\w-]+)\s*;?\s*$", RegexOptions.Compiled)
    let private rspBpMap   = dict ["sm","(min-width:640px)";"md","(min-width:768px)";"lg","(min-width:1024px)";"xl","(min-width:1280px)";"2xl","(min-width:1536px)"]

    let private extractVars (lines: string seq) =
        let d = Dictionary<string, string>()
        for line in lines do
            let m = varPattern.Match(line)
            if m.Success then
                let v = Regex.Replace(m.Groups.[2].Value.Trim(), @"\$([\w-]+)", MatchEvaluator(fun mm ->
                    match d.TryGetValue(mm.Groups.[1].Value) with true, vv -> vv | _ -> mm.Value))
                d.[m.Groups.[1].Value] <- v
        d

    let private resolveVars (value: string) (vars: IDictionary<string, string>) =
        Regex.Replace(value, @"\$([\w-]+)", MatchEvaluator(fun m ->
            match vars.TryGetValue(m.Groups.[1].Value) with true, v -> v | _ -> m.Value))

    let private parseDecl (line: string) (vars: IDictionary<string, string>) : Declaration option =
        let t = line.Trim().TrimEnd(';').Trim()
        if String.IsNullOrEmpty t || t.StartsWith("//") then None
        else
            let idx = t.IndexOf(':')
            if idx <= 0 then None
            else
                let prop = t.Substring(0, idx).Trim()
                let rest = t.Substring(idx + 1).Trim()
                let important = rest.EndsWith("!important")
                let rawVal = (if important then rest.Substring(0, rest.Length - 10) else rest).Trim()
                let value =
                    rawVal
                    |> fun v -> resolveVars v vars
                    |> ValueShorthand.resolve
                    |> ColorFunctions.resolve
                Some { Property = ShorthandMap.resolve prop; Value = value; Important = important }

    let rec private parseBlock (startIdx: int) (lines: string array) (vars: IDictionary<string, string>) : ZssNode list * int =
        let nodes = ResizeArray<ZssNode>()
        let mutable i = startIdx
        let mutable stop = false

        while i < lines.Length && not stop do
            let line = lines.[i].TrimEnd()
            let t    = line.TrimStart()

            if String.IsNullOrWhiteSpace t then
                i <- i + 1

            // Closing brace
            elif t.StartsWith("}") then
                i <- i + 1
                stop <- true

            // Variable declaration
            elif varPattern.IsMatch(t) then
                let m = varPattern.Match(t)
                let v = resolveVars (m.Groups.[2].Value.Trim()) vars
                vars.[m.Groups.[1].Value] <- v
                nodes.Add(Variable(m.Groups.[1].Value, v))
                i <- i + 1

            // @mixin definition
            elif t.StartsWith("@mixin") then
                let m = mixinDefPat.Match(t)
                if m.Success then
                    let name   = m.Groups.[1].Value
                    let parms  = if m.Groups.[2].Success then
                                     m.Groups.[2].Value.Split(',')
                                     |> Array.map (fun p -> p.Trim().TrimStart('$'))
                                     |> Array.filter (fun p -> p.Length > 0)
                                     |> Array.toList
                                 else []
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBlock i lines vars
                    i <- newI
                    nodes.Add(Mixin(name, parms, body))
                else i <- i + 1

            // @include
            elif t.StartsWith("@include") then
                let m = includePat.Match(t)
                if m.Success then
                    let name = m.Groups.[1].Value
                    let args = if m.Groups.[2].Success then
                                   m.Groups.[2].Value.Split(',')
                                   |> Array.map (fun a -> resolveVars (a.Trim().TrimStart('$')) vars)
                                   |> Array.toList
                               else []
                    nodes.Add(Include(name, args))
                i <- i + 1

            // @extend
            elif t.StartsWith("@extend") then
                let m = extendPat.Match(t)
                if m.Success then nodes.Add(Extend(m.Groups.[1].Value))
                i <- i + 1

            // @apply  (utility-first)
            elif t.StartsWith("@apply") then
                let m = applyPat.Match(t)
                if m.Success then
                    let cls = m.Groups.[1].Value.Split([|' ';','|], StringSplitOptions.RemoveEmptyEntries)
                              |> Array.toList
                    nodes.Add(Apply cls)
                i <- i + 1

            // @import
            elif t.StartsWith("@import") then
                let m = importPat.Match(t)
                if m.Success then nodes.Add(Import(m.Groups.[1].Value))
                i <- i + 1

            // @export $var — emit as CSS custom property in :root
            elif t.StartsWith("@export") then
                let m = exportPat.Match(t)
                if m.Success then
                    let vname = m.Groups.[1].Value
                    match vars.TryGetValue(vname) with
                    | true, vval -> nodes.Add(CssVarExport(vname, vval))
                    | _ -> ()
                i <- i + 1

            // @each $item in (a,b,c) { ... }
            elif t.StartsWith("@each") then
                let m = eachPat.Match(t)
                if m.Success then
                    let varName = m.Groups.[1].Value
                    let items   = m.Groups.[2].Value.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBlock i lines vars
                    i <- newI
                    nodes.Add(Each(varName, items, body))
                else i <- i + 1

            // Responsive shorthand: @sm, @md, @lg, @xl, @2xl
            elif (let key = t.TrimEnd('{', ' ').Trim().TrimStart('@') in rspBpMap.ContainsKey key) && t.StartsWith("@") then
                let key = t.TrimEnd('{', ' ').Trim().TrimStart('@')
                let hasBrace = t.Contains("{")
                i <- if hasBrace then i + 1 else i + 1
                let body, newI = parseBlock i lines vars
                i <- newI
                nodes.Add(Responsive(key, body))

            // Generic at-rule block  (@media, @keyframes, @supports, etc.)
            elif t.StartsWith("@") then
                let m = atRulePat.Match(t)
                let atRule = if m.Success then m.Groups.[1].Value.TrimEnd() else t.TrimEnd('{', ' ')
                let hasBrace = t.Contains("{")
                i <- if hasBrace then i + 1 else i + 1
                let body, newI = parseBlock i lines vars
                i <- newI
                nodes.Add(RawBlock(atRule, ""))  // body handled in compiler via children
                // Reinterpret: use nested rule sets
                let sb = StringBuilder()
                // Re-emit children as raw content (simple approach for @keyframes)
                nodes.[nodes.Count - 1] <- RuleSet(atRule, [], body)

            // Rule set with opening brace
            elif t.Contains("{") then
                let braceIdx = t.IndexOf("{")
                let selector = t.Substring(0, braceIdx).Trim()

                // Inline declarations on same line as {
                let afterBrace = t.Substring(braceIdx + 1).Trim()
                let inlineClosed = afterBrace.Contains("}")
                let inlineText   = if inlineClosed then afterBrace.Substring(0, afterBrace.IndexOf("}")) else afterBrace

                let inlineDecls =
                    inlineText.Split(';')
                    |> Array.choose (fun d ->
                        let s = d.Trim()
                        if s.Length > 0 then parseDecl (s + ";") vars else None)
                    |> Array.toList

                if inlineClosed then
                    nodes.Add(RuleSet(selector, inlineDecls, []))
                    i <- i + 1
                else
                    i <- i + 1
                    let children, newI = parseBlock i lines vars
                    i <- newI
                    nodes.Add(RuleSet(selector, inlineDecls, children))

            // Bare declaration
            elif t.Contains(":") then
                match parseDecl t vars with
                | Some d -> nodes.Add(RuleSet("", [d], []))
                | None   -> ()
                i <- i + 1

            else
                i <- i + 1

        (nodes |> Seq.toList, i)

    let parse (source: string) : ZssNode list =
        if String.IsNullOrWhiteSpace source then []
        else
            let cleaned = stripComments source
            let lines   = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd())
            let vars    = extractVars lines
            let result, _ = parseBlock 0 lines vars
            result

// ============================================================
// ZSS Compiler
// ============================================================

module Compiler =

    /// Resolve @extend: collect all declarations from matching rules in the AST.
    let private collectExtendDecls (selector: string) (allNodes: ZssNode list) : Declaration list =
        let rec collect nodes =
            [ for n in nodes do
                match n with
                | RuleSet(sel, decls, children) when sel.Trim() = selector.Trim() ->
                    yield! decls
                    yield! collect children
                | RuleSet(_, _, children) -> yield! collect children
                | _ -> () ]
        collect allNodes

    /// Expand @include by resolving mixin body with argument substitution.
    let private expandMixin
        (name: string)
        (args: string list)
        (mixins: IDictionary<string, string list * ZssNode list>)
        : ZssNode list =

        match mixins.TryGetValue name with
        | false, _ -> []
        | true, (parms, body) ->
            let subst =
                List.zip
                    (if parms.Length <= args.Length then parms else parms |> List.truncate args.Length)
                    (if args.Length <= parms.Length then args  else args  |> List.truncate parms.Length)
                |> dict

            let rec applySubst nodes =
                [ for node in nodes do
                    match node with
                    | RuleSet(sel, decls, children) ->
                        let newDecls =
                            decls |> List.map (fun d ->
                                let v =
                                    subst
                                    |> Seq.fold (fun acc kv ->
                                        Regex.Replace(acc, @"\$?" + Regex.Escape(kv.Key), kv.Value)) d.Value
                                { d with Value = v })
                        yield RuleSet(sel, newDecls, applySubst children)
                    | other -> yield other ]
            applySubst body

    let compile (nodes: ZssNode list) : string =
        let sb = StringBuilder()

        // First pass: collect all mixins
        let mixins = Dictionary<string, string list * ZssNode list>()
        let rec collectMixins ns =
            for n in ns do
                match n with
                | Mixin(name, parms, body) -> mixins.[name] <- (parms, body)
                | RuleSet(_, _, children)  -> collectMixins children
                | _ -> ()
        collectMixins nodes

        let rec emitNodes (nodes: ZssNode list) (parent: string) =
            for node in nodes do
                match node with

                | Variable _ | Mixin _ -> ()  // already consumed

                | CssVarExport(name, value) ->
                    sb.AppendLine(sprintf ":root { --%s: %s; }" name value) |> ignore

                | Each(varName, items, body) ->
                    for item in items do
                        let localVars = Dictionary<string, string>(dict [varName, item])
                        let expandedBody =
                            body |> List.map (function
                                | RuleSet(sel, decls, ch) ->
                                    let newSel = sel.Replace("#{$" + varName + "}", item).Replace("$" + varName, item)
                                    let newDecls = decls |> List.map (fun d -> { d with Value = d.Value.Replace("$" + varName, item) })
                                    RuleSet(newSel, newDecls, ch)
                                | other -> other)
                        emitNodes expandedBody parent

                | Responsive(bp, body) ->
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

                | Import path ->
                    sb.AppendLine(sprintf "@import '%s';" path) |> ignore

                | Comment text ->
                    if text.Trim().Length > 0 then
                        sb.AppendLine(sprintf "/* %s */" text) |> ignore

                | Include(name, args) ->
                    let expanded = expandMixin name args mixins
                    emitNodes [ RuleSet(parent, [], expanded) ] ""

                | Extend extSel ->
                    // Emitting @extend as comment — full placeholder support would need 2-pass
                    sb.AppendLine(sprintf "/* @extend %s */" extSel) |> ignore

                | Apply _ -> ()  // handled inside RuleSet context below

                | RawBlock(atRule, content) ->
                    sb.AppendLine(atRule + " {") |> ignore
                    if content.Trim().Length > 0 then sb.AppendLine(content) |> ignore
                    sb.AppendLine("}") |> ignore

                | RuleSet(selector, decls, children) ->
                    let fullSel =
                        if String.IsNullOrEmpty parent then selector
                        elif String.IsNullOrEmpty selector then parent
                        elif selector.StartsWith("&") then parent + selector.Substring(1)
                        elif selector.StartsWith(":") || selector.StartsWith("::") then parent + selector
                        elif selector.StartsWith("@") then selector
                        else parent + " " + selector

                    // Expand @include / @apply in children
                    let expandedChildren =
                        children |> List.collect (fun c ->
                            match c with
                            | Include(name, args) ->
                                let body = expandMixin name args mixins
                                body |> List.choose (function
                                    | RuleSet("", ds, []) -> Some (RuleSet("", ds, []))
                                    | other -> Some other)
                            | Apply cls ->
                                // @apply: inline as comment tokens (utility framework hook)
                                [ RuleSet("", [], []) ]  // no-op placeholder
                            | other -> [other])

                    // Collect all declarations including from @include expansions
                    let allDecls =
                        decls @
                        (expandedChildren |> List.collect (function
                            | RuleSet("", ds, []) -> ds
                            | _ -> []))
                    let nestedRules =
                        expandedChildren |> List.filter (function
                            | RuleSet("", _, []) -> false
                            | RuleSet _ -> true
                            | _ -> false)
                    let otherNodes =
                        expandedChildren |> List.filter (function
                            | RuleSet("", _, []) -> false
                            | RuleSet _ -> false
                            | _ -> true)

                    if String.IsNullOrEmpty fullSel then
                        // bare declarations
                        for d in allDecls do
                            let imp = if d.Important then " !important" else ""
                            sb.AppendLine(sprintf "  %s: %s%s;" d.Property d.Value imp) |> ignore
                    elif fullSel.StartsWith("@") then
                        sb.AppendLine(sprintf "%s {" fullSel) |> ignore
                        for d in allDecls do
                            let imp = if d.Important then " !important" else ""
                            sb.AppendLine(sprintf "  %s: %s%s;" d.Property d.Value imp) |> ignore
                        emitNodes nestedRules ""
                        emitNodes otherNodes  ""
                        sb.AppendLine("}") |> ignore
                    else
                        if allDecls.Length > 0 then
                            sb.AppendLine(sprintf "%s {" fullSel) |> ignore
                            for d in allDecls do
                                let imp = if d.Important then " !important" else ""
                                sb.AppendLine(sprintf "  %s: %s%s;" d.Property d.Value imp) |> ignore
                            sb.AppendLine("}") |> ignore
                        emitNodes nestedRules fullSel
                        emitNodes otherNodes  fullSel

        emitNodes nodes ""
        sb.ToString().Trim()

// ============================================================
// High-level API
// ============================================================

module Processor =

    let processText (source: string) : string =
        Parser.parse source |> Compiler.compile

    let processFile (filePath: string) : string =
        System.IO.File.ReadAllText(filePath) |> processText

    let processFileTo (src: string) (dst: string) : string =
        let css = processFile src
        let dir = System.IO.Path.GetDirectoryName(dst)
        if not (String.IsNullOrEmpty dir) then System.IO.Directory.CreateDirectory(dir) |> ignore
        System.IO.File.WriteAllText(dst, css)
        css

module BundleService =

    let processZssFiles (assetsDir: string) (outputDir: string) : int =
        if not (System.IO.Directory.Exists assetsDir) then 0
        else
            let files = System.IO.Directory.GetFiles(assetsDir, "*.zss", System.IO.SearchOption.AllDirectories)
            if files.Length = 0 then 0
            else
                let outAssets = System.IO.Path.Combine(outputDir, "assets")
                System.IO.Directory.CreateDirectory(outAssets) |> ignore
                let mutable count = 0
                for f in files do
                    try
                        let rel      = System.IO.Path.GetRelativePath(assetsDir, f)
                        let cssRel   = System.IO.Path.ChangeExtension(rel, ".css")
                        let target   = System.IO.Path.Combine(outAssets, cssRel)
                        let targetDir = System.IO.Path.GetDirectoryName(target)
                        if targetDir <> null then System.IO.Directory.CreateDirectory(targetDir) |> ignore
                        Processor.processFileTo f target |> ignore
                        count <- count + 1
                    with ex ->
                        eprintfn "[Zest] ZSS error '%s': %s" f ex.Message
                count
