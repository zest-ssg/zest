namespace Zest.Dsl

open System
open System.Text

// ============================================================
// DslSugar — Conditionals, loops, pipelines, and shortcuts
// ============================================================

module DslSugar =
    open Dsl

    // ── Implicit yield helpers ───────────────────────────────────

    /// Begin an implicit-yield block with newline separators.
    let yield_block (nodes: string list) =
        nodes |> String.concat "\n"

    /// Begin an implicit-yield block without separators.
    let yield_inline (nodes: string list) =
        nodes |> String.concat ""

    // ── Shorthand conditionals ───────────────────────────────────

    /// Ternary-like conditional for strings.
    let inline cond (condition: bool) (ifTrue: string) (ifFalse: string) =
        if condition then ifTrue else ifFalse

    /// Return fallback if value is null/empty.
    let default_to (fallback: string) (value: string) =
        if String.IsNullOrEmpty value then fallback else value

    /// Return the first non-null/non-empty value from a list.
    let coalesce_str (values: string list) =
        values |> List.tryFind (fun v -> not (String.IsNullOrEmpty v))
        |> Option.defaultValue ""

    /// Return content only if condition is true.
    let when_true (cond: bool) (content: string) =
        if cond then content else ""

    /// Return content only if condition is false.
    let unless_true (cond: bool) (content: string) =
        if cond then "" else content

    /// Switch on a string value, return the matching case.
    let switch_value (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    /// Match on boolean conditions, return first match.
    let match_cond (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ── Simplified loops and iterators ───────────────────────────

    /// Map over items and join with a separator.
    let each_with (items: 'a list) (separator: string) (f: 'a -> string) =
        items |> List.map f |> String.concat separator

    /// Map over items and join with newlines.
    let each_line (items: 'a list) (f: 'a -> string) =
        items |> List.map f |> String.concat "\n"

    /// Map over items and wrap in a container tag.
    let each_in_container (tag: string) (items: 'a list) (f: 'a -> string) =
        let inner = items |> List.map f |> String.concat ""
        elem tag [] [inner]

    /// Repeat a string N times.
    let repeat_str (count: int) (s: string) =
        StringBuilder().Insert(0, s, count).ToString()

    /// Generate a numbered list of items.
    let numbered_list (items: 'a list) (f: int -> 'a -> string) =
        items |> List.mapi (fun i item -> f (i + 1) item) |> String.concat "\n"

    /// For-loop over a range with a render function.
    let for_range (start: int) (endInclusive: int) (f: int -> string) =
        [start..endInclusive] |> List.map f |> String.concat ""

    // ── Pipeline / chaining operators ────────────────────────────

    /// Forward pipe operator.
    let (|>) = (|>)

    /// Backward pipe operator.
    let (<|) = (<|)

    /// Function composition.
    let (>>) = (>>)

    /// Wrap a string in an HTML tag.
    let wrap_in (tag: string) (content: string) =
        sprintf "<%s>%s</%s>" tag content tag

    /// Add a CSS class to an element string.
    let add_class (cls: string) (element: string) =
        let pattern = @"^<(\w+)"
        let m = Text.RegularExpressions.Regex.Match(element, pattern)
        if m.Success then
            let tag = m.Groups.[1].Value
            element.Replace(sprintf "<%s" tag, sprintf "<%s class=\"%s\"" tag cls)
        else element

    // ── Shorthand element builders ───────────────────────────────

    /// Create a div with text content.
    let div_text (cls: string) (content: string) =
        divC cls [text content]

    /// Create a span with text content.
    let span_text (cls: string) (content: string) =
        spanC cls [text content]

    /// Create a paragraph with text content.
    let p_text (content: string) =
        p [text content]

    /// Create a heading with text content.
    let h_text (level: int) (content: string) =
        let tag = sprintf "h%d" level
        elem tag [] [text content]

    /// Create a link with text content.
    let a_text (url: string) (textContent: string) =
        a url [text textContent]

    /// Create a link with a CSS class and text content.
    let a_text_c (cls: string) (url: string) (textContent: string) =
        aC cls url [text textContent]

    /// Create an image with a CSS class.
    let img_c (cls: string) (src: string) (alt: string) =
        imgC cls src alt

    /// Create a ul from items using a render function.
    let ul_from (items: 'a list) (f: 'a -> string) =
        ul (items |> List.map (fun i -> li [f i]))

    /// Create an ol from items using a render function.
    let ol_from (items: 'a list) (f: 'a -> string) =
        ol (items |> List.map (fun i -> li [f i]))

    // ── Type conversion shortcuts ────────────────────────────────

    /// Convert any value to a string via .ToString().
    let inline str (x: 'a) = x.ToString()

    /// Convert an integer to a string.
    let inline int_str (x: int) = string x

    /// Convert a float to a string with format.
    let float_str (format: string) (x: float) = x.ToString(format)

    /// Convert a boolean to "true" / "false".
    let bool_str (x: bool) = if x then "true" else "false"
