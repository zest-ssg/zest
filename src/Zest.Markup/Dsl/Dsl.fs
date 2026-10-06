namespace Zest.Markup

open System

/// Module containing all DSL helpers. Auto-opened with the namespace, so
/// `open Zest.Markup` is enough to bring it into scope.
[<AutoOpen>]
module Dsl =
    open Context

    /// HTML-encodes a string to safely insert into HTML content.
    /// Escapes &, <, >, and " characters to their entity equivalents.
    let htmlEncode (s: string) =
        s.Replace("&", "&amp;")
         .Replace("<", "&lt;")
         .Replace(">", "&gt;")
         .Replace("\"", "&quot;")

    /// Converts a string to HTML-safe text content (escaped).
    let text s = htmlEncode s
    
    /// Returns raw string content without HTML encoding.
    let raw  s = s
    
    /// Empty string constant for convenient use in DSL builders.
    let empty = ""

    /// Creates an HTML attribute string with the given key and value.
    /// The value is automatically HTML-encoded for safety.
    let attr k v = sprintf "%s=\"%s\"" k (htmlEncode v)

    /// Creates an HTML element with the specified tag, attributes, and children.
    /// Attributes are concatenated with spaces, children are concatenated without separators.
    let elem tag (attrs: string list) (children: string list) =
        let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
        sprintf "<%s%s>%s</%s>" tag a (String.concat "" children) tag

    /// W3C HTML5 void elements. These are the only tags that may be emitted
    /// self-closing; every other tag needs an explicit closing tag because the
    /// parser ignores the slash on a non-void element.
    /// Mirrors `Zest.Compiler.Rendering.HtmlWriter.voidTags` (the same list for
    /// template-layer nodes) — keep the two in sync.
    let voidTags = Set.ofList [
        "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input";
        "link"; "meta"; "param"; "source"; "track"; "wbr"
    ]

    /// Creates a void HTML element (`<img … />`) with the specified tag and attributes.
    /// A tag outside `voidTags` is not void — `<script src="x" />` would leave the
    /// parser inside the element and swallow every following node — so such a tag
    /// falls back to `elem`, which emits an explicit closing tag.
    let voidElem tag (attrs: string list) =
        if not (voidTags.Contains tag) then elem tag attrs []
        else
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
    /// `<progress max="…" value="…">…</progress>` — completion of a task.
    /// `max` precedes `value` to match `Zest.Compiler.Rendering.Elements`.
    let progress (maxValue: int) (value: int) ch =
        elem "progress" [attr "max" (string maxValue); attr "value" (string value)] ch

    /// `<meter min="…" max="…" value="…">…</meter>` — a scalar gauge.
    let meter (minimum: float) (maximum: float) (value: float) ch =
        elem "meter" [attr "min" (string minimum); attr "max" (string maximum)
                      attr "value" (string value)] ch

    /// `<output for="…">…</output>` — the result of a calculation.
    let output (forVal: string) ch = elem "output" [attr "for" forVal] ch

    // ---- Form elements ----
    let fieldset ch = elem "fieldset" [] ch
    let legend ch = elem "legend" [] ch

    // ---- Media / embedded elements ----
    // `iframe` gets an explicit closing tag: the HTML parser ignores a
    // self-closing slash on non-void elements and would nest the siblings.
    let video src ch = elem "video" [attr "src" src] ch
    let audio src ch = elem "audio" [attr "src" src] ch
    let iframe src = elem "iframe" [attr "src" src] []
    let canvas id ch = elem "canvas" [attr "id" id] ch
    let svg ch = elem "svg" [] ch

    // ---- Embedded content ----
    let picture ch = elem "picture" [] ch
    /// `<source src="…" type="…" />` — a responsive media candidate.
    let source src type' = voidElem "source" [attr "src" src; attr "type" type']
    /// `<track src="…" kind="…" srclang="…" label="…" />` — a media text track.
    let track src kind srclang label =
        voidElem "track" [attr "src" src; attr "kind" kind; attr "srclang" srclang; attr "label" label]
    let embed src type' = voidElem "embed" [attr "src" src; attr "type" type']
    /// `` ``object`` `` — `object` is a reserved word, so the builder is quoted (R3).
    let ``object`` data type' ch = elem "object" [attr "data" data; attr "type" type'] ch
    let param name value = voidElem "param" [attr "name" name; attr "value" value]

    // ---- Scripting / templating ----
    let noscript ch = elem "noscript" [] ch
    let template id ch = elem "template" [attr "id" id] ch
    let slot name ch = elem "slot" [attr "name" name] ch

    // ---- Grouping / text-level semantics ----
    let menu ch = elem "menu" [] ch
    let var ch = elem "var" [] ch
    let bdo dir ch = elem "bdo" [attr "dir" dir] ch
    let bdi ch = elem "bdi" [] ch

    // ---- Class-shortcut variants for new semantic elements ----
    let figureClass cls ch = elem "figure" [attr "class" cls] ch
    let timeClass cls datetime ch = elem "time" [attr "datetime" datetime; attr "class" cls] ch
    let detailsClass cls ch = elem "details" [attr "class" cls] ch
    let dialogClass cls ch = elem "dialog" [attr "class" cls] ch
    let dlClass cls ch = elem "dl" [attr "class" cls] ch
    let citeClass cls ch = elem "cite" [attr "class" cls] ch

    // ---- Doc structure ----
    let doctype = "<!DOCTYPE html>"
    let html ch = elem "html" [] ch
    let head ch = elem "head" [] ch
    let body ch = elem "body" [] ch
    let title ch = elem "title" [] ch
    let meta attrs = voidElem "meta" attrs
    let link rel href = voidElem "link" [attr "rel" rel; attr "href" href]
    // `stylesheet` is the ZCSS computation-expression builder in `Stylesheet`;
    // for the `<link rel="stylesheet">` tag use `link "stylesheet" href`.
    // `script` is not a void element: a self-closing slash leaves the parser in
    // script-data state and swallows the rest of the document.
    let script src = elem "script" [attr "src" src] []
    let scriptInline code = elem "script" [] [raw code]
    let style css = elem "style" [] [raw css]
    // `styleZcss` lives in `InlineStyle` alone — it returns "" for blank input
    // instead of emitting an empty `<style>` tag.

    // ---- Class-shortcut helpers ----
    let divClass cls ch = elem "div" [attr "class" cls] ch
    let pClass cls ch = elem "p" [attr "class" cls] ch
    let spanClass cls ch = elem "span" [attr "class" cls] ch
    let sectionClass cls ch = elem "section" [attr "class" cls] ch
    let ulClass cls ch = elem "ul" [attr "class" cls] ch
    let olClass cls ch = elem "ol" [attr "class" cls] ch
    let liClass cls ch = elem "li" [attr "class" cls] ch
    let navClass cls ch = elem "nav" [attr "class" cls] ch
    let headerClass cls ch = elem "header" [attr "class" cls] ch
    let footerClass cls ch = elem "footer" [attr "class" cls] ch
    let mainClass cls ch = elem "main" [attr "class" cls] ch
    let articleClass cls ch = elem "article" [attr "class" cls] ch
    let asideClass cls ch = elem "aside" [attr "class" cls] ch
    let h1Class cls ch = elem "h1" [attr "class" cls] ch
    let h2Class cls ch = elem "h2" [attr "class" cls] ch
    let h3Class cls ch = elem "h3" [attr "class" cls] ch
    let h4Class cls ch = elem "h4" [attr "class" cls] ch
    let h5Class cls ch = elem "h5" [attr "class" cls] ch
    let h6Class cls ch = elem "h6" [attr "class" cls] ch
    let blockquoteClass cls ch = elem "blockquote" [attr "class" cls] ch
    let preClass cls ch = elem "pre" [attr "class" cls] ch
    let codeClass cls ch = elem "code" [attr "class" cls] ch
    let tableClass cls ch = elem "table" [attr "class" cls] ch
    let imgClass cls src alt = voidElem "img" [attr "src" src; attr "alt" alt; attr "class" cls]
    let codeBlock lang c = elem "pre" [] [elem "code" [attr "class" ("lang-" + lang)] [c]]

    // ---- Class-shortcut helpers for the remaining content elements ----
    let abbrClass cls title ch = elem "abbr" [attr "title" title; attr "class" cls] ch
    let strongClass cls ch = elem "strong" [attr "class" cls] ch
    let emClass cls ch = elem "em" [attr "class" cls] ch
    let smallClass cls ch = elem "small" [attr "class" cls] ch
    let markClass cls ch = elem "mark" [attr "class" cls] ch
    let delClass cls ch = elem "del" [attr "class" cls] ch
    let insClass cls ch = elem "ins" [attr "class" cls] ch
    let bClass cls ch = elem "b" [attr "class" cls] ch
    let iClass cls ch = elem "i" [attr "class" cls] ch
    let uClass cls ch = elem "u" [attr "class" cls] ch
    let sClass cls ch = elem "s" [attr "class" cls] ch
    let qClass cls ch = elem "q" [attr "class" cls] ch
    let subClass cls ch = elem "sub" [attr "class" cls] ch
    let supClass cls ch = elem "sup" [attr "class" cls] ch
    let kbdClass cls ch = elem "kbd" [attr "class" cls] ch
    let sampClass cls ch = elem "samp" [attr "class" cls] ch
    let dfnClass cls ch = elem "dfn" [attr "class" cls] ch
    let addressClass cls ch = elem "address" [attr "class" cls] ch
    let dtClass cls ch = elem "dt" [attr "class" cls] ch
    let ddClass cls ch = elem "dd" [attr "class" cls] ch
    let figcaptionClass cls ch = elem "figcaption" [attr "class" cls] ch
    let summaryClass cls ch = elem "summary" [attr "class" cls] ch
    let progressClass cls maxValue value ch =
        elem "progress" [attr "max" (string maxValue); attr "value" (string value)
                         attr "class" cls] ch
    let meterClass cls minimum maximum value ch =
        elem "meter" [attr "min" (string minimum); attr "max" (string maximum)
                      attr "value" (string value); attr "class" cls] ch
    let outputClass cls forVal ch = elem "output" [attr "for" forVal; attr "class" cls] ch
    let theadClass cls ch = elem "thead" [attr "class" cls] ch
    let tbodyClass cls ch = elem "tbody" [attr "class" cls] ch
    let trClass cls ch = elem "tr" [attr "class" cls] ch
    let thClass cls ch = elem "th" [attr "class" cls] ch
    let tdClass cls ch = elem "td" [attr "class" cls] ch
    let fieldsetClass cls ch = elem "fieldset" [attr "class" cls] ch
    let legendClass cls ch = elem "legend" [attr "class" cls] ch
    let videoClass cls src ch = elem "video" [attr "src" src; attr "class" cls] ch
    let audioClass cls src ch = elem "audio" [attr "src" src; attr "class" cls] ch
    let iframeClass cls src = elem "iframe" [attr "src" src; attr "class" cls] []
    let canvasClass cls id ch = elem "canvas" [attr "id" id; attr "class" cls] ch
    let svgClass cls ch = elem "svg" [attr "class" cls] ch

    // ---- Class-shortcut helpers for the embedded / scripting / bidi elements ----
    let pictureClass cls ch = elem "picture" [attr "class" cls] ch
    let sourceClass cls src type' = voidElem "source" [attr "src" src; attr "type" type'; attr "class" cls]
    let trackClass cls src kind srclang label =
        voidElem "track" [attr "src" src; attr "kind" kind; attr "srclang" srclang
                          attr "label" label; attr "class" cls]
    let embedClass cls src type' = voidElem "embed" [attr "src" src; attr "type" type'; attr "class" cls]
    let objectClass cls data type' ch = elem "object" [attr "data" data; attr "type" type'; attr "class" cls] ch
    let paramClass cls name value = voidElem "param" [attr "name" name; attr "value" value; attr "class" cls]
    let noscriptClass cls ch = elem "noscript" [attr "class" cls] ch
    let templateClass cls id ch = elem "template" [attr "id" id; attr "class" cls] ch
    let slotClass cls name ch = elem "slot" [attr "name" name; attr "class" cls] ch
    let menuClass cls ch = elem "menu" [attr "class" cls] ch
    let varClass cls ch = elem "var" [attr "class" cls] ch
    let bdoClass cls dir ch = elem "bdo" [attr "dir" dir; attr "class" cls] ch
    let bdiClass cls ch = elem "bdi" [attr "class" cls] ch

    // ---- Link shortcuts ----
    let aBlank url t = elem "a" [attr "href" url; attr "target" "_blank"; attr "rel" "noopener noreferrer"] [text t]
    let aHref url t = elem "a" [attr "href" url] [text t]
    let aClass cls url ch = elem "a" [attr "href" url; attr "class" cls] ch

    // ---- Class-shorthand aliases: C ≡ Class ----
    //
    // Naming convention: within Zest.Markup the suffix `C` is globally
    // EQUIVALENT to `Class`. Every `xxxClass` builder therefore has an `xxxC`
    // alias — `divC` ≡ `divClass`, `imgC` ≡ `imgClass`, `aC` ≡ `aClass`, …
    // Both spellings are fully interchangeable and produce identical HTML.
    // The short form exists for backwards compatibility with scripts written
    // before the `Class` rename and because it is terser to write.
    let figureC = figureClass
    let timeC = timeClass
    let detailsC = detailsClass
    let dlC = dlClass
    let citeC = citeClass
    let dialogC = dialogClass
    let divC = divClass
    let pC = pClass
    let spanC = spanClass
    let sectionC = sectionClass
    let ulC = ulClass
    let olC = olClass
    let liC = liClass
    let navC = navClass
    let headerC = headerClass
    let footerC = footerClass
    let mainC = mainClass
    let articleC = articleClass
    let asideC = asideClass
    let h1C = h1Class
    let h2C = h2Class
    let h3C = h3Class
    let h4C = h4Class
    let h5C = h5Class
    let h6C = h6Class
    let blockquoteC = blockquoteClass
    let preC = preClass
    let codeC = codeClass
    let tableC = tableClass
    let imgC = imgClass
    let aC = aClass
    let abbrC = abbrClass
    let strongC = strongClass
    let emC = emClass
    let smallC = smallClass
    let markC = markClass
    let delC = delClass
    let insC = insClass
    let bC = bClass
    let iC = iClass
    let uC = uClass
    let sC = sClass
    let qC = qClass
    let subC = subClass
    let supC = supClass
    let kbdC = kbdClass
    let sampC = sampClass
    let dfnC = dfnClass
    let addressC = addressClass
    let dtC = dtClass
    let ddC = ddClass
    let figcaptionC = figcaptionClass
    let summaryC = summaryClass
    let progressC = progressClass
    let meterC = meterClass
    let outputC = outputClass
    let theadC = theadClass
    let tbodyC = tbodyClass
    let trC = trClass
    let thC = thClass
    let tdC = tdClass
    let fieldsetC = fieldsetClass
    let legendC = legendClass
    let videoC = videoClass
    let audioC = audioClass
    let iframeC = iframeClass
    let canvasC = canvasClass
    let svgC = svgClass
    let pictureC = pictureClass
    let sourceC = sourceClass
    let trackC = trackClass
    let embedC = embedClass
    let objectC = objectClass
    let paramC = paramClass
    let noscriptC = noscriptClass
    let templateC = templateClass
    let slotC = slotClass
    let menuC = menuClass
    let varC = varClass
    let bdoC = bdoClass
    let bdiC = bdiClass

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
