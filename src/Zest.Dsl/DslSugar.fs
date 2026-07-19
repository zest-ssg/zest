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

    // ── Option / nullable rendering ─────────────────────────────

    /// Render an `Option<string>`: `Some s` → `s`, `None` → `""`.
    /// Same as `DslComponents.opt` but available in the sugar module.
    let opt_str (v: string option) = match v with Some s -> s | None -> ""

    /// Render an `Option<string>` with a fallback for `None`.
    let opt_or (fallback: string) (v: string option) =
        match v with Some s when not (String.IsNullOrEmpty s) -> s | _ -> fallback

    /// Apply a render function only when the value is `Some`, else `""`.
    let opt_map (f: 'a -> string) (v: 'a option) =
        match v with Some x -> f x | None -> ""

    /// Render content only when the value is `Some`, ignoring the inner value.
    let opt_when (v: 'a option) (content: string) =
        match v with Some _ -> content | None -> ""

    // ── Joining helpers ─────────────────────────────────────────

    /// Join items with newlines (alias for readability in pipelines).
    let join_lines (items: string list) = String.concat "\n" items

    /// Join items with commas (e.g. tag lists).
    let join_comma (items: string list) = String.concat ", " items

    /// Join items with a custom separator (alias for `joinWith`).
    let join_with (sep: string) (items: string list) = String.concat sep items

    /// Intersperse a separator BETWEEN items (not trailing).
    /// `intersperse ", " ["a";"b";"c"]` → `"a, b, c"`.
    let intersperse (sep: string) (items: string list) =
        match items with
        | [] | [_] -> String.concat "" items
        | head :: tail -> head + (tail |> List.map (fun x -> sep + x) |> String.concat "")

    // ── Text formatting ─────────────────────────────────────────

    /// Truncate a string to `maxLen` chars, appending an ellipsis if cut.
    let truncate_str (maxLen: int) (s: string) =
        if s = null then ""
        elif s.Length <= maxLen then s
        else s.[..maxLen-1] + "…"

    /// Pad a string to a fixed width with spaces (right-padded).
    let pad_right (width: int) (s: string) = s.PadRight(width)

    /// Pad a string to a fixed width with spaces (left-padded).
    let pad_left (width: int) (s: string) = s.PadLeft(width)

    /// Simple pluralisation: `pluralize 1 "item"` → `"1 item"`,
    /// `pluralize 3 "item"` → `"3 items"` (appends 's'). For irregular
    /// plurals pass the plural form explicitly.
    let pluralize (count: int) (singular: string) =
        let word = if count = 1 then singular else singular + "s"
        sprintf "%d %s" count word

    /// Pluralise with an explicit plural form.
    let pluralize_with (count: int) (singular: string) (plural: string) =
        let word = if count = 1 then singular else plural
        sprintf "%d %s" count word

    /// Capitalise the first character (sentence case).
    let capitalize (s: string) =
        if String.IsNullOrEmpty s then s
        else s.[0].ToString().ToUpperInvariant() + s.[1..]

    /// Convert a kebab/snake-case string to a human-readable title.
    /// `"post-list"` / `"post_list"` → `"Post List"`.
    let titleize (s: string) =
        s.Replace('-', ' ').Replace('_', ' ').Split(' ')
        |> Array.filter (fun w -> w.Length > 0)
        |> Array.map (fun w -> w.[0].ToString().ToUpperInvariant() + (if w.Length > 1 then w.[1..] else ""))
        |> String.concat " "
