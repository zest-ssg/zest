namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZSS Parser — Indentation-mode (Python-style)
// ============================================================

module ParserIndent =

    open ParserCore

    let rec parseIndentBlock (startIdx: int) (lines: string array) (baseIndent: int) (vars: IDictionary<string, string>) : ZssNode list * int =
        let nodes = ResizeArray<ZssNode>()
        let mutable i = startIdx
        let mutable stop = false

        while i < lines.Length && not stop do
            let line = lines.[i]
            let t = line.TrimStart()
            let lineNum = i + 1
            let indent = getIndent line

            if String.IsNullOrWhiteSpace t then
                i <- i + 1

            // If indent is less than base, we're done with this block
            elif indent < baseIndent then
                stop <- true

            // If indent is greater than base, it's a child (shouldn't happen here, but skip)
            elif indent > baseIndent && nodes.Count = 0 then
                i <- i + 1  // skip orphan indented lines

            elif indent > baseIndent then
                // This shouldn't happen at this level — skip
                i <- i + 1

            else
                // Same indent level — parse this line
                // Variable ($name: value)
                if varPattern.IsMatch(t) then
                    let m = varPattern.Match(t)
                    let isDefault = m.Groups.[3].Success
                    let v = resolveVars (m.Groups.[2].Value.Trim()) vars
                    if isDefault then
                        if not (vars.ContainsKey(m.Groups.[1].Value)) then vars.[m.Groups.[1].Value] <- v
                    else
                        vars.[m.Groups.[1].Value] <- v
                    nodes.Add(Variable(m.Groups.[1].Value, v, isDefault, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // F#-style: let name = value
                elif letPattern.IsMatch(t) then
                    let m = letPattern.Match(t)
                    let name = m.Groups.[1].Value
                    let v = resolveVars (m.Groups.[2].Value.Trim()) vars
                    vars.[name] <- v
                    nodes.Add(Variable(name, v, false, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @mixin
                elif t.StartsWith("@mixin") then
                    let m = mixinDefPat.Match(t)
                    if m.Success then
                        let name = m.Groups.[1].Value
                        let parms = parseMixinParams m
                        i <- i + 1
                        // Find child indent
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let body, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(Mixin(name, parms, body, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(Mixin(name, parms, [], { Line = lineNum; Col = indent + 1 }))

                // @include with content
                elif t.StartsWith("@include") then
                    let m = includePat.Match(t)
                    if m.Success then
                        let name = m.Groups.[1].Value
                        let args = parseIncludeArgs m vars
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let content, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(Include(name, args, content, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(Include(name, args, [], { Line = lineNum; Col = indent + 1 }))

                // @extend
                elif t.StartsWith("@extend") then
                    let m = extendPat.Match(t)
                    if m.Success then nodes.Add(Extend(m.Groups.[1].Value, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @apply
                elif t.StartsWith("@apply") then
                    let m = applyPat.Match(t)
                    if m.Success then
                        let cls = m.Groups.[1].Value.Split([|' ';','|], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
                        nodes.Add(Apply(cls, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @import
                elif t.StartsWith("@import") then
                    let m = importPat.Match(t)
                    if m.Success then nodes.Add(Import(m.Groups.[1].Value, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @use
                elif t.StartsWith("@use") then
                    let m = usePat.Match(t)
                    if m.Success then
                        let alias = if m.Groups.[2].Success then Some m.Groups.[2].Value else None
                        nodes.Add(Use(m.Groups.[1].Value, alias, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @export
                elif t.StartsWith("@export") then
                    let m = exportPat.Match(t)
                    if m.Success then
                        let vname = m.Groups.[1].Value
                        match vars.TryGetValue(vname) with
                        | true, vval -> nodes.Add(CssVarExport(vname, vval, { Line = lineNum; Col = indent + 1 }))
                        | _ -> ()
                    i <- i + 1

                // @each
                elif t.StartsWith("@each") then
                    let m = eachPat.Match(t)
                    let mm = eachMapPat.Match(t)
                    if mm.Success then
                        let keyVar = mm.Groups.[1].Value
                        let valVar = mm.Groups.[2].Value
                        let mapName = mm.Groups.[3].Value
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let body, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(EachMap(keyVar, valVar, mapName, body, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(EachMap(keyVar, valVar, mapName, [], { Line = lineNum; Col = indent + 1 }))
                    elif m.Success then
                        let varName = m.Groups.[1].Value
                        let items = m.Groups.[3].Value.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let body, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(Each(varName, items, body, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(Each(varName, items, [], { Line = lineNum; Col = indent + 1 }))
                    else i <- i + 1

                // @for
                elif t.StartsWith("@for") then
                    let m = forPat.Match(t)
                    if m.Success then
                        let varName = m.Groups.[1].Value
                        let from = int m.Groups.[2].Value
                        let through = int m.Groups.[4].Value
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let body, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(For(varName, from, through, body, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(For(varName, from, through, [], { Line = lineNum; Col = indent + 1 }))
                    else i <- i + 1

                // @if / @else
                elif t.StartsWith("@if") then
                    let m = ifPat.Match(t)
                    if m.Success then
                        let cond = m.Groups.[1].Value.Trim()
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        let body, newI =
                            if childIndent > baseIndent then parseIndentBlock i lines childIndent vars
                            else ([], i)
                        i <- newI
                        // Check for @else
                        let mutable elseBody = None
                        if i < lines.Length then
                            let nextLine = lines.[i].TrimStart()
                            let nextIndent = getIndent lines.[i]
                            if nextIndent = baseIndent && nextLine.StartsWith("@else") then
                                i <- i + 1
                                let elseChildIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                                let eb, newI2 =
                                    if elseChildIndent > baseIndent then parseIndentBlock i lines elseChildIndent vars
                                    else ([], i)
                                i <- newI2
                                elseBody <- Some eb
                        nodes.Add(If(cond, body, elseBody, { Line = lineNum; Col = indent + 1 }))
                    else i <- i + 1

                // @content
                elif t.StartsWith("@content") then
                    nodes.Add(Content({ Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @option
                elif t.StartsWith("@option") then
                    let m = optionPat.Match(t)
                    if m.Success then nodes.Add(Option(m.Groups.[1].Value, m.Groups.[2].Value.Trim(), { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // @warn / @debug
                elif t.StartsWith("@warn") then
                    let m = warnPat.Match(t)
                    if m.Success then nodes.Add(Warn(m.Groups.[1].Value, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1
                elif t.StartsWith("@debug") then
                    let m = debugPat.Match(t)
                    if m.Success then nodes.Add(Debug(m.Groups.[1].Value, { Line = lineNum; Col = indent + 1 }))
                    i <- i + 1

                // Responsive shorthand
                elif t.StartsWith("@") && rspBpMap.ContainsKey(t.TrimEnd('{', ' ').Trim().TrimStart('@')) then
                    let key = t.TrimEnd('{', ' ').Trim().TrimStart('@')
                    i <- i + 1
                    let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                    if childIndent > baseIndent then
                        let body, newI = parseIndentBlock i lines childIndent vars
                        i <- newI
                        nodes.Add(Responsive(key, body, { Line = lineNum; Col = indent + 1 }))
                    else
                        nodes.Add(Responsive(key, [], { Line = lineNum; Col = indent + 1 }))

                // Generic at-rule
                elif t.StartsWith("@") then
                    let atRuleStr = t.TrimEnd('{', ' ').Trim()
                    let hasBrace = t.Contains("{")
                    i <- i + 1
                    let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                    let body, newI =
                        if hasBrace then
                            // Brace-syntax body (e.g. @keyframes, @media with braces)
                            // Use brace-mode parsing for the body
                            ParserBrace.parseBraceBlock i lines vars
                        elif childIndent > baseIndent then
                            parseIndentBlock i lines childIndent vars
                        else ([], i)
                    i <- newI
                    let parts = atRuleStr.Split([|' '|], 2)
                    let atName = parts.[0]
                    let atParams = if parts.Length > 1 then parts.[1] else ""
                    nodes.Add(AtRule(atName, atParams, body, { Line = lineNum; Col = indent + 1 }))

                // Rule set (selector line) — children are indented
                elif t.Contains(":") || t.Contains("=") then
                    // Could be a declaration or a selector
                    // Heuristic: if it looks like `prop: value` or `prop = value`, it's a declaration
                    // If it has a CSS selector pattern (starts with ., #, tag, etc.), it's a rule
                    let isSelector =
                        t.StartsWith(".") || t.StartsWith("#") || t.StartsWith("&") ||
                        t.StartsWith("*") || t.StartsWith("[") || t.StartsWith(":") ||
                        (Regex.IsMatch(t, @"^[a-zA-Z][\w-]*\s*[,.>+~:&\[ ]") && not (t.Contains(": ") || t.Contains("=")))

                    if isSelector then
                        let selector = t.Trim()
                        i <- i + 1
                        let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                        if childIndent > baseIndent then
                            let children, newI = parseIndentBlock i lines childIndent vars
                            i <- newI
                            nodes.Add(RuleSet(selector, [], children, { Line = lineNum; Col = indent + 1 }))
                        else
                            nodes.Add(RuleSet(selector, [], [], { Line = lineNum; Col = indent + 1 }))
                    else
                        // Declaration
                        match parseDecl t lineNum vars with
                        | Some d -> nodes.Add(RuleSet("", [d], [], { Line = lineNum; Col = indent + 1 }))
                        | None -> ()
                        i <- i + 1

                // Selector without colon (e.g., `.button`, `nav a`)
                else
                    let selector = t.Trim()
                    i <- i + 1
                    let childIndent = if i < lines.Length then getIndent lines.[i] else baseIndent
                    if childIndent > baseIndent then
                        let children, newI = parseIndentBlock i lines childIndent vars
                        i <- newI
                        nodes.Add(RuleSet(selector, [], children, { Line = lineNum; Col = indent + 1 }))
                    else
                        nodes.Add(RuleSet(selector, [], [], { Line = lineNum; Col = indent + 1 }))

        (Seq.toList nodes, i)
