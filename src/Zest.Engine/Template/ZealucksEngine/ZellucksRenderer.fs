namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.Text
open NunjucksTypes
open NunjucksTokenizer
open NunjucksEvaluator
open NunjucksBlocks
open NunjucksCompiler

// NunjucksRenderer.fs
//
// Walks a Token array and emits rendered HTML. Implements control-flow tags
// (if/for/set/block/extends/include/macro/call/import/from/filter/with) and
// template inheritance.
//
// Invariant: the recursive core works on a Token[] (O(1) indexing, single
// conversion per render). The list wrapper keeps the public signature unchanged.

module internal NunjucksRenderer =

    // ── RenderEnv ──────────────────────────────────────────
    type RenderEnv = {
        Variables: IDictionary<string, obj>
        LoadTemplate: string * int -> Result<string, string>
        ChildBlocks: IDictionary<string, Token list>   // blocks from child template
        BlockStack: string list                        // currently active block names
        Depth: int
        Macros: IDictionary<string, ((string * string option) list * Token list)>   // macro name → (args with defaults, body)
        Blocks: IDictionary<string, Token list>        // this template's own block defs (for super())
        CurrentBlock: string option                    // block being rendered (for super())
        CallerBody: Token list option                  // captured {% call %} body (for caller())
        LoopNesting: int                               // current for-loop nesting depth
        LastLine: int ref                              // most recently processed source line (for errors)
        ControlFlow: string ref                        // "" | "break" | "continue" (consumed by the nearest for loop)
    }

    /// Bind macro arguments. Positional values fill parameters in declaration
    /// order; any parameter without a value falls back to its default
    /// expression (evaluated in the caller's context) or to null when no
    /// default exists. Returns a fresh context so caller variables are not
    /// mutated.
    let private bindMacroArgs (argDefs: (string * string option) list) (vals: obj list)
                              (baseCtx: IDictionary<string, obj>) : Dictionary<string, obj> =
        let mCtx = Dictionary<string, obj>(baseCtx |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
        let rec zip defs vs =
            match defs with
            | [] -> ()
            | (name, defOpt) :: restDefs ->
                match vs with
                | v :: restVs -> mCtx.[name] <- v; zip restDefs restVs
                | [] ->
                    match defOpt with
                    | Some d -> mCtx.[name] <- evalExpr d baseCtx
                    | None -> mCtx.[name] <- null
                    zip restDefs []
        zip argDefs vals
        mCtx

    // ── Main renderer ──────────────────────────────────────
    // The recursive core works on a Token[] (O(1) indexing, single conversion
    // per render). The list wrapper keeps the public signature unchanged.
    let rec renderTokens (tokens: Token list) (env: RenderEnv) : Result<string, string> =
        renderTokensArr (List.toArray tokens) env

    and renderTokensArr (tokens: Token[]) (env: RenderEnv) : Result<string, string> =
        let sb = StringBuilder()
        let len = tokens.Length
        let mutable idx = 0
        let mutable error: string option = None

        while idx < len && error.IsNone do
            let curLine =
                match tokens.[idx] with
                | TextToken(_, l) | VarToken(_, l) | TagToken(_, _, l) | CmtToken(_, l) -> l
            env.LastLine.Value <- curLine

            match tokens.[idx] with
            | TextToken(t, _) -> sb.Append(t) |> ignore; idx <- idx + 1

            | CmtToken _ -> idx <- idx + 1

            | VarToken(expr, _) ->
                let exprTrim = expr.Trim()
                // {{ super() }} — render the parent's version of the current block.
                if exprTrim.StartsWith("super(") then
                    match env.CurrentBlock with
                    | Some name ->
                        match env.Blocks.TryGetValue name with
                        | true, parentBody ->
                            match renderTokens parentBody { env with CurrentBlock = Some name } with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                        | _ -> ()
                    | None -> ()
                    idx <- idx + 1
                // {{ caller() }} — render the captured {% call %} body.
                elif exprTrim.StartsWith("caller(") then
                    match env.CallerBody with
                    | Some body ->
                        match renderTokens body env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | None -> ()
                    idx <- idx + 1
                else
                    // Check for macro call: macroName(args) — must be a function-like expr
                    let pOpen = exprTrim.IndexOf('(')
                    let mutable macroResult : string option = None
                    if pOpen > 0 && exprTrim.EndsWith(")") then
                        let mName = exprTrim.[..pOpen-1].Trim()
                        match env.Macros.TryGetValue mName with
                        | true, (margDefs, mbody) ->
                            let argsText = exprTrim.[pOpen+1..exprTrim.Length-2].Trim()
                            let argValues =
                                if argsText = "" then []
                                else splitTopLevelArgs argsText |> List.map (fun a -> evalExpr a env.Variables)
                            let mCtx = bindMacroArgs margDefs argValues env.Variables
                            match renderTokens mbody { env with Variables = mCtx :> IDictionary<string, obj> } with
                            | Ok h -> macroResult <- Some h
                            | Error e -> error <- Some e
                        | _ -> ()
                    match macroResult with
                    | Some h ->
                        sb.Append(h) |> ignore
                    | None ->
                        let v = evalExpr expr env.Variables
                        // Auto-escape: strings are escaped unless marked as safe
                        let html =
                            match v with
                            | :? SafeString as ss -> ss.Value
                            | :? string as sv -> HtmlEncode sv
                            | null -> ""
                            | _ -> toStr v
                        sb.Append(html) |> ignore
                    idx <- idx + 1

            | TagToken(tag, args, _) ->
                let isBlock = blockTags.Contains(tag)
                let endIdx =
                    if isBlock then findMatchingEnd (idx+1) tag tokens
                    else idx
                // A block tag whose matching end was not found: report a precise error.
                if isBlock && endIdx >= len && len > idx then
                    error <- Some(sprintf "Unclosed block tag '{%% %s %%}'" tag)
                else
                let bodyTokens =
                    if isBlock && endIdx > idx+1 then tokens.[idx+1..endIdx-1] |> Array.toList
                    else []
                let bodyHtml =
                    if isBlock then
                        match renderTokens bodyTokens env with
                        | Ok h -> Some h
                        | Error e -> error <- Some e; None
                    else None

                match tag with
                | "if" ->
                    // Split the body into (condition, branchTokens) at top-level
                    // elif/else boundaries, then render the first matching branch.
                    let condExpr = args |> String.concat " "
                    let branches = splitIfBranches condExpr (List.toArray bodyTokens)
                    let chosen =
                        branches |> List.tryPick (fun (cond, toks) ->
                            let matched =
                                match cond with
                                | None -> true   // else branch
                                | Some "" -> true
                                | Some e -> toBool (evalExpr e env.Variables)
                            if matched then Some toks else None)
                    match chosen with
                    | Some toks ->
                        match renderTokens toks env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | None -> ()

                | "for" ->
                    let ls = args |> String.concat " "
                    let inIdx = ls.IndexOf(" in ", StringComparison.Ordinal)
                    let loopVar, iterExpr =
                        if inIdx >= 0 then ls.[..inIdx-1].Trim(), ls.[inIdx+4..]
                        else ls, ""
                    let iter = evalExpr iterExpr env.Variables
                    let loopTokens = forLoopBody (List.toArray bodyTokens)
                    // Support "key, value" destructuring for dict/pair iteration.
                    let varNames = loopVar.Split(',') |> Array.map (fun v -> v.Trim())
                    // Bind the iteration variable(s) into the per-iteration context.
                    let bindItem (ctx: Dictionary<string, obj>) (item: obj) =
                        if varNames.Length = 2 then
                            match item with
                            | :? IDictionary<string, obj> as kv ->
                                ctx.[varNames.[0]] <- (match kv.TryGetValue "key" with true, k -> k | _ -> box "")
                                ctx.[varNames.[1]] <- (match kv.TryGetValue "value" with true, v -> v | _ -> box "")
                            | :? System.Collections.IList as pair when pair.Count >= 2 ->
                                ctx.[varNames.[0]] <- pair.[0]
                                ctx.[varNames.[1]] <- pair.[1]
                            // Iterating a dictionary directly yields key/value
                            // pairs, so `for key, value in dict` destructures them.
                            | :? KeyValuePair<string, obj> as kvp ->
                                ctx.[varNames.[0]] <- box kvp.Key
                                ctx.[varNames.[1]] <- kvp.Value
                            | :? System.Collections.DictionaryEntry as de ->
                                ctx.[varNames.[0]] <- de.Key
                                ctx.[varNames.[1]] <- de.Value
                            | _ -> ctx.[loopVar] <- item
                        else
                            match item with
                            // A single loop variable over a dictionary binds the
                            // key, matching Nunjucks dictionary iteration.
                            | :? KeyValuePair<string, obj> as kvp -> ctx.[loopVar] <- box kvp.Key
                            | :? System.Collections.DictionaryEntry as de -> ctx.[loopVar] <- de.Key
                            | _ -> ctx.[loopVar] <- item
                    // Reuse a single context + a single loop dictionary across all
                    // iterations to avoid per-iteration heap allocations (perf).
                    let mkCtx () =
                        let ctx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                        let loopDict = Dictionary<string, obj>()
                        ctx.["loop"] <- loopDict
                        ctx, loopDict
                    // Iterate without buffering the whole collection: IList is
                    // indexed, so @last / loop.length / previtem / nextitem stay
                    // accurate; a plain IEnumerable is streamed once (@index /
                    // @first only) to keep lazy sequences lazy.
                    match iter with
                    | :? System.Collections.IList as list ->
                        let ctx, loopDict = mkCtx ()
                        let mutable i = 0
                        let mutable stop = false
                        while i < list.Count && not stop && error.IsNone do
                            let item = list.[i]
                            bindItem ctx item
                            let prev = if i > 0 then box list.[i-1] else null
                            let nxt = if i < list.Count - 1 then box list.[i+1] else null
                            loopDict.["index"] <- box(i+1); loopDict.["index0"] <- box i
                            loopDict.["revindex"] <- box(list.Count-i); loopDict.["revindex0"] <- box(list.Count-i-1)
                            loopDict.["first"] <- box(i=0); loopDict.["last"] <- box(i=list.Count-1)
                            loopDict.["length"] <- box list.Count
                            loopDict.["depth"] <- box(env.LoopNesting + 1)
                            loopDict.["depth0"] <- box env.LoopNesting
                            loopDict.["previtem"] <- prev; loopDict.["nextitem"] <- nxt
                            match renderTokens loopTokens { env with Variables = ctx :> IDictionary<string, obj>; LoopNesting = env.LoopNesting + 1 } with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                            // break / continue are consumed by the nearest enclosing for.
                            if env.ControlFlow.Value = "break" then env.ControlFlow.Value <- ""; stop <- true
                            elif env.ControlFlow.Value = "continue" then env.ControlFlow.Value <- ""
                            i <- i + 1
                        if not stop && list.Count = 0 then
                            // else body of for
                            let elseBody = forElseBody (List.toArray bodyTokens)
                            match renderTokens elseBody env with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                    | :? System.Collections.IEnumerable as e when not (iter :? string) ->
                        // Single-pass streaming: loop.length / revindex / previtem
                        // / nextitem / last are unavailable for lazy data.
                        let ctx, loopDict = mkCtx ()
                        loopDict.["depth"] <- box(env.LoopNesting + 1)
                        loopDict.["depth0"] <- box env.LoopNesting
                        let mutable count = 0
                        let mutable any = false
                        let mutable stop = false
                        let en = e.GetEnumerator()
                        try
                            while en.MoveNext() && not stop && error.IsNone do
                                let item = en.Current
                                any <- true
                                bindItem ctx item
                                loopDict.["index"] <- box(count+1); loopDict.["index0"] <- box count
                                loopDict.["first"] <- box(count=0)
                                match renderTokens loopTokens { env with Variables = ctx :> IDictionary<string, obj>; LoopNesting = env.LoopNesting + 1 } with
                                | Ok h -> sb.Append(h) |> ignore
                                | Error er -> error <- Some er
                                if env.ControlFlow.Value = "break" then env.ControlFlow.Value <- ""; stop <- true
                                elif env.ControlFlow.Value = "continue" then env.ControlFlow.Value <- ""
                                count <- count + 1
                        finally
                            match box en with :? System.IDisposable as d -> d.Dispose() | _ -> ()
                        if not stop && not any then
                            let elseBody = forElseBody (List.toArray bodyTokens)
                            match renderTokens elseBody env with
                            | Ok h -> sb.Append(h) |> ignore
                            | Error e -> error <- Some e
                    | _ ->
                        // Non-iterable value (null / scalar): render the else body.
                        let elseBody = forElseBody (List.toArray bodyTokens)
                        match renderTokens elseBody env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e

                | "block" ->
                    let name = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    // Check if child template overrides this block
                    match env.ChildBlocks.TryGetValue name with
                    | true, childBody when not (env.BlockStack |> List.contains name) ->
                        // Render child's block content (which may itself extend further)
                        let childEnv = { env with BlockStack = name :: env.BlockStack; CurrentBlock = Some name }
                        match renderTokens childBody childEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | _ ->
                        // Use parent's default content
                        match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()

                | "extends" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let parentArr = tokenize txt |> Array.ofList
                        // Collect blocks from the parent (for super()) and the child
                        let parentBlocks = collectBlocks parentArr
                        let childBlocks = collectBlocks tokens
                        // Render parent with child blocks available for override
                        let parentEnv = { env with
                                            ChildBlocks = childBlocks
                                            Blocks = parentBlocks
                                            Depth = env.Depth + 1
                                            BlockStack = [] }
                        match renderTokensArr parentArr parentEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                        // extends replaces the whole template: stop rendering the
                        // child's own (already-inherited) tokens.
                        idx <- len
                    | Error e -> error <- Some e

                | "include" ->
                    let path = if args.Length > 0 then args.[0].Trim('"', '\'') else ""
                    let ignoreMissing = args.Length > 1 && (args |> String.concat " ").Contains("ignore", StringComparison.OrdinalIgnoreCase)
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        match renderTokens (tokenize txt) env with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | Error _ when ignoreMissing -> ()
                    | Error e -> error <- Some e

                | "set" ->
                    let setText = args |> String.concat " "
                    let eqIdx = setText.IndexOf("=")
                    if eqIdx >= 0 then
                        let sname = setText.[..eqIdx-1].Trim()
                        let rhsText = setText.[eqIdx+1..].Trim()
                        // Multiple assignment: {% set a, b = 1, 2 %} binds each
                        // left-hand name to the matching right-hand value.
                        let lhs = sname.Split(',') |> Array.map (fun s -> s.Trim())
                        if lhs.Length > 1 then
                            let rhs = splitTopLevelArgs rhsText |> List.map (fun e -> evalExpr e env.Variables)
                            let n = min lhs.Length rhs.Length
                            for j in 0..n-1 do env.Variables.[lhs.[j]] <- rhs.[j]
                        else
                            let sval = evalExpr rhsText env.Variables
                            env.Variables.[sname] <- sval
                    else
                        // Block assignment: {% set name %}...{% endset %}
                        let sname = setText.Trim().Trim('"', '\'')
                        let endIdx = findMatchingEnd (idx+1) "set" tokens
                        if endIdx > idx then
                            let body = tokens.[idx+1..endIdx-1] |> Array.toList
                            match renderTokens body env with
                            | Ok h -> env.Variables.[sname] <- box h
                            | Error e -> error <- Some e
                            idx <- endIdx   // consumed; default increment advances past {% endset %}
                        ()
                | "macro" ->
                    if args.Length > 0 then
                        let macroText = args |> String.concat " "
                        let pIdx = macroText.IndexOf('(')
                        let mname, margs =
                            if pIdx >= 0 then
                                let name = macroText.[..pIdx-1].Trim()
                                let argsPart =
                                    let cp = macroText.IndexOf(')', pIdx)
                                    if cp >= pIdx then macroText.[pIdx+1..cp-1].Trim() else ""
                                let pargs =
                                    if argsPart = "" then []
                                    else
                                        splitTopLevelArgs argsPart
                                        |> List.map (fun a ->
                                            let a = a.Trim()
                                            let eq = a.IndexOf('=')
                                            if eq > 0 then a.[..eq-1].Trim(), Some(a.[eq+1..].Trim())
                                            else a, None)
                                name, pargs
                            else macroText.Trim(), []
                        env.Macros.[mname] <- (margs, bodyTokens)
                    ()

                | "call" ->
                    // {% call macroName(arg) %}body{% endcall %}
                    let macroText = args |> String.concat " "
                    let pIdx = macroText.IndexOf('(')
                    let mname =
                        if pIdx >= 0 then macroText.[..pIdx-1].Trim()
                        else macroText.Trim()
                    let callArgs =
                        if pIdx >= 0 then
                            let at = macroText.[pIdx+1..macroText.Length-2].Trim()
                            if at = "" then [] else splitTopLevelArgs at |> List.map (fun a -> evalExpr a env.Variables)
                        else []
                    match env.Macros.TryGetValue mname with
                    | true, (margDefs, mbody) ->
                        let mCtx = bindMacroArgs margDefs callArgs env.Variables
                        // Make the captured body available as caller() (also kept as
                        // a string for backwards compatibility with {{ caller }}).
                        match bodyHtml with
                        | Some h -> mCtx.["caller"] <- box h
                        | None -> ()
                        let callEnv = { env with
                                            Variables = (mCtx :> IDictionary<string, obj>)
                                            CallerBody = if bodyHtml.IsSome then Some bodyTokens else None }
                        match renderTokens mbody callEnv with
                        | Ok h -> sb.Append(h) |> ignore
                        | Error e -> error <- Some e
                    | _ -> ()

                | "import" ->
                    let importText = args |> String.concat " "
                    let asIdx = importText.IndexOf(" as ", StringComparison.OrdinalIgnoreCase)
                    let path, asName =
                        if asIdx >= 0 then
                            importText.[..asIdx-1].Trim().Trim('"', '\''),
                            importText.[asIdx+4..].Trim()
                        else importText.Trim().Trim('"', '\''), ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let importTokens = tokenize txt |> Array.ofList
                        // Register every macro from the imported file as a callable.
                        let defs = collectMacroDefs importTokens
                        for (mname, margs, body) in defs do
                            let key = if asName <> "" then asName + "." + mname else mname
                            env.Macros.[key] <- (margs, body)
                    | Error e -> error <- Some e

                | "from" ->
                    let fromText = args |> String.concat " "
                    let impIdx = fromText.IndexOf(" import ", StringComparison.OrdinalIgnoreCase)
                    let path, imports =
                        if impIdx >= 0 then
                            fromText.[..impIdx-1].Trim().Trim('"', '\''),
                            fromText.[impIdx+8..].Trim()
                        else "", ""
                    let asIdxOrig = imports.IndexOf(" as ", StringComparison.OrdinalIgnoreCase)
                    let importName, asName =
                        if asIdxOrig >= 0 then imports.[..asIdxOrig-1].Trim(), imports.[asIdxOrig+4..].Trim()
                        else imports.Trim(), ""
                    match env.LoadTemplate (path, env.Depth + 1) with
                    | Ok txt ->
                        let defs = collectMacroDefs (tokenize txt |> Array.ofList)
                        match defs |> List.tryFind (fun (mname, _, _) -> mname = importName) with
                        | Some(_, margs, body) ->
                            let key = if asName <> "" then asName else importName
                            env.Macros.[key] <- (margs, body)
                        | None -> ()
                    | Error e -> error <- Some e

                | "raw" ->
                    match bodyHtml with Some h -> sb.Append(h) |> ignore | None -> ()

                | "filter" ->
                    let fname = if args.Length > 0 then args.[0] else ""
                    match bodyHtml with
                    | Some h -> sb.Append(toStr (applyFilter fname (box h) [])) |> ignore
                    | None -> ()

                | "break" -> env.ControlFlow.Value <- "break"
                | "continue" -> env.ControlFlow.Value <- "continue"

                | "with" ->
                    // {% with a = 1, b = 2 %}body{% endwith %} — variables are
                    // scoped to the block; {% set %} inside does not leak out.
                    let wText = args |> String.concat " "
                    let newCtx = Dictionary<string, obj>(env.Variables |> Seq.map (fun kv -> KeyValuePair(kv.Key, kv.Value)))
                    for pair in wText.Split(',') do
                        let pair = pair.Trim()
                        let eq = pair.IndexOf("=")
                        if eq > 0 then
                            newCtx.[pair.[..eq-1].Trim()] <- evalExpr pair.[eq+1..] env.Variables
                    match renderTokens bodyTokens { env with Variables = newCtx :> IDictionary<string, obj> } with
                    | Ok h -> sb.Append(h) |> ignore
                    | Error e -> error <- Some e

                | "now" | "endblock" | "endfor" | "endif" | "endmacro" | "endcall" | "endraw" | "endfilter" | "endwith" ->
                    ()  // closing tags are handled by findMatchingEnd

                | _ -> ()  // unknown tag — silently ignore (Nunjucks behavior)

                idx <- if isBlock && endIdx > idx then endIdx + 1 else idx + 1

        match error with
        | Some e -> Error e
        | None -> Ok(sb.ToString())

    /// Split an if-block body into ordered branches, each tagged with an
    /// optional condition (None = the final `else`). Nested if/for blocks are
    /// skipped so their inner elif/else tags don't split the outer branch.
    and splitIfBranches (firstCond: string) (arr: Token[]) : (string option * Token list) list =
        let n = arr.Length
        let branches = ResizeArray<string option * Token list>()
        let mutable curCond : string option = Some firstCond
        let cur = ResizeArray<Token>()
        let mutable depth = 0
        let mutable i = 0
        while i < n do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1; cur.Add arr.[i]
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1; cur.Add arr.[i]
            | TagToken(("elif" | "elseif"), a, _) when depth = 0 ->
                branches.Add(curCond, List.ofSeq cur); cur.Clear()
                curCond <- Some(a |> String.concat " ")
            | TagToken("else", _, _) when depth = 0 ->
                branches.Add(curCond, List.ofSeq cur); cur.Clear()
                curCond <- None
            | t -> cur.Add t
            i <- i + 1
        branches.Add(curCond, List.ofSeq cur)
        List.ofSeq branches

    /// Extract the `{% else %}` body of a for-loop (depth-aware). Returns the
    /// tokens after a top-level else, or an empty list when none is present.
    and forElseBody (arr: Token[]) : Token list =
        let n = arr.Length
        let mutable depth = 0
        let mutable elseIdx = -1
        let mutable i = 0
        while i < n && elseIdx < 0 do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1
            | TagToken("else", _, _) when depth = 0 -> elseIdx <- i
            | _ -> ()
            i <- i + 1
        if elseIdx >= 0 && elseIdx + 1 < n then arr.[elseIdx+1..] |> Array.toList else []

    /// Extract the loop body of a for-loop, stopping at a top-level else.
    and forLoopBody (arr: Token[]) : Token list =
        let n = arr.Length
        let mutable depth = 0
        let mutable elseIdx = -1
        let mutable i = 0
        while i < n && elseIdx < 0 do
            match arr.[i] with
            | TagToken(("if" | "for"), _, _) -> depth <- depth + 1
            | TagToken(("endif" | "endfor"), _, _) -> depth <- depth - 1
            | TagToken("else", _, _) when depth = 0 -> elseIdx <- i
            | _ -> ()
            i <- i + 1
        if elseIdx >= 0 then arr.[..elseIdx-1] |> Array.toList else Array.toList arr
