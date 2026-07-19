namespace Zest.Dsl

open System

/// Module containing all DSL helpers — opened by user scripts
module Dsl =
    open Context

    let htmlEncode (s: string) =
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")

    let text s = htmlEncode s
    let raw  s = s

    let attr k v = sprintf "%s=\"%s\"" k (htmlEncode v)

    let elem tag (attrs: string list) (children: string list) =
        let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
        sprintf "<%s%s>%s</%s>" tag a (String.concat "" children) tag

    let voidElem tag (attrs: string list) =
        let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
        sprintf "<%s%s />" tag a

    // ---- Inline elements ----
    let a url (ch: string list) = elem "a" [attr "href" url] ch
    let span (ch: string list) = elem "span" [] ch
    let code (ch: string list) = elem "code" [] ch
    let strong (ch: string list) = elem "strong" [] ch
    let em (ch: string list) = elem "em" [] ch
    let small ch = elem "small" [] ch
    let mark ch = elem "mark" [] ch
    let del ch = elem "del" [] ch
    let abbr title ch = elem "abbr" [attr "title" title] ch

    // ---- Void elements ----
    let img src alt = voidElem "img" [attr "src" src; attr "alt" alt]
    let br () = voidElem "br" []
    let hr () = voidElem "hr" []

    // ---- Block elements ----
    let h1 ch = elem "h1" [] ch
    let h2 ch = elem "h2" [] ch
    let h3 ch = elem "h3" [] ch
    let h4 ch = elem "h4" [] ch
    let h5 ch = elem "h5" [] ch
    let h6 ch = elem "h6" [] ch
    let p ch = elem "p" [] ch
    let div ch = elem "div" [] ch
    let section ch = elem "section" [] ch
    let article ch = elem "article" [] ch
    let nav ch = elem "nav" [] ch
    let header ch = elem "header" [] ch
    let footer ch = elem "footer" [] ch
    let main ch = elem "main" [] ch
    let ul ch = elem "ul" [] ch
    let ol ch = elem "ol" [] ch
    let li ch = elem "li" [] ch
    let blockquote ch = elem "blockquote" [] ch
    let pre ch = elem "pre" [] ch
    let table ch = elem "table" [] ch
    let thead ch = elem "thead" [] ch
    let tbody ch = elem "tbody" [] ch
    let tr ch = elem "tr" [] ch
    let th ch = elem "th" [] ch
    let td ch = elem "td" [] ch

    // ---- Description lists ----
    let dl ch = elem "dl" [] ch
    let dt ch = elem "dt" [] ch
    let dd ch = elem "dd" [] ch

    // ---- Additional semantic / inline elements ----
    let aside ch = elem "aside" [] ch
    let figure ch = elem "figure" [] ch
    let figcaption ch = elem "figcaption" [] ch
    let address ch = elem "address" [] ch
    let cite ch = elem "cite" [] ch
    let q ch = elem "q" [] ch
    let sub ch = elem "sub" [] ch
    let sup ch = elem "sup" [] ch
    let kbd ch = elem "kbd" [] ch
    let samp ch = elem "samp" [] ch
    let dfn ch = elem "dfn" [] ch
    let ins ch = elem "ins" [] ch
    let b ch = elem "b" [] ch
    let i ch = elem "i" [] ch
    let u ch = elem "u" [] ch
    let s ch = elem "s" [] ch
    let wbr () = voidElem "wbr" []

    /// `<time datetime="...">label</time>` — machine-readable timestamp.
    let time (datetime: string) (ch: string list) = elem "time" [attr "datetime" datetime] ch
    let summary ch = elem "summary" [] ch
    let details ch = elem "details" [] ch
    let dialog ch = elem "dialog" [] ch
    let progress ch = elem "progress" [] ch
    let meter ch = elem "meter" [] ch
    let output ch = elem "output" [] ch

    // ---- Class-shortcut variants for new semantic elements ----
    let figureC cls ch = elem "figure" [attr "class" cls] ch
    let timeC cls datetime ch = elem "time" [attr "datetime" datetime; attr "class" cls] ch
    let detailsC cls ch = elem "details" [attr "class" cls] ch
    let dlC cls ch = elem "dl" [attr "class" cls] ch
    let citeC cls ch = elem "cite" [attr "class" cls] ch

    // ---- Doc structure ----
    let doctype = "<!DOCTYPE html>"
    let html ch = elem "html" [] ch
    let head ch = elem "head" [] ch
    let body ch = elem "body" [] ch
    let title ch = elem "title" [] ch
    let meta attrs = voidElem "meta" attrs
    let link rel href = voidElem "link" [attr "rel" rel; attr "href" href]
    let stylesheet href = link "stylesheet" href
    let script src = voidElem "script" [attr "src" src]
    let scriptInline code = elem "script" [] [raw code]
    let style css = elem "style" [] [raw css]

    /// Convenience re-export — inlines a compiled ZCSS stylesheet string
    /// (output of `stylesheet { ... }` computation expression).
    /// Equivalent to `styleZcss` from DslStyle.
    let styleZcss (compiledCss: string) = elem "style" [] [raw ("\n" + compiledCss + "\n")]

    // ---- Class-shortcut helpers ----
    let divC cls ch = elem "div" [attr "class" cls] ch
    let pC cls ch = elem "p" [attr "class" cls] ch
    let spanC cls ch = elem "span" [attr "class" cls] ch
    let sectionC cls ch = elem "section" [attr "class" cls] ch
    let ulC cls ch = elem "ul" [attr "class" cls] ch
    let olC cls ch = elem "ol" [attr "class" cls] ch
    let liC cls ch = elem "li" [attr "class" cls] ch
    let navC cls ch = elem "nav" [attr "class" cls] ch
    let headerC cls ch = elem "header" [attr "class" cls] ch
    let footerC cls ch = elem "footer" [attr "class" cls] ch
    let mainC cls ch = elem "main" [attr "class" cls] ch
    let articleC cls ch = elem "article" [attr "class" cls] ch
    let asideC cls ch = elem "aside" [attr "class" cls] ch
    let h1C cls ch = elem "h1" [attr "class" cls] ch
    let h2C cls ch = elem "h2" [attr "class" cls] ch
    let h3C cls ch = elem "h3" [attr "class" cls] ch
    let blockquoteC cls ch = elem "blockquote" [attr "class" cls] ch
    let preC cls ch = elem "pre" [attr "class" cls] ch
    let codeC cls ch = elem "code" [attr "class" cls] ch
    let tableC cls ch = elem "table" [attr "class" cls] ch
    let imgC cls src alt = voidElem "img" [attr "src" src; attr "alt" alt; attr "class" cls]
    let codeBlock lang c = elem "pre" [] [elem "code" [attr "class" ("lang-" + lang)] [c]]

    // ---- Link shortcuts ----
    let aBlank url t = elem "a" [attr "href" url; attr "target" "_blank"; attr "rel" "noopener noreferrer"] [text t]
    let aHref url t = elem "a" [attr "href" url] [text t]
    let aC cls url ch = elem "a" [attr "href" url; attr "class" cls] ch

    // ---- Conditional helpers ----
    let showIf cond ch = if cond then ch else ""
    let hideIf cond ch = if cond then "" else ch
    let render (nodes: string list) = printf "%s" (String.concat "\n" nodes)

    // ---- Safety helpers ──────────────────────────────────────────
    // `htmlSafe` escapes text for insertion into HTML element content.
    // `attrSafe` additionally escapes single quotes so the value is safe
    // inside both single- and double-quoted attribute values.

    /// HTML-escape text for safe insertion into element content.
    /// Escapes &, <, > (and " for attribute compatibility).
    let htmlSafe (s: string) = htmlEncode s

    /// Escape a value for safe use inside an HTML attribute value.
    /// Escapes &, <, >, " and '.
    let attrSafe (s: string) =
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")
         .Replace("'", "&#39;")

    /// URL-encode a value for use in href / query strings.
    let urlSafe (s: string) = Uri.EscapeDataString(s)

    /// Escape text for safe insertion into a <script> JSON context.
    /// Escapes </, <, U+2028, U+2029 which break inline scripts.
    let jsSafe (s: string) =
        s.Replace("</", "<\\/")
         .Replace("\u2028", "\\u2028")
         .Replace("\u2029", "\\u2029")

    // ---- Attribute sugar ───────────────────────────────────────
    // Concise builders for the most common attributes, so authors can write
    // `div [id' "main"; cls "page"; data' "page" "home"] [...]` instead of
    // spelling out `attr "id" "main"` each time.

    /// `class="…"` attribute.
    let cls (c: string) = attr "class" c
    /// `id="…"` attribute.
    let id' (v: string) = attr "id" v
    /// `role="…"` attribute (ARIA landmark roles).
    let role (v: string) = attr "role" v
    /// `href="…"` attribute.
    let href (v: string) = attr "href" v
    /// `src="…"` attribute.
    let src (v: string) = attr "src" v
    /// `type="…"` attribute.
    let type' (v: string) = attr "type" v
    /// `name="…"` attribute.
    let name' (v: string) = attr "name" v
    /// `value="…"` attribute.
    let value' (v: string) = attr "value" v
    /// `placeholder="…"` attribute.
    let placeholder (v: string) = attr "placeholder" v
    /// `title="…"` attribute (tooltip).
    let title' (v: string) = attr "title" v
    /// `width="…"` attribute.
    let width' (v: string) = attr "width" v
    /// `height="…"` attribute.
    let height' (v: string) = attr "height" v
    /// `alt="…"` attribute.
    let alt (v: string) = attr "alt" v
    /// `lang="…"` attribute.
    let lang (v: string) = attr "lang" v
    /// `tabindex="…"` attribute.
    let tabindex (v: string) = attr "tabindex" v

    /// `data-KEY="VALUE"` — HTML data attribute. `data' "id" "42"` →
    /// `data-id="42"`. The key is inserted verbatim (use kebab-case).
    let data' (key: string) (v: string) = attr ("data-" + key) v

    /// `aria-KEY="VALUE"` — ARIA accessibility attribute.
    /// `aria "label" "Close"` → `aria-label="Close"`.
    let aria (key: string) (v: string) = attr ("aria-" + key) v

    /// Boolean attribute (present when `true`, omitted when `false`).
    /// `boolAttr "disabled" true` → `disabled="disabled"`; `… false` → `""`.
    let boolAttr (name: string) (on: bool) =
        if on then sprintf "%s=\"%s\"" name name else ""

    // ---- Misc string helpers ───────────────────────────────────

    /// An HTML comment `<!-- … -->`. The body is NOT escaped — do not embed
    /// user input containing `-->`.
    let comment (body: string) = sprintf "<!-- %s -->" body

    /// Non-breaking space entity.
    let nbsp = "&nbsp;"

    /// Concatenate child nodes WITHOUT a wrapping element (a "fragment"),
    /// useful when a parent already provides the container.
    let fragment (children: string list) = String.concat "" children

    /// Join child nodes with a newline separator (pretty-printed block).
    let fragmentLines (children: string list) = String.concat "\n" children

    /// Render a list of key/value attribute pairs into an attribute string.
    /// `attrsOf ["id","main"; "class","page"]` → `id="main" class="page"`.
    let attrsOf (pairs: (string * string) list) =
        pairs |> List.map (fun (k, v) -> attr k v) |> String.concat " "

    /// Build an element from a tag, a list of (key,value) attribute pairs,
    /// and children. A concise general-purpose constructor.
    /// `el "div" [("id","main"); ("class","page")] [text "Hi"]`
    let el (tag: string) (pairs: (string * string) list) (children: string list) =
        elem tag (pairs |> List.map (fun (k, v) -> attr k v)) children

    /// Void element from a tag and (key,value) attribute pairs.
    let elVoid (tag: string) (pairs: (string * string) list) =
        voidElem tag (pairs |> List.map (fun (k, v) -> attr k v))
