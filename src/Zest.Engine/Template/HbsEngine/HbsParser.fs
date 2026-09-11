namespace Zest.Engine.Template

open System.Collections.Concurrent
open HbsTypes
open HbsTokenizer

// HbsParser.fs
//
// Builds a Handlebars AST from the token stream via a recursive-descent parser
// and caches the parsed AST per distinct source hash.
//
// Invariant: the AST cache is keyed by content hash (via TemplateUtils) so a
// layout reused across pages parses once.

module internal HbsParser =

    // ── Recursive-descent parser ───────────────────────────────────────
    // Builds the AST. parseNode handles one token; blocks recurse.
    let rec parseBodyUntilElse (tokens: HbsToken list) : HbsNode list * HbsToken list * bool =
        // returns (nodes, rest, hitElse) — stops at TElse/TElseIf/TBlockClose
        let rec go acc rest =
            match rest with
            | [] -> List.rev acc, [], false
            | TElse :: _ -> List.rev acc, rest, true
            | TElseIf _ :: _ -> List.rev acc, rest, true
            | TBlockClose _ :: _ -> List.rev acc, rest, false
            | token :: tail ->
                match parseNode token tail with
                | node, rest' -> go (node :: acc) rest'
        go [] tokens

    and parseBodyUntilClose (name: string) (tokens: HbsToken list) : HbsNode list * HbsToken list =
        let rec go acc rest =
            match rest with
            | [] -> List.rev acc, []
            | TBlockClose n :: tail when n = name -> List.rev acc, tail
            | token :: tail ->
                match parseNode token tail with
                | node, rest' -> go (node :: acc) rest'
        go [] tokens

    and parseNode (token: HbsToken) (rest: HbsToken list) : HbsNode * HbsToken list =
        match token with
        | TText t -> NText t, rest
        | TExpr(e, t) -> NExpr(e, t), rest
        | TComment -> NText "", rest
        | TPartial(name, args) -> NPartial(name, args), rest
        | TBlockOpen(name, args) ->
            let body, afterBody, hitElse = parseBodyUntilElse rest
            if not hitElse then
                match afterBody with
                | TBlockClose n :: tail when n = name ->
                    NBlock(name, args, body, None), tail
                | _ -> NBlock(name, args, body, None), afterBody
            else
                // collect else / else-if chain
                let rec collectElseChain (nodes: HbsNode list) (toks: HbsToken list) : HbsNode list * HbsToken list =
                    match toks with
                    | TElse :: tail ->
                        let eb, after = parseBodyUntilClose name tail
                        List.rev (List.rev nodes @ eb), after
                    | TElseIf a :: tail ->
                        let eb, after = parseBodyUntilClose name tail
                        let inner = NBlock("if", a, eb, None)
                        collectElseChain (inner :: nodes) after
                    | TBlockClose n :: tail when n = name ->
                        List.rev nodes, tail
                    | TBlockClose n :: tail ->
                        List.rev nodes, (TBlockClose n :: tail)
                    | [] -> List.rev nodes, []
                    | _ :: tail -> collectElseChain nodes tail
                let elseBody, tail = collectElseChain [] afterBody
                if elseBody.IsEmpty then NBlock(name, args, body, None), tail
                else NBlock(name, args, body, Some elseBody), tail
        | TInverted(name, args) ->
            let body, afterBody, _ = parseBodyUntilElse rest
            match afterBody with
            | TBlockClose n :: tail when n = name -> NInverted(name, args, body), tail
            | _ -> NInverted(name, args, body), afterBody
        | TElse | TElseIf _ | TBlockClose _ -> NText "", rest

    // ── Parsed AST cache ───────────────────────────────────────────────
    // A layout rendered once per page (or shared across pages) should parse
    // once per distinct source. Keyed by content hash via TemplateUtils.
    let private astCache = ConcurrentDictionary<int64, HbsNode list>()

    let parse (src: string) : HbsNode list =
        if isNull src then []
        else
            let key = TemplateUtils.hashSource src
            match astCache.TryGetValue key with
            | true, a -> a
            | _ ->
                let tokens = tokenize src
                let rec go acc rest =
                    match rest with
                    | [] -> List.rev acc
                    | token :: tail ->
                        match parseNode token tail with
                        | node, rest' -> go (node :: acc) rest'
                let ast = go [] tokens
                astCache.[key] <- ast
                ast

    /// Clear the parsed-AST cache (called on engine cache clear).
    let clearAstCache () = astCache.Clear()
