namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zss

// ============================================================
// HTML DSL — Core element builders
// ============================================================

[<AutoOpen>]
module HtmlElements =

    // ---- Primitives ----
    let text  (s: string)    = Text s
    let raw   (s: string)    = Raw s
    let frag  (ns: HtmlNode list) = Fragment ns

    // ---- Void elements ----
    let br  = Element("br",    [], [])
    let hr  = Element("hr",    [], [])
    let img src alt = Element("img",   ["src", src; "alt", alt], [])

    // ---- Inline elements ----
    let a      href ch = Element("a",      ["href", href], ch)
    let aBlank href t  = Element("a",      ["href", href; "target", "_blank"; "rel", "noopener noreferrer"], [Text t])
    let aHref  href t  = Element("a",      ["href", href], [Text t])
    let span   ch      = Element("span",   [], ch)
    let strong ch      = Element("strong", [], ch)
    let em     ch      = Element("em",     [], ch)
    let code   ch      = Element("code",   [], ch)
    let small  ch      = Element("small",  [], ch)
    let mark   ch      = Element("mark",   [], ch)
    let del    ch      = Element("del",    [], ch)
    let abbr   title ch = Element("abbr",  ["title", title], ch)

    // ---- Block elements ----
    let div  ch = Element("div",  [], ch)
    let p    ch = Element("p",    [], ch)
    let h1   ch = Element("h1",   [], ch)
    let h2   ch = Element("h2",   [], ch)
    let h3   ch = Element("h3",   [], ch)
    let h4   ch = Element("h4",   [], ch)
    let h5   ch = Element("h5",   [], ch)
    let h6   ch = Element("h6",   [], ch)
    let ul   ch = Element("ul",   [], ch)
    let ol   ch = Element("ol",   [], ch)
    let li   ch = Element("li",   [], ch)
    let blockquote ch = Element("blockquote", [], ch)
    let pre  ch = Element("pre",  [], ch)
    let details summary ch = Element("details", [], Element("summary", [], [Text summary]) :: ch)

    // ---- Semantic elements ----
    let header  ch = Element("header",  [], ch)
    let footer  ch = Element("footer",  [], ch)
    let nav     ch = Element("nav",     [], ch)
    let main    ch = Element("main",    [], ch)
    let section ch = Element("section", [], ch)
    let article ch = Element("article", [], ch)
    let aside   ch = Element("aside",   [], ch)
    let figure src alt cap =
        Element("figure", [],
            [ Element("img",        ["src", src; "alt", alt], [])
              Element("figcaption", [],                       [Text cap]) ])

    // ---- Table ----
    let table  ch = Element("table",  [], ch)
    let thead  ch = Element("thead",  [], ch)
    let tbody  ch = Element("tbody",  [], ch)
    let tr     ch = Element("tr",     [], ch)
    let th     ch = Element("th",     [], ch)
    let td     ch = Element("td",     [], ch)

    // ---- Form ----
    let form   action ch     = Element("form",     ["action", action], ch)
    let input  t n v         = Element("input",    ["type", t; "name", n; "value", v], [])
    let button ch            = Element("button",   [], ch)
    let textarea name ch     = Element("textarea", ["name", name], ch)
    let select name ch       = Element("select",   ["name", name], ch)
    let option value ch      = Element("option",   ["value", value], ch)
    let label  ``for`` ch    = Element("label",    ["for", ``for``], ch)

    // ---- Form extensions ----
    let inputText  n v            = Element("input", ["type", "text"; "name", n; "value", v], [])
    let inputEmail n v            = Element("input", ["type", "email"; "name", n; "value", v], [])
    let inputPass  n v            = Element("input", ["type", "password"; "name", n; "value", v], [])
    let inputHidden n v           = Element("input", ["type", "hidden"; "name", n; "value", v], [])
    let inputCheckbox n v isChecked = Element("input", ["type", "checkbox"; "name", n; "value", v; (if isChecked then "checked" else ""), "checked"], [])
    let inputRadio n v isChecked    = Element("input", ["type", "radio"; "name", n; "value", v; (if isChecked then "checked" else ""), "checked"], [])
    let inputNumber n v           = Element("input", ["type", "number"; "name", n; "value", v], [])
    let inputDate n v             = Element("input", ["type", "date"; "name", n; "value", v], [])
    let inputRange n v            = Element("input", ["type", "range"; "name", n; "value", v], [])
    let inputColor n v            = Element("input", ["type", "color"; "name", n; "value", v], [])
    let inputFile n               = Element("input", ["type", "file"; "name", n], [])
    let inputSubmit v             = Element("input", ["type", "submit"; "value", v], [])
    let inputSearch n v            = Element("input", ["type", "search"; "name", n; "value", v], [])
    let inputTel n v               = Element("input", ["type", "tel"; "name", n; "value", v], [])
    let inputUrl n v               = Element("input", ["type", "url"; "name", n; "value", v], [])
    let fieldset ch                = Element("fieldset", [], ch)
    let legend ch                  = Element("legend", [], ch)
    let optgroup label ch          = Element("optgroup", ["label", label], ch)
    let datalist id ch             = Element("datalist", ["id", id], ch)
    let output ``for`` ch          = Element("output", ["for", ``for``], ch)
    let progress max (value: obj) ch = Element("progress", ["max", string max; "value", string value], ch)
    let meter min max (value: obj) ch = Element("meter", ["min", string min; "max", string max; "value", string value], ch)

    // ---- Media elements ----
    let video src ch               = Element("video", ["src", src], ch)
    let audio src ch               = Element("audio", ["src", src], ch)
    let sourceEl src ``type``      = Element("source", ["src", src; "type", ``type``], [])
    let trackEl src kind srclang label = Element("track", ["src", src; "kind", kind; "srclang", srclang; "label", label], [])
    let picture ch                 = Element("picture", [], ch)
    let iframe src                 = Element("iframe", ["src", src], [])
    let iframeSized src w h        = Element("iframe", ["src", src; "width", string w; "height", string h], [])
    let embed src ``type``         = Element("embed", ["src", src; "type", ``type``], [])
    let objectEl data ``type`` ch  = Element("object", ["data", data; "type", ``type``], ch)
    let param name value           = Element("param", ["name", name; "value", value], [])
    let canvas id ch               = Element("canvas", ["id", id], ch)
    let svg ch                     = Element("svg", [], ch)

    // ---- Interactive elements ----
    let dialog ch                  = Element("dialog", [], ch)
    let menu ch                    = Element("menu", [], ch)
    let menuitem ch                = Element("menuitem", [], ch)
    let summaryEl ch               = Element("summary", [], ch)
    let detailsEl ch               = Element("details", [], ch)

    // ---- Text formatting ----
    let sub ch                     = Element("sub", [], ch)
    let sup ch                     = Element("sup", [], ch)
    let ins ch                     = Element("ins", [], ch)
    let s ch                       = Element("s", [], ch)
    let q ch                       = Element("q", [], ch)
    let cite ch                    = Element("cite", [], ch)
    let dfn ch                     = Element("dfn", [], ch)
    let kbd ch                     = Element("kbd", [], ch)
    let samp ch                    = Element("samp", [], ch)
    let varEl ch                   = Element("var", [], ch)
    let bdo dir ch                 = Element("bdo", ["dir", dir], ch)
    let bdi ch                     = Element("bdi", [], ch)
    let wbr                        = Element("wbr", [], [])
    let time datetime ch           = Element("time", ["datetime", datetime], ch)

    // ---- Content grouping ----
    let dl ch                      = Element("dl", [], ch)
    let dt ch                      = Element("dt", [], ch)
    let dd ch                      = Element("dd", [], ch)
    let figureEl ch                = Element("figure", [], ch)
    let figcaption ch               = Element("figcaption", [], ch)
    let address ch                 = Element("address", [], ch)
    let hrEl                       = Element("hr", [], [])
    let brEl                       = Element("br", [], [])

    // ---- Scripting ----
    let noscript ch                = Element("noscript", [], ch)
    let templateEl id ch           = Element("template", ["id", id], ch)
    let slot name ch               = Element("slot", ["name", name], ch)

    // ---- Document structure ----
    let doctype              = Raw "<!DOCTYPE html>"
    let html   ch            = Element("html",   [], ch)
    let head   ch            = Element("head",   [], ch)
    let body   ch            = Element("body",   [], ch)
    let title  ch            = Element("title",  [], ch)
    let meta   attrs         = Element("meta",   attrs, [])
    let link   rel href      = Element("link",   ["rel", rel; "href", href], [])
    let stylesheet href      = link "stylesheet" href
    let script src           = Element("script", ["src", src], [])
    let scriptInline code    = Element("script", [], [Raw code])
    let style  css           = Element("style",  [], [Raw css])

    /// Generic element with attributes and children.
    let el tag attrs ch      = Element(tag, attrs, ch)


