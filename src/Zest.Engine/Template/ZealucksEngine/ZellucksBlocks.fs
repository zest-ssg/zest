namespace Zest.Engine.Template

open System.Collections.Generic
open NunjucksTypes
open NunjucksCompiler

// NunjucksBlocks.fs
//
// Analyses token arrays for template inheritance: collecting top-level
// `{% block %}` and `{% macro %}` definitions and locating the matching end
// tag for a block-like opening tag.
//
// Invariant: findMatchingEnd is array-indexed (O(1) per element) so scanning
// a template stays linear in its token count.

module internal NunjucksBlocks =

    // ── Block collector (for extends/block inheritance) ──
    /// Collect all top-level `{% block NAME %}...{% endblock %}` blocks
    /// from a token array. Returns a map from block name to its body tokens.
    let rec collectBlocks (tokens: Token[]) : IDictionary<string, Token list> =
        let blocks = Dictionary<string, Token list>()
        let len = tokens.Length
        let mutable i = 0
        while i < len do
            match tokens.[i] with
            | TagToken("block", args, _) when args.Length > 0 ->
                let name = args.[0].Trim('"', '\'')
                let endIdx = findMatchingEnd (i+1) "block" tokens
                if endIdx > i then
                    let body = tokens.[i+1..endIdx-1] |> Array.toList
                    blocks.[name] <- body
                    i <- endIdx + 1
                else i <- i + 1
            | TagToken("extends", _, _) | TagToken("macro", _, _) ->
                i <- i + 1
            | _ -> i <- i + 1
        blocks :> IDictionary<string, Token list>

    // Array-indexed (O(1) per element) version of findMatchingEnd. The old
    // list-based version indexed a linked list on every step — O(n²) per tag.
    and findMatchingEnd (start: int) (tagName: string) (tokens: Token[]) : int =
        let len = tokens.Length
        let mutable depth = 0
        let mutable result = len
        let mutable i = start
        while i < len do
            match tokens.[i] with
            | TagToken(n, _, _) when n = tagName -> depth <- depth + 1; i <- i + 1
            | TagToken(n, _, _) when n = "end" + tagName ->
                if depth = 0 then result <- i; i <- len  // found it, save position
                else depth <- depth - 1; i <- i + 1
            | _ -> i <- i + 1
        result

    /// Collect all top-level `{% macro name(args) %}...{% endmacro %}` definitions
    /// from a token array. Returns (name, args, body) tuples so they can be
    /// registered into the macro table (used by import / from). Each argument
    /// carries an optional default expression (`arg=default`).
    let collectMacroDefs (tsArr: Token []) : (string * (string * string option) list * Token list) list =
        let mutable result = []
        let mutable i = 0
        let n = tsArr.Length
        while i < n do
            match tsArr.[i] with
            | TagToken("macro", a, _) when a.Length > 0 ->
                let macroText = a |> String.concat " "
                let pIdx = macroText.IndexOf('(')
                let mname, margs =
                    if pIdx >= 0 then
                        let name = macroText.[..pIdx-1].Trim()
                        let cp = macroText.IndexOf(')', pIdx)
                        let argsPart = if cp >= pIdx then macroText.[pIdx+1..cp-1].Trim() else ""
                        let pargs =
                            if argsPart = "" then []
                            else
                                splitTopLevelArgs argsPart
                                |> List.map (fun x ->
                                    let a = x.Trim()
                                    let eq = a.IndexOf('=')
                                    if eq > 0 then a.[..eq-1].Trim(), Some(a.[eq+1..].Trim())
                                    else a, None)
                        name, pargs
                    else macroText.Trim(), []
                let eIdx = findMatchingEnd (i+1) "macro" tsArr
                let body = if eIdx > i+1 then tsArr.[i+1..eIdx-1] |> Array.toList else []
                result <- (mname, margs, body) :: result
                i <- if eIdx > i then eIdx + 1 else i + 1
            | _ -> i <- i + 1
        List.rev result

    // ── Block tags that require a closing end-tag ──────────
    let blockTags = set ["if"; "for"; "block"; "macro"; "filter"; "call"; "with"]
