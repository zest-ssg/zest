namespace Zest.Dsl

open System
open System.Text.RegularExpressions

// ============================================================
// DslUtilities — Control flow, string interp, collections, math
// ============================================================

module DslUtilities =
    open Dsl

    // ── Control flow helpers ─────────────────────────────────────

    let switch_str (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    let cond_str (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let chain_cond (conditions: (bool * string) list) (fallback: string) =
        conditions |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    let choose (cond: bool) (ifTrue: string) (ifFalse: string) =
        if cond then ifTrue else ifFalse

    // ── String interpolation ─────────────────────────────────────

    let interp (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> v
                | _ -> m.Value
        )

    let interp_safe (template: string) (vars: (string * string) list) =
        let dict = dict vars
        Regex.Replace(
            template,
            @"\{(\w+)\}",
            fun m ->
                match dict.TryGetValue(m.Groups.[1].Value) with
                | true, v -> htmlEncode v
                | _ -> m.Value
        )

    // ── Collection helpers ───────────────────────────────────────

    let take_n (n: int) (items: string list) = items |> List.truncate n

    let skip_n (n: int) (items: string list) =
        items |> List.skip (min n items.Length)

    let filter_by (pred: string -> bool) (items: string list) =
        items |> List.filter pred

    let map_by (f: string -> string) (items: string list) =
        items |> List.map f

    let group_by (keyFn: string -> string) (items: string list) =
        items
        |> List.groupBy keyFn
        |> List.map (fun (k, g) -> k, List.ofSeq g)

    let chunk (size: int) (items: string list) =
        items |> List.chunkBySize size

    let intersperse_str (sep: string) (items: string list) =
        items |> List.collect (fun x -> [sep; x]) |> List.tail

    let zip_lists (a: string list) (b: string list) = List.zip a b

    // ── Math helpers ─────────────────────────────────────────────

    let sum (items: int list) = items |> List.sum

    let avg (items: int list) =
        if items.IsEmpty then 0
        else (items |> List.sum) / items.Length

    let min_val (items: int list) = items |> List.min
    let max_val (items: int list) = items |> List.max
