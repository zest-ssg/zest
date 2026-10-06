namespace Zest.Compiler.Zestucks
open System.Collections.Generic
open Tokens
open ExpressionCompiler

// BlockCollector.fs
//
// Analyses token arrays for template inheritance: collecting top-level
// `{% block %}` and `{% macro %}` definitions and locating the matching end
// tag for a block-like opening tag.
//
// Invariant: findMatchingEnd is array-indexed (O(1) per element) so scanning
// a template stays linear in its token count.

module internal BlockCollector =

    // ── Block collector (for extends/block inheritance) ──
    /// Collect all top-level `{% block NAME %}...{% endblock %}` blocks within
    /// `range`. Returns a map from block name to the body's token window (no
    /// copy: it shares the caller's backing array).
    let rec collectBlocks (range: TokenRange) : IDictionary<string, TokenRange> =
        let blocks = Dictionary<string, TokenRange>()
        let tokens = range.Source
        let stop = range.Stop
        let mutable i = range.Start
        while i < stop do
            match tokens.[i] with
            | TagToken("block", args, _) when args.Length > 0 ->
                let name = args.[0].Trim('"', '\'')
                let endIdx = findMatchingEnd (i+1) stop "block" tokens
                if endIdx > i then
                    blocks.[name] <- { Source = tokens; Start = i + 1; Count = endIdx - i - 1 }
                    i <- endIdx + 1
                else i <- i + 1
            | _ -> i <- i + 1
        blocks :> IDictionary<string, TokenRange>

    // Array-indexed (O(1) per element) scan bounded to [start, stop). Returns
    // `stop` when no matching end tag exists inside the window.
    and findMatchingEnd (start: int) (stop: int) (tagName: string) (tokens: Token[]) : int =
        let mutable depth = 0
        let mutable result = stop
        let mutable i = start
        while i < stop do
            match tokens.[i] with
            | TagToken(n, _, _) when n = tagName -> depth <- depth + 1; i <- i + 1
            | TagToken(n, _, _) when n = "end" + tagName ->
                if depth = 0 then result <- i; i <- stop  // found it, save position
                else depth <- depth - 1; i <- i + 1
            | _ -> i <- i + 1
        result

    /// Collect all top-level `{% macro name(args) %}...{% endmacro %}` definitions
    /// within `range`. Returns (name, args, body) tuples so they can be
    /// registered into the macro table (used by import / from). Each argument
    /// carries an optional default expression (`arg=default`).
    let collectMacroDefs (range: TokenRange) : (string * (string * string option) list * TokenRange) list =
        let tsArr = range.Source
        let stop = range.Stop
        let mutable result = []
        let mutable i = range.Start
        while i < stop do
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
                let eIdx = findMatchingEnd (i+1) stop "macro" tsArr
                let body =
                    if eIdx > i+1 then { Source = tsArr; Start = i + 1; Count = eIdx - i - 1 }
                    else TokenRange.empty
                result <- (mname, margs, body) :: result
                i <- if eIdx > i then eIdx + 1 else i + 1
            | _ -> i <- i + 1
        List.rev result

    // ── Block tags that require a closing end-tag ──────────
    let blockTags = set ["if"; "for"; "block"; "macro"; "filter"; "call"; "with"]
