namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zcss

// ============================================================
// HTML DSL — Pipe-friendly modifiers
// ============================================================

[<AutoOpen>]
module HtmlModifiers =

    // ---- Pipe-friendly modifiers ----
    /// Add a CSS class to an element (merges with existing).
    let withClass (c: string) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) ->
            let existing = attrs |> List.tryFind (fst >> (=) "class") |> Option.map snd |> Option.defaultValue ""
            let merged   = if existing = "" then c else existing + " " + c
            let newAttrs = ("class", merged) :: (attrs |> List.filter (fst >> (<>) "class"))
            Element(tag, newAttrs, ch)
        | other -> other

    /// Set inline style on an element.
    let withStyle (css: string) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) -> Element(tag, ("style", css) :: attrs, ch)
        | other -> other

    /// Set an arbitrary attribute on an element.
    let withAttr k v (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) -> Element(tag, (k, v) :: attrs, ch)
        | other -> other

    /// Set the id of an element.
    let withId (v: string) = withAttr "id" v

    /// Add multiple CSS classes at once.
    let withClasses (cs: string list) (node: HtmlNode) =
        cs |> List.fold (fun (n: HtmlNode) c -> withClass c n) node

    /// Remove a CSS class from an element.
    let withoutClass (c: string) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) ->
            let existing = attrs |> List.tryFind (fst >> (=) "class") |> Option.map snd |> Option.defaultValue ""
            let filtered = existing.Split(' ') |> Array.filter (fun x -> x <> c) |> String.concat " "
            let newAttrs = ("class", filtered) :: (attrs |> List.filter (fst >> (<>) "class"))
            Element(tag, newAttrs, ch)
        | other -> other

    /// Toggle a CSS class based on a condition.
    let toggleClass (c: string) (cond: bool) (node: HtmlNode) =
        if cond then withClass c node else node

    /// Add multiple attributes at once.
    let withAttrs (kvs: (string * string) list) (node: HtmlNode) =
        kvs |> List.fold (fun (n: HtmlNode) (k, v) -> withAttr k v n) node

    /// Wrap a node inside another element.
    let wrapIn (tag: string) (attrs: (string * string) list) (node: HtmlNode) =
        Element(tag, attrs, [node])

    /// Wrap a node in a div with a class.
    let wrapInDiv (cls: string) (node: HtmlNode) =
        wrapIn "div" ["class", cls] node

    /// Prepend a child node.
    let prependChild (child: HtmlNode) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) -> Element(tag, attrs, child :: ch)
        | other -> other

    /// Append a child node.
    let appendChild (child: HtmlNode) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) -> Element(tag, attrs, ch @ [child])
        | other -> other

    /// Replace all children of an element.
    let withChildren (children: HtmlNode list) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, _) -> Element(tag, attrs, children)
        | other -> other

    /// Map over children of an element.
    let mapChildren (f: HtmlNode -> HtmlNode) (node: HtmlNode) =
        match node with
        | Element(tag, attrs, ch) -> Element(tag, attrs, ch |> List.map f)
        | other -> other

    // ---- Conditional & list helpers ----
    let showIf  (cond: bool) (node: HtmlNode)    = Conditional(cond, node)
    let hideIf  (cond: bool) (node: HtmlNode)    = Conditional(not cond, node)
    let each    (items: 'a list) (f: 'a -> HtmlNode) = Repeat(items |> List.map f)
    let eachI   (items: 'a list) (f: int -> 'a -> HtmlNode) =
        Repeat(items |> List.mapi f)

    // ---- Conditional render with fallback ----
    let renderIf (cond: bool) (node: HtmlNode) (fallback: HtmlNode) =
        if cond then node else fallback

    // ---- Optional value rendering ----
    let renderOpt (opt: 'a option) (f: 'a -> HtmlNode) =
        opt |> Option.map f |> Option.defaultValue (Text "")

    // ---- List rendering with separator ----
    let joinWith (separator: HtmlNode) (nodes: HtmlNode list) =
        nodes |> List.collect (fun n -> [n; separator]) |> List.truncate (nodes.Length * 2 - 1)

    // ---- Class-shortcut constructors ----
    let divC    c ch = div    ch |> withClass c
    let spanC   c ch = span   ch |> withClass c
    let pC      c ch = p      ch |> withClass c
    let sectionC c ch = section ch |> withClass c
    let articleC c ch = article ch |> withClass c
    let navC    c ch = nav    ch |> withClass c
    let headerC c ch = header ch |> withClass c
    let footerC c ch = footer ch |> withClass c
    let mainC   c ch = main   ch |> withClass c
    let asideC  c ch = aside  ch |> withClass c
    let ulC     c ch = ul     ch |> withClass c
    let olC     c ch = ol     ch |> withClass c
    let liC     c ch = li     ch |> withClass c
    let aC c href ch = a href ch |> withClass c
    let h1C c ch = h1 ch |> withClass c
    let h2C c ch = h2 ch |> withClass c
    let h3C c ch = h3 ch |> withClass c
    let h4C c ch = h4 ch |> withClass c
    let h5C c ch = h5 ch |> withClass c
    let h6C c ch = h6 ch |> withClass c
    let blockquoteC c ch = blockquote ch |> withClass c
    let preC c ch = pre ch |> withClass c
    let codeC c ch = code ch |> withClass c
    let tableC c ch = table ch |> withClass c
    let formC c action ch = form action ch |> withClass c
    let buttonC c ch = button ch |> withClass c
    let labelC c ``for`` ch = label ``for`` ch |> withClass c
    let fieldsetC c ch = fieldset ch |> withClass c
    let figureC c ch = figureEl ch |> withClass c
    let detailsC c ch = detailsEl ch |> withClass c
    let dialogC c ch = dialog ch |> withClass c
    let imgC c src alt = img src alt |> withClass c
    let videoC c src ch = video src ch |> withClass c
    let audioC c src ch = audio src ch |> withClass c
    let iframeC c src = iframe src |> withClass c
    let canvasC c id ch = canvas id ch |> withClass c
    let svgC c ch = svg ch |> withClass c

    // ---- ID-shortcut constructors ----
    let divId    i ch = div    ch |> withId i
    let spanId   i ch = span   ch |> withId i
    let sectionId i ch = section ch |> withId i
    let mainId   i ch = main   ch |> withId i
    let navId    i ch = nav    ch |> withId i
    let headerId i ch = header ch |> withId i
    let footerId i ch = footer ch |> withId i
    let articleId i ch = article ch |> withId i
    let asideId  i ch = aside  ch |> withId i
    let formId   i action ch = form action ch |> withId i

    // ---- Combined class+id constructors ----
    let divCI c i ch = div ch |> withClass c |> withId i
    let sectionCI c i ch = section ch |> withClass c |> withId i
    let spanCI c i ch = span ch |> withClass c |> withId i
    let pCI c i ch = p ch |> withClass c |> withId i

    // ---- Empty state ----
    let emptyState message =
        divC "empty-state" [
            p [Text message]
        ]

    // ---- Conditional class modifiers ----
    /// Add a CSS class only when the condition is true.
    let whenClass (c: string) (cond: bool) (node: HtmlNode) =
        if cond then withClass c node else node

    /// Add a CSS class only when the condition is false.
    let unlessClass (c: string) (cond: bool) (node: HtmlNode) =
        if not cond then withClass c node else node

    // ══════════════════════════════════════════════════════════════
    // ── Shorthand node builders (from DslSugar) ──────────────────
    // ══════════════════════════════════════════════════════════════

    /// Create a Text node: `t "Hello"`
    let t (s: string) = Text s

    /// Create a Raw HTML node: `r "<br>"`
    let r (s: string) = Raw s

    /// Create an Element node with attributes and children.
    /// Usage: `e "div" ["class", "hero"] [t "Hello"]`
    let e (tag: string) (attrs: (string * string) list) (children: HtmlNode list) =
        Element(tag, attrs, children)

    /// Create a void Element (no children).
    /// Usage: `ve "br" []`
    let ve (tag: string) (attrs: (string * string) list) =
        Element(tag, attrs, [])

    /// Element with just a class attribute.
    /// Usage: `ec "div" "hero" [t "Hello"]`
    let ec (tag: string) (cls: string) (children: HtmlNode list) =
        Element(tag, ["class", cls], children)

    /// Element with just text content.
    /// Usage: `et "h1" "Hello World"`
    let et (tag: string) (textContent: string) =
        Element(tag, [], [Text textContent])

    /// Element with class and text content.
    /// Usage: `ect "p" "lead" "Hello"`
    let ect (tag: string) (cls: string) (textContent: string) =
        Element(tag, ["class", cls], [Text textContent])

    /// Fragment (list of nodes rendered sequentially).
    /// Usage: `f [t "A"; br; t "B"]`
    let f (nodes: HtmlNode list) = Fragment nodes

    /// Conditional node: rendered only if condition is true.
    /// Usage: `cnd (x > 0) (t "positive")`
    let cnd (condition: bool) (node: HtmlNode) =
        Conditional(condition, node)

    /// Conditional with else: choose between two nodes.
    /// Usage: `cnd_else (x > 0) (t "positive") (t "non-positive")`
    let cnd_else (condition: bool) (ifTrue: HtmlNode) (ifFalse: HtmlNode) =
        if condition then ifTrue else ifFalse

    /// Repeated nodes from a list.
    /// Usage: `rep (items |> List.map (fun i -> li [t i]))`
    let rep (nodes: HtmlNode list) = Repeat nodes

    // ══════════════════════════════════════════════════════════════
    // ── Common HTML element shortcuts (from DslSugar) ────────────
    // ══════════════════════════════════════════════════════════════

    /// `<br>` void element.
    let br = Element("br", [], [])

    /// `<hr>` void element.
    let hr = Element("hr", [], [])

    /// `<img>` void element.
    let img (src: string) (alt: string) =
        Element("img", ["src", src; "alt", alt], [])

    /// `<img>` with class.
    let img_c (cls: string) (src: string) (alt: string) =
        Element("img", ["src", src; "alt", alt; "class", cls], [])

    /// `<a>` with href and text.
    let a_href (url: string) (textContent: string) =
        Element("a", ["href", url], [Text textContent])

    /// `<a>` with target="_blank".
    let a_blank (url: string) (textContent: string) =
        Element("a", ["href", url; "target", "_blank"; "rel", "noopener noreferrer"], [Text textContent])

    /// `<h1>`-`<h6>` with text.
    let h (level: int) (textContent: string) =
        let tag = sprintf "h%d" level
        Element(tag, [], [Text textContent])

    /// `<p>` with text content.
    let p_text (content: string) =
        Element("p", [], [Text content])

    /// `<li>` with text.
    let li_text (content: string) =
        Element("li", [], [Text content])

    /// `<style>` block.
    let style_block (css: string) =
        Element("style", [], [Raw css])

    /// `<script>` block.
    let script_block (js: string) =
        Element("script", [], [Raw js])

    // ══════════════════════════════════════════════════════════════
    // ── Pipeline / chaining operators (from DslSugar) ────────────
    // ══════════════════════════════════════════════════════════════

    /// Wrap nodes in an element tag.
    /// Usage: `nodes |> wrap_in "div"`
    let wrap_in (tag: string) (nodes: HtmlNode list) : HtmlNode =
        Element(tag, [], nodes)

    /// Wrap nodes in an element with a class.
    /// Usage: `nodes |> wrap_in_c "div" "container"`
    let wrap_in_c (tag: string) (cls: string) (nodes: HtmlNode list) : HtmlNode =
        Element(tag, ["class", cls], nodes)

    /// Add a CSS class to an element node (replaces existing class).
    /// Usage: `elem |> with_class "active"`
    let with_class (cls: string) (node: HtmlNode) : HtmlNode =
        match node with
        | Element(tag, attrs, children) ->
            let cleaned = attrs |> List.filter (fun (k, _) -> k <> "class")
            Element(tag, ("class", cls) :: cleaned, children)
        | other -> other

    /// Add an attribute to an element node (replaces existing if same key).
    /// Usage: `elem |> with_attr "data-id" "42"`
    let with_attr (key: string) (value: string) (node: HtmlNode) : HtmlNode =
        match node with
        | Element(tag, attrs, children) ->
            let cleaned = attrs |> List.filter (fun (k, _) -> k <> key)
            Element(tag, (key, value) :: cleaned, children)
        | other -> other

    // ══════════════════════════════════════════════════════════════
    // ── Validation / error-reporting helpers (from DslSugar) ─────
    // ══════════════════════════════════════════════════════════════

    /// Validate a condition and emit an HTML comment with the error
    /// message if the condition is not met.
    /// Usage: `validate_cond (not (String.IsNullOrEmpty title)) "Title is required" renderedTitle`
    let validate_cond (condition: bool) (message: string) (node: HtmlNode) : HtmlNode =
        if condition then node
        else Fragment [Raw (sprintf "<!-- VALIDATION ERROR: %s -->" message); node]

    /// Guard: render only if the string value is non-empty.
    /// Usage: `guard_str title (fun t -> h 1 t)`
    let guard_str (value: string) (render: string -> HtmlNode) : HtmlNode =
        if String.IsNullOrEmpty value then Fragment []
        else render value

    /// Guard for optional values.
    /// Usage: `guard_opt maybeTitle (fun t -> h 1 t) (p_text "Untitled")`
    let guard_opt (value: string option) (render: string -> HtmlNode) (fallback: HtmlNode) : HtmlNode =
        match value with
        | Some v when not (String.IsNullOrEmpty v) -> render v
        | _ -> fallback

    /// Guard a list: render only if non-empty.
    /// Usage: `guard_list items (fun items -> ul (items |> List.map li_text)) (p_text "No items")`
    let guard_list (items: 'a list) (render: 'a list -> HtmlNode) (fallback: HtmlNode) : HtmlNode =
        if List.isEmpty items then fallback
        else render items
