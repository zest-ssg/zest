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
