namespace Zest.Dsl

open System
open System.Collections.Generic
open System.Text
open System.Text.RegularExpressions

// ============================================================
// SequenceHelper — Sequence ops, type conversion, and sugar
// ============================================================

module SequenceHelper =

    // ── Data type helpers ──────────────────────────────────────

    let kv (k: string) (v: obj) = (k, v)

    let kv_list (pairs: (string * obj) list) = pairs

    let kv_get (key: string) (pairs: (string * obj) list) =
        pairs |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd

    // ── Sequence helpers ──────────────────────────────────────

    /// Generate a range of integers as strings from start to end (inclusive).
    let range (start: int) (endInclusive: int) =
        [ start..endInclusive ] |> List.map string

    /// Repeat a string N times.
    let repeat (count: int) (s: string) =
        StringBuilder().Insert(0, s, count).ToString()

    // ── URL helpers ───────────────────────────────────────────

    /// Join a base URL with a relative path, handling slashes correctly.
    let url_join (baseUrl: string) (path: string) =
        let base' = baseUrl.TrimEnd('/')
        let path' = path.TrimStart('/')
        base' + "/" + path'

    /// Check whether a URL is absolute (starts with scheme).
    let is_absolute_url (url: string) =
        Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z0-9+\-.]*://")

    // ── Type conversion helpers ───────────────────────────────

    /// Convert a value to its string representation.
    let to_string (value: obj) = value.ToString()

    /// Try to parse an integer; return fallback on failure.
    let to_int (fallback: int) (s: string) =
        match Int32.TryParse(s) with
        | true, v -> v
        | _ -> fallback

    /// Try to parse a boolean; return fallback on failure.
    let to_bool (fallback: bool) (s: string) =
        match Boolean.TryParse(s) with
        | true, v -> v
        | _ -> fallback

    // ── Syntactic sugar ───────────────────────────────────────

    /// Forward pipe operator.
    let (|>) x f = f x

    /// Backward pipe operator.
    let (<|) f x = f x

    /// Function composition (right-to-left).
    let (>>) f g x = g (f x)

    /// Ternary conditional operator for any type.
    let inline ternary (cond: bool) (ifTrue: 'T) (ifFalse: 'T) =
        if cond then ifTrue else ifFalse

    /// Return Some value if non-null/non-empty; otherwise None.
    let as_option (s: string) =
        if String.IsNullOrEmpty s then None else Some s

    /// Apply a function to a value only when condition is true.
    let apply_when (cond: bool) (f: 'T -> 'T) (value: 'T) =
        if cond then f value else value

    /// Apply a function to a value only when condition is false.
    let apply_unless (cond: bool) (f: 'T -> 'T) (value: 'T) =
        if cond then value else f value

    /// Try with: apply a function, return fallback on exception.
    let try_with (f: unit -> 'T) (fallback: 'T) =
        try f () with _ -> fallback

    /// Tap: execute a side-effect and return the value unchanged.
    let tap (f: 'T -> unit) (value: 'T) =
        f value; value

    /// Pair a value with a key for dict-building.
    let (=>) (key: string) (value: obj) = (key, value)

    /// Create a dict from key-value pairs.
    let dict_of (pairs: (string * obj) list) =
        let d = Dictionary<string, obj>()
        for (k, v) in pairs do d.[k] <- v
        d

    // ── List operation helpers ────────────────────────────────

    /// Take items from a list while a predicate holds.
    let take_while (pred: 'a -> bool) (items: 'a list) =
        items |> List.takeWhile pred

    /// Skip items from a list while a predicate holds.
    let skip_while (pred: 'a -> bool) (items: 'a list) =
        items |> List.skipWhile pred

    /// Partition a list into two based on a predicate.
    let partition (pred: 'a -> bool) (items: 'a list) =
        items |> List.partition pred

    /// Sort a list by a key projection.
    let sort_by (proj: 'a -> 'b when 'b : comparison) (items: 'a list) =
        items |> List.sortBy proj

    /// Sort a list by a key projection in descending order.
    let sort_by_desc (proj: 'a -> 'b when 'b : comparison) (items: 'a list) =
        items |> List.sortByDescending proj

    /// Deduplicate a list.
    let dedup (items: 'a list when 'a : equality) =
        items |> List.distinct

    /// Flat-map: map each element to a list and flatten.
    let flat_map (f: 'a -> 'b list) (items: 'a list) =
        items |> List.collect f
