namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// LiquidConverter — Liquid → Nunjucks converter
// ============================================================
// Converts Shopify/Jekyll Liquid syntax into equivalent Nunjucks so the
// existing Nunjucks engine can render `.liquid` files directly, the same way
// PugConverter and HamlConverter feed the other dialects.
//
// Rationale: Liquid and Nunjucks share the same output syntax, whitespace
// control, and `{% %}` tag model. The Nunjucks engine already implements
// `set/endset`, `raw`, `include`, `for` + `loop` metadata, `loop.cycle`, and
// most Liquid standard filters — a standalone engine would duplicate all of
// it. Conversion keeps one mature renderer instead of two.
//
// Mappings applied (per token):
//   {{ expr }}                 → {{ expr | safe }}  (Liquid never auto-escapes)
//   filter: a, b               → filter(a, b)       (argument syntax)
//   assign / capture           → set / set..endset
//   unless / elsif             → if not (..) / elif
//   case / when / else         → if / elif chain
//   for limit/offset/reversed  → | take(offset, limit) | reverse
//   forloop.*                  → loop.*
//   cycle 'a','b' (in a for)   → loop.cycle('a', 'b')
//   comment / endcomment       → {# .. #}
//   a contains b               → a | contains(b)
//   (a..b)                     → range(a, b + 1)
//   nil                        → null
//
// Deliberately NOT supported (Nunjucks semantics cannot express them; rare in
// real themes): increment/decrement state, tablerow, forloop.parentloop,
// break/continue. Unknown tags are left untouched so they fail loudly at
// render time instead of silently producing wrong output.
//
// Filter aliases and new filters (`take`, `contains`, `split`, `downcase`,
// ...) are registered on the Nunjucks engine by FilterRegistry.
// ============================================================

module LiquidConverter =

    // ── Module-level compiled regexes ──
    let private reRange      = Regex(@"\((\d+)\s*\.\.\s*(\d+)\)", RegexOptions.Compiled)
    let private reNil        = Regex(@"\bnil\b", RegexOptions.Compiled)
    let private reNotContains= Regex(@"([A-Za-z_][\w.]*)\s+not\s+contains\s+([^\s]+)", RegexOptions.Compiled)
    let private reContains   = Regex(@"([A-Za-z_][\w.]*)\s+contains\s+([^\s]+)", RegexOptions.Compiled)
    let private reEmptyNe    = Regex(@"(\S+)\s*!=\s*empty\b", RegexOptions.Compiled)
    let private reEmptyEq    = Regex(@"(\S+)\s*==\s*empty\b", RegexOptions.Compiled)
    let private reFilterArg  = Regex(@"^\s*([A-Za-z_][\w]*)\s*:(.*)$", RegexOptions.Compiled)
    let private reGroupName  = Regex(@"^([A-Za-z_][\w]*)\s*:\s*(.+)$", RegexOptions.Compiled)

    // ── Quote-aware top-level splitting ─────────────────────────
    /// Split on a separator char, ignoring occurrences inside quotes,
    /// parentheses, and brackets (used for filter chains and arg lists).
    let private splitTopLevel (sep: char) (s: string) : string list =
        let res = ResizeArray<string>()
        let sb = StringBuilder()
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        for c in s do
            if inS then
                sb.Append(c) |> ignore
                if c = '\'' then inS <- false
            elif inD then
                sb.Append(c) |> ignore
                if c = '"' then inD <- false
            elif c = '\'' then inS <- true; sb.Append(c) |> ignore
            elif c = '"' then inD <- true; sb.Append(c) |> ignore
            elif c = '(' || c = '[' then depth <- depth + 1; sb.Append(c) |> ignore
            elif c = ')' || c = ']' then depth <- depth - 1; sb.Append(c) |> ignore
            elif c = sep && depth = 0 then res.Add(sb.ToString().Trim()); sb.Clear() |> ignore
            else sb.Append(c) |> ignore
        if sb.Length > 0 then res.Add(sb.ToString().Trim())
        List.ofSeq res

    /// Position of the first top-level `=` (for `assign`), or -1.
    let private findTopEq (s: string) : int =
        let mutable inS = false
        let mutable inD = false
        let mutable depth = 0
        let mutable i = 0
        let mutable found = -1
        while i < s.Length && found < 0 do
            let c = s.[i]
            if inS then (if c = '\'' then inS <- false)
            elif inD then (if c = '"' then inD <- false)
            elif c = '\'' then inS <- true
            elif c = '"' then inD <- true
            elif c = '(' || c = '[' then depth <- depth + 1
            elif c = ')' || c = ']' then depth <- depth - 1
            elif c = '=' && depth = 0 then found <- i
            i <- i + 1
        found

    // ── Expression rewrites ──────────────────────────────────────
    /// Map a Liquid filter name to its Nunjucks equivalent. Unknown names
    /// pass through unchanged (they may be registered custom filters).
    let private filterAlias (name: string) : string =
        match name.ToLowerInvariant() with
        | "downcase" -> "lower"
        | "upcase" -> "upper"
        | "size" -> "length"
        | "group_by" -> "groupby"
        | "strip_html" -> "striptags"
        | "escape_once" -> "escape"
        | "raw" -> "safe"
        | "newline_to_br" -> "nl2br"
        | n -> n

    /// Rewrite Liquid filter argument syntax `name: a, b` → `name(a, b)`.
    let private rewriteFilters (expr: string) : string =
        match splitTopLevel '|' expr with
        | [] -> expr
        | first :: rest ->
            let sb = StringBuilder(first)
            for seg in rest do
                let seg = seg.Trim()
                let m = reFilterArg.Match seg
                if m.Success then
                    let rawName = m.Groups.[1].Value
                    let alias = filterAlias rawName
                    let args = m.Groups.[2].Value |> splitTopLevel ',' |> List.map (fun a -> a.Trim())
                    if alias = "selectattr" && rawName.ToLowerInvariant() = "where" then
                        // where: "attr" → selectattr(attr); where: "attr", "v" →
                        // selectattr(attr, "equalto", v)
                        let selectArgs =
                            match args with
                            | [a] -> a
                            | a :: v :: _ -> sprintf "%s, \"equalto\", %s" a v
                            | [] -> ""
                        sb.Append(" | selectattr(").Append(selectArgs).Append(")") |> ignore
                    else
                        sb.Append(" | ").Append(alias).Append("(").Append(String.concat ", " args).Append(")") |> ignore
                else
                    // Bare filter (no args): map known aliases, keep the rest.
                    let bareAlias = filterAlias seg
                    if bareAlias = seg then
                        sb.Append(" | ").Append(seg) |> ignore
                    else
                        sb.Append(" | ").Append(bareAlias).Append("()") |> ignore
            sb.ToString()

    /// Shared expression normalization: ranges, forloop, nil, filter args.
    let private rewriteExpr (expr: string) : string =
        let step1 = reRange.Replace(expr, fun m -> sprintf "range(%s, %d)" m.Groups.[1].Value (int m.Groups.[2].Value + 1))
        let step2 = step1.Replace("forloop.", "loop.")
        // Liquid rindex/rindex0 (1-based from the end) == Nunjucks revindex/revindex0.
        let step3 = step2.Replace("loop.rindex0", "loop.revindex0").Replace("loop.rindex", "loop.revindex")
        let step4 = reNil.Replace(step3, "null")
        rewriteFilters step4

    /// Condition rewrites: `contains` / `not contains` / `== empty`.
    let private rewriteCond (cond: string) : string =
        let s1 = rewriteExpr cond
        let s2 = reEmptyNe.Replace(s1, "$1 | length != 0")
        let s3 = reEmptyEq.Replace(s2, "$1 | length == 0")
        let s4 = reNotContains.Replace(s3, "$1 | contains($2) == false")
        reContains.Replace(s4, "$1 | contains($2)")

    /// Quote unquoted `cycle` args (Liquid treats them as string literals).
    let private quoteCycleArg (a: string) : string =
        let a = a.Trim()
        if a.Length >= 2 && (a.StartsWith("'") && a.EndsWith("'") || a.StartsWith("\"") && a.EndsWith("\"")) then a
        elif Regex.IsMatch(a, @"^-?\d+(\.\d+)?$") then a
        elif a = "true" || a = "false" then a
        elif a = "nil" then "null"
        else "'" + a.Replace("'", "\\'") + "'"

    // ── Tokenizer ────────────────────────────────────────────────
    type Token =
        | TText of string
        | TOutput of bool * string * bool   // lstrip, expr, rstrip
        | TTag of bool * string * bool      // lstrip, body, rstrip

    let private reRawClose     = Regex(@"\{%-?\s*endraw\s*-?%\}", RegexOptions.Compiled)
    let private reCmtClose     = Regex(@"\{%-?\s*endcomment\s*-?%\}", RegexOptions.Compiled)
    let private reLiquidClose  = Regex(@"\{%-?\s*endliquid\s*-?%\}", RegexOptions.Compiled)

    let private tokenize (text: string) : Token list =
        let tokens = ResizeArray<Token>()
        let mutable i = 0
        let n = text.Length
        while i < n do
            let o1 = text.IndexOf("{{", i, StringComparison.Ordinal)
            let o2 = text.IndexOf("{%", i, StringComparison.Ordinal)
            let start, isOutput =
                match o1, o2 with
                | -1, -1 -> -1, true
                | -1, b  -> b, false
                | a, -1  -> a, true
                | a, b   -> if a < b then a, true else b, false
            if start < 0 then
                if i < n then tokens.Add(TText(text.Substring(i)))
                i <- n
            else
                if start > i then tokens.Add(TText(text.Substring(i, start - i)))
                let mutable cursor = start + 2
                let mutable lstrip = false
                if cursor < n && text.[cursor] = '-' then lstrip <- true; cursor <- cursor + 1
                let closer = if isOutput then "}}" else "%}"
                let e = text.IndexOf(closer, cursor, StringComparison.Ordinal)
                if e < 0 then
                    tokens.Add(TText(text.Substring(start)))
                    i <- n
                else
                    let mutable rstrip = false
                    let innerEnd = if e >= 1 && text.[e-1] = '-' then (rstrip <- true; e - 1) else e
                    let body = if innerEnd > cursor then text.Substring(cursor, innerEnd - cursor).Trim() else ""
                    if isOutput then
                        tokens.Add(TOutput(lstrip, body, rstrip))
                        i <- e + 2
                    elif body = "raw" then
                        // Nunjucks has its own raw tag — pass the whole span through verbatim.
                        let m = reRawClose.Match(text, e + 2)
                        if m.Success then
                            tokens.Add(TText(text.Substring(start, m.Index + m.Length - start)))
                            i <- m.Index + m.Length
                        else
                            tokens.Add(TTag(lstrip, body, rstrip)); i <- e + 2
                    elif body = "comment" then
                        // Nunjucks comments are {# .. #}; strip the tag wrapper.
                        let m = reCmtClose.Match(text, e + 2)
                        if m.Success then
                            let content = text.Substring(e + 2, m.Index - (e + 2))
                            tokens.Add(TText("{# " + content.Trim() + " #}"))
                            i <- m.Index + m.Length
                        else
                            tokens.Add(TTag(lstrip, body, rstrip)); i <- e + 2
                    elif body = "liquid" then
                        // {% liquid %} tag: capture the body, prefixed so the
                        // conversion walker can re-parse each line.
                        let m = reLiquidClose.Match(text, e + 2)
                        if m.Success then
                            tokens.Add(TTag(lstrip, "liquid:" + text.Substring(e + 2, m.Index - (e + 2)), rstrip))
                            i <- m.Index + m.Length
                        else
                            tokens.Add(TTag(lstrip, body, rstrip)); i <- e + 2
                    else
                        tokens.Add(TTag(lstrip, body, rstrip))
                        i <- e + 2
        Seq.toList tokens

    // ── Conversion walker ────────────────────────────────────────
    /// Convert a Liquid template string to Nunjucks syntax.
    let convert (liquid: string) : string =
        TemplateUtils.cachedConvert liquid (fun liquid ->
            if String.IsNullOrEmpty liquid then ""
            else
                let tokens = tokenize liquid
                let sb = StringBuilder()
                // Tag-context stack: "if" | "for" | "case" | "capture".
                let ctxStack = ResizeArray<string>()
                // Case frames: (case expression, whether the opening if was emitted).
                let caseFrames = ResizeArray<string * bool>()

                let inForBody () = ctxStack |> Seq.contains "for"

                // Handle one Liquid tag line. Used for top-level tokens and for
                // each line inside a {% liquid %} block.
                let rec convertTagBody (l: bool) (r: bool) (body: string) : unit =
                    let words =
                        body.Split([|' '; '\t'; '\n'; '\r'|], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.toList
                    let name = if words.IsEmpty then "" else words.Head
                    let rest = if words.IsEmpty then "" else String.Join(" ", words.Tail)
                    let emitTag (inner: string) =
                        sb.Append("{%") |> ignore
                        if l then sb.Append("-") |> ignore
                        sb.Append(' ') |> ignore
                        sb.Append(inner) |> ignore
                        sb.Append(' ') |> ignore
                        if r then sb.Append("-") |> ignore
                        sb.Append("%}") |> ignore
                    let emitOutput (inner: string) =
                        sb.Append("{{") |> ignore
                        if l then sb.Append("-") |> ignore
                        sb.Append(' ') |> ignore
                        sb.Append(inner) |> ignore
                        sb.Append(' ') |> ignore
                        if r then sb.Append("-") |> ignore
                        sb.Append("}}") |> ignore
                    let popContext (expected: string) =
                        if ctxStack.Count > 0 && ctxStack.[ctxStack.Count-1] = expected then
                            ctxStack.RemoveAt(ctxStack.Count-1)

                    // {% liquid %} block: each non-empty line is a tag or output.
                    if name.StartsWith("liquid:") then
                        let block = body.Substring("liquid:".Length)
                        for line in block.Split('\n') do
                            let lt = line.Trim()
                            if lt <> "" then
                                if lt.StartsWith("{%") && lt.EndsWith("%}") then
                                    convertTagBody false false (lt.Substring(2, lt.Length - 4).Trim())
                                elif lt.StartsWith("{{") && lt.EndsWith("}}") then
                                    let expr = lt.Substring(2, lt.Length - 4).Trim()
                                    sb.Append("{{ ").Append(rewriteExpr expr).Append(" | safe }}") |> ignore
                                else
                                    convertTagBody false false lt
                    else
                        match name with
                        | "" -> ()

                        // ── assignment / capture → set ───────────────
                        | "assign" ->
                            let afterTag = body.Substring("assign".Length).Trim()
                            let eq = findTopEq afterTag
                            if eq > 0 then
                                let varName = afterTag.Substring(0, eq).Trim()
                                let expr = afterTag.Substring(eq + 1).Trim() |> rewriteExpr
                                emitTag (sprintf "set %s = %s" varName expr)
                            else emitTag body
                        | "capture" when words.Length >= 2 ->
                            emitTag ("set " + words.[1]); ctxStack.Add("capture")
                        | "endcapture" ->
                            popContext "capture"; emitTag "endset"

                        // ── conditions ───────────────────────────────
                        | "if" ->
                            emitTag ("if " + rewriteCond rest); ctxStack.Add("if")
                        | "endif" ->
                            popContext "if"; emitTag "endif"
                        | "unless" ->
                            emitTag ("if not (" + rewriteCond rest + ")"); ctxStack.Add("if")
                        | "endunless" ->
                            popContext "if"; emitTag "endif"
                        | "elsif" -> emitTag ("elif " + rewriteCond rest)
                        | "else" -> emitTag "else"

                        // ── case / when → if / elif chain ────────────
                        | "case" ->
                            caseFrames.Add(rewriteExpr rest, false); ctxStack.Add("case")
                        | "when" when ctxStack.Count > 0 && ctxStack.[ctxStack.Count-1] = "case" ->
                            let caseExpr, emitted = caseFrames.[caseFrames.Count-1]
                            let conds =
                                splitTopLevel ',' rest
                                |> List.map (fun a -> sprintf "(%s == %s)" caseExpr (a.Trim()))
                                |> String.concat " or "
                            if emitted then emitTag ("elif " + conds)
                            else emitTag ("if " + conds); caseFrames.[caseFrames.Count-1] <- (caseExpr, true)
                        | "endcase" ->
                            if caseFrames.Count > 0 then caseFrames.RemoveAt(caseFrames.Count-1)
                            popContext "case"; emitTag "endif"

                        // ── for loops (with limit/offset/reversed) ───
                        | "for" when words.Length >= 4 && words.[2] = "in" ->
                            let mutable limit = None
                            let mutable offset = None
                            let mutable reversed = false
                            let colParts = ResizeArray<string>()
                            for w in words |> List.skip 3 do
                                if w.StartsWith("limit:", StringComparison.OrdinalIgnoreCase) then
                                    limit <- (try Some(int (w.Substring(6))) with _ -> None)
                                elif w.StartsWith("offset:", StringComparison.OrdinalIgnoreCase) then
                                    offset <- (try Some(int (w.Substring(7))) with _ -> None)
                                elif w.Equals("reversed", StringComparison.OrdinalIgnoreCase) then
                                    reversed <- true
                                else colParts.Add(w)
                            let mutable col = String.concat " " colParts |> rewriteExpr
                            if offset.IsSome || limit.IsSome then
                                let off = defaultArg offset 0
                                col <- col + sprintf " | take(%d%s)" off (if limit.IsSome then ", " + string limit.Value else "")
                            if reversed then col <- col + " | reverse"
                            emitTag (sprintf "for %s in %s" words.[1] col)
                            ctxStack.Add("for")
                        | "endfor" ->
                            popContext "for"; emitTag "endfor"

                        // ── include / render (with / for forms) ──────
                        | "include" when words.Length >= 2 ->
                            match words.Tail |> List.tryFindIndex (fun w -> w = "for") with
                            | Some fi when fi >= 1 && fi + 2 < words.Tail.Length ->
                                let incWords = words.Tail
                                let varName = incWords.[fi+1]
                                let col = String.concat " " (incWords |> List.skip (fi + 3)) |> rewriteExpr
                                emitTag (sprintf "for %s in %s" varName col)
                                emitTag ("include " + incWords.[0])
                                emitTag "endfor"
                            | _ ->
                                // `with x` drops away: the Nunjucks include passes the full
                                // context, so the variable is already visible inside.
                                emitTag ("include " + words.Tail.Head)
                        | "render" when words.Length >= 2 ->
                            // Liquid's render isolates scope; the Nunjucks include
                            // shares the full context — close enough for static sites.
                            emitTag ("include " + words.[1])

                        // ── cycle → loop.cycle (inside a for body) ───
                        | "cycle" when inForBody () ->
                            let afterTag = body.Substring("cycle".Length).Trim()
                            let argsText =
                                let m = reGroupName.Match afterTag
                                if m.Success then m.Groups.[2].Value else afterTag
                            let values =
                                splitTopLevel ',' argsText
                                |> List.map quoteCycleArg
                                |> String.concat ", "
                            emitOutput (sprintf "loop.cycle(%s)" values)

                        // ── everything else passes through unchanged ──
                        // Unknown tags (increment/decrement/tablerow/...)
                        // fail loudly at render time — see module docs.
                        | _ -> emitTag body

                for tok in tokens do
                    match tok with
                    | TText t -> sb.Append(t) |> ignore

                    | TOutput(l, expr, r) ->
                        // Liquid never auto-escapes; Nunjucks does. `| safe` restores
                        // the raw-output contract, and is idempotent if already safe.
                        sb.Append("{{") |> ignore
                        if l then sb.Append("-") |> ignore
                        sb.Append(' ') |> ignore
                        sb.Append(rewriteExpr expr) |> ignore
                        sb.Append(" | safe") |> ignore
                        sb.Append(' ') |> ignore
                        if r then sb.Append("-") |> ignore
                        sb.Append("}}") |> ignore

                    | TTag(l, body, r) -> convertTagBody l r body

                sb.ToString())
