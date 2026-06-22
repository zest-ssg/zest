namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZSS Parser — Brace-mode (CSS/SCSS style)
// ============================================================

module ParserBrace =

    open ParserCore

    let rec parseBraceBlock (startIdx: int) (lines: string array) (vars: IDictionary<string, string>) : ZssNode list * int =
        let nodes = ResizeArray<ZssNode>()
        let mutable i = startIdx
        let mutable stop = false

        while i < lines.Length && not stop do
            let line = lines.[i].TrimEnd()
            let t = line.TrimStart()
            let lineNum = i + 1

            if String.IsNullOrWhiteSpace t then
                i <- i + 1

            elif t.StartsWith("}") then
                i <- i + 1
                stop <- true

            // Variable ($name: value)
            elif varPattern.IsMatch(t) then
                let m = varPattern.Match(t)
                let isDefault = m.Groups.[3].Success
                let rawV = resolveVars (m.Groups.[2].Value.Trim()) vars
                let v = Evaluator.resolveValue rawV vars
                if isDefault then
                    if not (vars.ContainsKey(m.Groups.[1].Value)) then vars.[m.Groups.[1].Value] <- v
                else
                    vars.[m.Groups.[1].Value] <- v
                nodes.Add(Variable(m.Groups.[1].Value, v, isDefault, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // F#-style: let name = value
            elif letPattern.IsMatch(t) then
                let m = letPattern.Match(t)
                let name = m.Groups.[1].Value
                let rawV = resolveVars (m.Groups.[2].Value.Trim()) vars
                let v = Evaluator.resolveValue rawV vars
                vars.[name] <- v
                nodes.Add(Variable(name, v, false, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @mixin
            elif t.StartsWith("@mixin") then
                let m = mixinDefPat.Match(t)
                if m.Success then
                    let name = m.Groups.[1].Value
                    let parms = parseMixinParams m
                    let hasBrace = t.Contains("{")
                    i <- i + 1
                    let body, newI = parseBraceBlock i lines vars
                    i <- newI
                    nodes.Add(Mixin(name, parms, body, { Line = lineNum; Col = 1 }))
                else i <- i + 1

            // @include with optional content block
            elif t.StartsWith("@include") then
                let m = includePat.Match(t)
                if m.Success then
                    let name = m.Groups.[1].Value
                    let args = parseIncludeArgs m vars
                    // Check if there's a content block on next lines
                    let hasBrace = t.Contains("{")
                    if hasBrace then
                        i <- i + 1
                        let content, newI = parseBraceBlock i lines vars
                        i <- newI
                        nodes.Add(Include(name, args, content, { Line = lineNum; Col = 1 }))
                    else
                        nodes.Add(Include(name, args, [], { Line = lineNum; Col = 1 }))
                        i <- i + 1
                else i <- i + 1

            // @extend
            elif t.StartsWith("@extend") then
                let m = extendPat.Match(t)
                if m.Success then nodes.Add(Extend(m.Groups.[1].Value, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @apply
            elif t.StartsWith("@apply") then
                let m = applyPat.Match(t)
                if m.Success then
                    let cls = m.Groups.[1].Value.Split([|' ';','|], StringSplitOptions.RemoveEmptyEntries) |> Array.toList
                    nodes.Add(Apply(cls, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @import
            elif t.StartsWith("@import") then
                let m = importPat.Match(t)
                if m.Success then nodes.Add(Import(m.Groups.[1].Value, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @use
            elif t.StartsWith("@use") then
                let m = usePat.Match(t)
                if m.Success then
                    let alias = if m.Groups.[2].Success then Some m.Groups.[2].Value else None
                    nodes.Add(Use(m.Groups.[1].Value, alias, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @export
            elif t.StartsWith("@export") then
                let m = exportPat.Match(t)
                if m.Success then
                    let vname = m.Groups.[1].Value
                    match vars.TryGetValue(vname) with
                    | true, vval -> nodes.Add(CssVarExport(vname, vval, { Line = lineNum; Col = 1 }))
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
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBraceBlock i lines vars
                    i <- newI
                    nodes.Add(EachMap(keyVar, valVar, mapName, body, { Line = lineNum; Col = 1 }))
                elif m.Success then
                    let varName = m.Groups.[1].Value
                    let items = m.Groups.[3].Value.Split(',') |> Array.map (fun s -> s.Trim()) |> Array.toList
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBraceBlock i lines vars
                    i <- newI
                    nodes.Add(Each(varName, items, body, { Line = lineNum; Col = 1 }))
                else i <- i + 1

            // @for
            elif t.StartsWith("@for") then
                let m = forPat.Match(t)
                if m.Success then
                    let varName = m.Groups.[1].Value
                    let from = int m.Groups.[2].Value
                    let through = int m.Groups.[4].Value
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBraceBlock i lines vars
                    i <- newI
                    nodes.Add(For(varName, from, through, body, { Line = lineNum; Col = 1 }))
                else i <- i + 1

            // @if / @else
            elif t.StartsWith("@if") then
                let m = ifPat.Match(t)
                if m.Success then
                    let cond = m.Groups.[1].Value.Trim()
                    let hasBrace = t.Contains("{")
                    i <- if hasBrace then i + 1 else i + 1
                    let body, newI = parseBraceBlock i lines vars
                    i <- newI
                    // Check for @else
                    let mutable elseBody = None
                    if i < lines.Length then
                        let nextLine = lines.[i].TrimStart()
                        if nextLine.StartsWith("@else") then
                            let em = elsePat.Match(nextLine)
                            let hasBrace2 = nextLine.Contains("{")
                            i <- if hasBrace2 then i + 1 else i + 1
                            let eb, newI2 = parseBraceBlock i lines vars
                            i <- newI2
                            elseBody <- Some eb
                    nodes.Add(If(cond, body, elseBody, { Line = lineNum; Col = 1 }))
                else i <- i + 1

            // @content
            elif t.StartsWith("@content") then
                nodes.Add(Content({ Line = lineNum; Col = 1 }))
                i <- i + 1

            // @option
            elif t.StartsWith("@option") then
                let m = optionPat.Match(t)
                if m.Success then nodes.Add(Option(m.Groups.[1].Value, m.Groups.[2].Value.Trim(), { Line = lineNum; Col = 1 }))
                i <- i + 1

            // @warn / @debug
            elif t.StartsWith("@warn") then
                let m = warnPat.Match(t)
                if m.Success then nodes.Add(Warn(m.Groups.[1].Value, { Line = lineNum; Col = 1 }))
                i <- i + 1
            elif t.StartsWith("@debug") then
                let m = debugPat.Match(t)
                if m.Success then nodes.Add(Debug(m.Groups.[1].Value, { Line = lineNum; Col = 1 }))
                i <- i + 1

            // Responsive shorthand: @sm, @md, @lg, @xl, @2xl
            elif t.StartsWith("@") && rspBpMap.ContainsKey(t.TrimEnd('{', ' ').Trim().TrimStart('@')) then
                let key = t.TrimEnd('{', ' ').Trim().TrimStart('@')
                let hasBrace = t.Contains("{")
                i <- if hasBrace then i + 1 else i + 1
                let body, newI = parseBraceBlock i lines vars
                i <- newI
                nodes.Add(Responsive(key, body, { Line = lineNum; Col = 1 }))

            // Generic at-rule (@media, @keyframes, @supports, etc.)
            elif t.StartsWith("@") then
                let atRuleStr = t.TrimEnd('{', ' ').Trim()
                let hasBrace = t.Contains("{")
                i <- if hasBrace then i + 1 else i + 1
                let body, newI = parseBraceBlock i lines vars
                i <- newI
                // Parse at-rule name and params
                let parts = atRuleStr.Split([|' '|], 2)
                let atName = parts.[0]
                let atParams = if parts.Length > 1 then parts.[1] else ""
                nodes.Add(AtRule(atName, atParams, body, { Line = lineNum; Col = 1 }))

            // Rule set with opening brace
            elif t.Contains("{") then
                let braceIdx = t.IndexOf("{")
                let selector = t.Substring(0, braceIdx).Trim()
                let afterBrace = t.Substring(braceIdx + 1).Trim()
                let inlineClosed = afterBrace.Contains("}")
                let inlineText = if inlineClosed then afterBrace.Substring(0, afterBrace.IndexOf("}")) else afterBrace

                let inlineDecls =
                    inlineText.Split(';')
                    |> Array.choose (fun d ->
                        let s = d.Trim()
                        if s.Length > 0 then parseDecl (s + ";") lineNum vars else None)
                    |> Array.toList

                if inlineClosed then
                    nodes.Add(RuleSet(selector, inlineDecls, [], { Line = lineNum; Col = 1 }))
                    i <- i + 1
                else
                    i <- i + 1
                    let children, newI = parseBraceBlock i lines vars
                    i <- newI
                    nodes.Add(RuleSet(selector, inlineDecls, children, { Line = lineNum; Col = 1 }))

            // Bare declaration (prop: value or prop = value)
            elif t.Contains(":") || t.Contains("=") then
                match parseDecl t lineNum vars with
                | Some d -> nodes.Add(RuleSet("", [d], [], { Line = lineNum; Col = 1 }))
                | None -> ()
                i <- i + 1

            else
                errors.Add({ Message = sprintf "Unexpected token: %s" t; Line = lineNum; Col = 1; Context = String.Join("\n", lines) })
                i <- i + 1

        (Seq.toList nodes, i)
