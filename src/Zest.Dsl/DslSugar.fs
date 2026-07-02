namespace Zest.Dsl

open System
open System.Text

// ============================================================
// DslSugar — Syntactic sugar for the string-based Zest DSL
// ============================================================
// Provides simplified syntax for common patterns:
//   - Shorthand conditionals (ternary-like `??`)
//   - Simplified loops and iterators
//   - Implicit yield wrappers for block expressions
//   - Pipeline operators for chaining
//   - Shorthand element builders
// ============================================================

module DslSugar =
    open Dsl

    // ══════════════════════════════════════════════════════════════
    // ── Implicit yield helpers ───────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Begin an implicit-yield block. All expressions within the
    /// block are automatically collected (yielded) without explicit
    /// concatenation. The block automatically concatenates its
    /// string results with newlines.
    /// Usage:
    ///   yield_block [
    ///       h1 [text "Title"]
    ///       p  [text "Paragraph"]
    ///   ]
    let yield_block (nodes: string list) =
        nodes |> String.concat "\n"

    /// Begin an implicit-yield block without separators.
    /// All expressions are concatenated directly.
    /// Usage:
    ///   yield_inline [
    ///       span [text "A"]
    ///       span [text "B"]
    ///   ]
    let yield_inline (nodes: string list) =
        nodes |> String.concat ""

    // ══════════════════════════════════════════════════════════════
    // ── Shorthand conditionals ───────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Ternary-like conditional operator for strings.
    /// Usage: `cond (x > 0) "positive" "non-positive"`
    let inline cond (condition: bool) (ifTrue: string) (ifFalse: string) =
        if condition then ifTrue else ifFalse

    /// Null-coalescing-like default operator for strings.
    /// Returns `value` if non-null/non-empty, otherwise `fallback`.
    /// Usage: `title ?? "Untitled"`  →  `default_to "Untitled" title`
    let default_to (fallback: string) (value: string) =
        if String.IsNullOrEmpty value then fallback else value

    /// Coalesce: return the first non-null/non-empty value from a list.
    /// Usage: `coalesce_str [metaTitle; pageTitle; "Default"]`
    let coalesce_str (values: string list) =
        values |> List.tryFind (fun v -> not (String.IsNullOrEmpty v))
        |> Option.defaultValue ""

    /// When-true: return content only if condition is true.
    /// Usage: `when_true isPublished "<p>Published</p>"`
    let when_true (cond: bool) (content: string) =
        if cond then content else ""

    /// Unless-true: return content only if condition is false.
    /// Usage: `unless_true isDraft "<p>Live</p>"`
    let unless_true (cond: bool) (content: string) =
        if cond then "" else content

    /// Switch on a string value, return the matching case.
    /// Usage: `switch_value theme ["dark", darkCss; "light", lightCss] defaultCss`
    let switch_value (value: string) (cases: (string * string) list) (defaultCase: string) =
        cases
        |> List.tryFind (fun (v, _) -> v = value)
        |> Option.map snd
        |> Option.defaultValue defaultCase

    /// Match on a list of boolean conditions, return first match.
    /// Usage: `match_cond [(isHome, homeHtml); (isPost, postHtml)] fallbackHtml`
    let match_cond (cases: (bool * string) list) (fallback: string) =
        cases |> List.tryFind fst |> Option.map snd |> Option.defaultValue fallback

    // ══════════════════════════════════════════════════════════════
    // ── Simplified loops and iterators ───────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Map over items and join results with a separator.
    /// Usage: `each_with items ", " (fun i -> span [text i])`
    let each_with (items: 'a list) (separator: string) (f: 'a -> string) =
        items |> List.map f |> String.concat separator

    /// Map over items and join results with newlines.
    /// Usage: `each_line items (fun i -> li [text i])`
    let each_line (items: 'a list) (f: 'a -> string) =
        items |> List.map f |> String.concat "\n"

    /// Map over items and wrap each result in a container.
    /// Usage: `each_in_container "ul" items (fun i -> li [text i])`
    let each_in_container (tag: string) (items: 'a list) (f: 'a -> string) =
        let inner = items |> List.map f |> String.concat ""
        elem tag [] [inner]

    /// Repeat a string N times.
    /// Usage: `repeat_str 3 "<br />"`
    let repeat_str (count: int) (s: string) =
        StringBuilder().Insert(0, s, count).ToString()

    /// Generate a numbered list of items.
    /// Usage: `numbered_list items (fun i item -> sprintf "%d. %s" i item)`
    let numbered_list (items: 'a list) (f: int -> 'a -> string) =
        items |> List.mapi (fun i item -> f (i + 1) item) |> String.concat "\n"

    /// For-loop over a range with a render function.
    /// Usage: `for_range 1 5 (fun i -> sprintf "<span>%d</span>" i)`
    let for_range (start: int) (endInclusive: int) (f: int -> string) =
        [start..endInclusive] |> List.map f |> String.concat ""

    // ══════════════════════════════════════════════════════════════
    // ── Pipeline / chaining operators ────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Forward pipe: apply value to a function.
    /// Usage: `title |> wrap_in "h1"`
    let (|>) = (|>)

    /// Backward pipe: apply a function to a value.
    /// Usage: `wrap_in "h1" <| title`
    let (<|) = (<|)

    /// Function composition.
    /// Usage: `(wrap_in "div" >> add_class "hero") content`
    let (>>) = (>>)

    /// Wrap a string in an HTML tag.
    /// Usage: `wrap_in "h1" "Hello"`
    let wrap_in (tag: string) (content: string) =
        sprintf "<%s>%s</%s>" tag content tag

    /// Add a CSS class to an element string (modifies class attribute).
    /// Assumes the element is a simple tag from the DSL.
    /// Usage: `add_class "hero" "<div>content</div>"`  →  `<div class="hero">content</div>`
    let add_class (cls: string) (element: string) =
        let pattern = @"^<(\w+)"
        let m = Text.RegularExpressions.Regex.Match(element, pattern)
        if m.Success then
            let tag = m.Groups.[1].Value
            element.Replace(sprintf "<%s" tag, sprintf "<%s class=\"%s\"" tag cls)
        else element

    // ══════════════════════════════════════════════════════════════
    // ── Shorthand element builders ───────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Create a div with text content (single string, not a list).
    /// Usage: `div_text "container" "Hello World"`
    let div_text (cls: string) (content: string) =
        divC cls [text content]

    /// Create a span with text content.
    /// Usage: `span_text "badge" "New"`
    let span_text (cls: string) (content: string) =
        spanC cls [text content]

    /// Create a paragraph with text content.
    /// Usage: `p_text "Hello World"`
    let p_text (content: string) =
        p [text content]

    /// Create a heading with text content.
    /// Usage: `h_text 1 "Title"`
    let h_text (level: int) (content: string) =
        let tag = sprintf "h%d" level
        elem tag [] [text content]

    /// Create a link with text (not a list of children).
    /// Usage: `a_text "/page" "Click here"`
    let a_text (url: string) (textContent: string) =
        a url [text textContent]

    /// Create a link with a CSS class and text content.
    /// Usage: `a_text_c "btn" "/page" "Click"`
    let a_text_c (cls: string) (url: string) (textContent: string) =
        aC cls url [text textContent]

    /// Create an image with a CSS class.
    /// Usage: `img_c "hero" "/img.jpg" "Hero image"`
    let img_c (cls: string) (src: string) (alt: string) =
        imgC cls src alt

    /// Create a list from items using a render function.
    /// Usage: `ul_from items (fun i -> li [text i])`
    let ul_from (items: 'a list) (f: 'a -> string) =
        ul (items |> List.map (fun i -> li [f i]))

    /// Create an ordered list from items using a render function.
    /// Usage: `ol_from items (fun i -> li [text i])`
    let ol_from (items: 'a list) (f: 'a -> string) =
        ol (items |> List.map (fun i -> li [f i]))

    // ══════════════════════════════════════════════════════════════
    // ── Compound element builders ────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Build a media object (image + text side by side).
    /// Usage: `media_object "/img.jpg" "Alt" "Title" "Description"`
    let media_object (imgSrc: string) (imgAlt: string) (title: string) (desc: string) =
        divC "media" [
            imgC "media-img" imgSrc imgAlt
            divC "media-body" [
                h_text 4 title
                p_text desc
            ]
        ]

    /// Build a simple card component.
    /// Usage: `card_component "Title" "Body text" "/link" "Read more"`
    let card_component (title: string) (body: string) (linkUrl: string) (linkText: string) =
        divC "card" [
            divC "card-body" [
                h_text 4 title
                p_text body
                a_text_c "btn btn-primary" linkUrl linkText
            ]
        ]

    /// Build a hero section.
    /// Usage: `hero_section "Welcome" "This is my site" "/about" "Learn More"`
    let hero_section (title: string) (subtitle: string) (ctaUrl: string) (ctaText: string) =
        sectionC "hero" [
            divC "hero-content" [
                h_text 1 title
                p_text subtitle
                a_text_c "btn btn-lg" ctaUrl ctaText
            ]
        ]

    /// Build a grid of cards from data items.
    /// Usage: `card_grid items (fun item -> card_component item.title item.desc item.url "Read")`
    let card_grid (items: 'a list) (cardFn: 'a -> string) =
        divC "grid" (items |> List.map cardFn)

    // ══════════════════════════════════════════════════════════════
    // ── Type conversion shortcuts ────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Convert any value to a string via .ToString().
    let inline str (x: 'a) = x.ToString()

    /// Convert an integer to a string.
    let inline int_str (x: int) = string x

    /// Convert a float to a string with format.
    let float_str (format: string) (x: float) = x.ToString(format)

    /// Convert a boolean to "true" / "false".
    let bool_str (x: bool) = if x then "true" else "false"

    // ══════════════════════════════════════════════════════════════
    // ── Content validation / guard helpers ───────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Guard: if value is null or empty, return fallback; otherwise
    /// apply the render function.
    /// Usage: `guard title (fun t -> h1 [text t]) "<h1>Untitled</h1>"`
    let guard (value: string) (render: string -> string) (fallback: string) =
        if String.IsNullOrEmpty value then fallback
        else render value

    /// GuardOption: if value is Some, apply render; otherwise fallback.
    /// Usage: `guard_opt maybeTitle (fun t -> h1 [text t]) "<h1>Untitled</h1>"`
    let guard_opt (value: string option) (render: string -> string) (fallback: string) =
        match value with
        | Some v when not (String.IsNullOrEmpty v) -> render v
        | _ -> fallback

    /// Guard a list: only render if the list is non-empty.
    /// Usage: `guard_list items (fun items -> ul_from items (fun i -> li [text i])) ""`
    let guard_list (items: 'a list) (render: 'a list -> string) (fallback: string) =
        if List.isEmpty items then fallback
        else render items

    // ══════════════════════════════════════════════════════════════
    // ── Error-reporting helpers ──────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Wrap content in an HTML comment with a validation error if
    /// the condition is not met. Useful for debugging templates.
    /// Usage: `validate (not (String.IsNullOrEmpty title)) "Title is required" titleHtml`
    let validate (condition: bool) (message: string) (content: string) =
        if condition then content
        else sprintf "<!-- VALIDATION ERROR: %s -->%s" (htmlEncode message) content

    /// Assert that a required value is present; emit error comment if not.
    /// Usage: `require "title" maybeTitle`
    let require (fieldName: string) (value: string) =
        if String.IsNullOrEmpty value then
            sprintf "<!-- REQUIRED FIELD MISSING: %s -->" (htmlEncode fieldName)
        else ""

    /// Warn when a value is suspiciously short/long.
    /// Usage: `warn_if (title.Length < 10) "Title is very short" titleHtml`
    let warn_if (condition: bool) (message: string) (content: string) =
        if condition then
            sprintf "<!-- WARNING: %s -->%s" (htmlEncode message) content
        else content

    // ══════════════════════════════════════════════════════════════
    // ── Debug/trace helpers ──────────────────────────────────────
    // ══════════════════════════════════════════════════════════════

    /// Print a debug message to stderr during script evaluation.
    /// Usage: `debug "Rendering page: %s" pageTitle`
    let debug (format: string) ([<ParamArray>] args: obj[]) =
        let msg = String.Format(format, args)
        eprintfn "[Zest.DSL] %s" msg

    /// Trace the value of an expression (returns the value unchanged).
    /// Usage: `trace "title" title`
    let trace (label: string) (value: 'a) =
        eprintfn "[Zest.DSL] %s = %A" label value
        value
