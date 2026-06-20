namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zss

// ============================================================
// HTML DSL — pipe-friendly element builders
// ============================================================

[<AutoOpen>]
module HtmlDsl =

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

    // ---- Attribute helpers ----
    let attr   k v           = (k, v)
    let id     v             = attr "id"    v
    let cls    v             = attr "class" v
    let styleA v             = attr "style" v
    let href   v             = attr "href"  v
    let src    v             = attr "src"   v
    let alt    v             = attr "alt"   v
    let rel    v             = attr "rel"   v
    let target v             = attr "target" v
    let data   k v           = attr ("data-" + k) v
    let ariaLabel v          = attr "aria-label" v
    let role   v             = attr "role"  v

    /// Generic element with attributes and children.
    let el tag attrs ch      = Element(tag, attrs, ch)

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

    // ---- Class-shortcut constructors ----
    let divC    c ch = div    ch |> withClass c
    let spanC   c ch = span   ch |> withClass c
    let pC      c ch = p      ch |> withClass c
    let sectionC c ch = section ch |> withClass c
    let articleC c ch = article ch |> withClass c
    let navC    c ch = nav    ch |> withClass c

    // ---- Conditional & list helpers ----
    let showIf  (cond: bool) (node: HtmlNode)    = Conditional(cond, node)
    let hideIf  (cond: bool) (node: HtmlNode)    = Conditional(not cond, node)
    let each    (items: 'a list) (f: 'a -> HtmlNode) = Repeat(items |> List.map f)
    let eachI   (items: 'a list) (f: int -> 'a -> HtmlNode) =
        Repeat(items |> List.mapi f)

    // ---- Code block ----
    let codeBlock lang (code: string) =
        Element("pre", [],
            [Element("code", ["class", "language-" + lang],
                [Raw (WebUtility.HtmlEncode code)])])

    // ---- Card / badge / alert convenience ----
    let card    ch = divC "card"  ch
    let badge   t  = spanC "badge" [Text t]
    let alert   level ch = divC ("alert alert-" + level) ch

// ============================================================
// HTML Renderer
// ============================================================

module HtmlRenderer =

    let private voidTags = set ["area";"base";"br";"col";"embed";"hr";"img";"input";"link";"meta";"param";"source";"track";"wbr"]

    let rec renderNode (node: HtmlNode) : string =
        match node with
        | Text s -> WebUtility.HtmlEncode s
        | Raw  s -> s
        | Fragment ns     -> ns |> List.map renderNode |> String.concat ""
        | Conditional(true,  n) -> renderNode n
        | Conditional(false, _) -> ""
        | Repeat items         -> items |> List.map renderNode |> String.concat ""
        | Element(tag, attrs, ch) ->
            let attrStr =
                attrs
                |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k (WebUtility.HtmlEncode v))
                |> String.concat ""
            if voidTags.Contains tag then sprintf "<%s%s />" tag attrStr
            else
                let inner = ch |> List.map renderNode |> String.concat ""
                sprintf "<%s%s>%s</%s>" tag attrStr inner tag

    let render (nodes: HtmlNode list) : string =
        nodes |> List.map renderNode |> String.concat ""

// ============================================================
// Page Computation Expression
// ============================================================

type PageBuilder () =
    member _.Yield _ = Page.empty
    member _.Run(state: Page) =
        { state with Content = state.ContentNodes |> HtmlRenderer.render }

    [<CustomOperation "layout">]
    member _.Layout(s, v)    = { s with Layout = Some v }
    [<CustomOperation "title">]
    member _.Title(s, v)     = { s with Title = v }
    [<CustomOperation "permalink">]
    member _.Permalink(s, v) = { s with Permalink = Some v; Url = v }
    [<CustomOperation "slug">]
    member _.Slug(s, v)      = { s with Slug = v }
    [<CustomOperation "tags">]
    member _.Tags(s, v)      = { s with Tags = v }
    [<CustomOperation "tag">]
    member _.Tag(s, v)       = { s with Tags = v :: s.Tags }
    [<CustomOperation "date">]
    member _.Date(s, v)      = { s with Date = Some v }
    [<CustomOperation "description">]
    member _.Description(s, v) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.["description"] <- box v
        { s with Data = d }
    [<CustomOperation "data">]
    member _.Data(s, key, value : obj) =
        let d = Dictionary<string, obj>()
        for kv in s.Data do d.[kv.Key] <- kv.Value
        d.[key] <- value
        { s with Data = d }
    [<CustomOperation "content">]
    member _.Content(s, nodes : HtmlNode list) =
        { s with ContentNodes = nodes }

module PageDsl =
    /// Computation expression entry point: `page { title "..."; content [...] }`.
    let page = PageBuilder()

// ============================================================
// Markdown → HTML
// ============================================================

module Markdown =
    let private codeBlockPat    = Regex(@"(?s)```(\w*)\n(.*?)```",       RegexOptions.Compiled)
    let private inlineCodePat   = Regex(@"(?<!`)`([^`]+)`(?!`)",         RegexOptions.Compiled)
    let private boldPat         = Regex(@"\*\*(.+?)\*\*",                RegexOptions.Compiled)
    let private italicPat       = Regex(@"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", RegexOptions.Compiled)
    let private strikePat       = Regex(@"~~(.+?)~~",                    RegexOptions.Compiled)
    let private linkPat         = Regex(@"\[([^\]]+)\]\(([^)]+)\)",      RegexOptions.Compiled)
    let private imagePat        = Regex(@"!\[([^\]]*)\]\(([^)]+)\)",     RegexOptions.Compiled)
    let private hrPat           = Regex(@"^(---|\*\*\*|___)\s*$",        RegexOptions.Multiline ||| RegexOptions.Compiled)

    let private processInline (text: string) =
        let enc = WebUtility.HtmlEncode
        imagePat   .Replace(text,   fun m -> sprintf """<img src="%s" alt="%s" />""" (enc m.Groups.[2].Value) (enc m.Groups.[1].Value))
        |> fun s -> linkPat.Replace(s,   fun m -> sprintf """<a href="%s">%s</a>""" (enc m.Groups.[2].Value) (enc m.Groups.[1].Value))
        |> fun s -> boldPat.Replace(s,   fun m -> sprintf "<strong>%s</strong>" (enc m.Groups.[1].Value))
        |> fun s -> italicPat.Replace(s, fun m -> sprintf "<em>%s</em>" (enc m.Groups.[1].Value))
        |> fun s -> strikePat.Replace(s, fun m -> sprintf "<del>%s</del>" (enc m.Groups.[1].Value))
        |> fun s -> inlineCodePat.Replace(s, fun m -> sprintf "<code>%s</code>" (enc m.Groups.[1].Value))

    let private parseTableRow (line: string) =
        line.Trim().Trim('|').Split('|')
        |> Array.map (fun c -> c.Trim())

    let private isTableSep (line: string) =
        Regex.IsMatch(line.Trim(), @"^\|?[\s\-|:]+\|?$")

    let toHtml (markdown: string) : string =
        if String.IsNullOrEmpty markdown then ""
        else
            // Protect code blocks
            let codeBlocks = Dictionary<int, string>()
            let mutable idx = 0
            let afterCode =
                codeBlockPat.Replace(markdown, fun m ->
                    let lang  = m.Groups.[1].Value
                    let code  = m.Groups.[2].Value
                    let langAttr = if String.IsNullOrEmpty lang then "" else sprintf """ class="language-%s" """ lang
                    let ph = sprintf "```ZEST_CB_%d```" idx
                    codeBlocks.[idx] <- sprintf "<pre><code%s>%s</code></pre>" langAttr (WebUtility.HtmlEncode code)
                    idx <- idx + 1
                    ph)

            let lines = afterCode.Split([| "\r\n"; "\n"; "\r" |], StringSplitOptions.None)
            let html  = ResizeArray<string>()
            let para  = ResizeArray<string>()
            let listU = ResizeArray<string>()   // unordered
            let listO = ResizeArray<string>()   // ordered
            let mutable inP = false
            let mutable inU = false
            let mutable inO = false

            let flushP () =
                if inP && para.Count > 0 then
                    html.Add(sprintf "<p>%s</p>" (String.Join(" ", para) |> processInline))
                    para.Clear(); inP <- false
            let flushU () =
                if inU && listU.Count > 0 then
                    html.Add("<ul>")
                    for item in listU do html.Add(sprintf "<li>%s</li>" (processInline item))
                    html.Add("</ul>"); listU.Clear(); inU <- false
            let flushO () =
                if inO && listO.Count > 0 then
                    html.Add("<ol>")
                    for item in listO do html.Add(sprintf "<li>%s</li>" (processInline item))
                    html.Add("</ol>"); listO.Clear(); inO <- false
            let flushAll () = flushP(); flushU(); flushO()

            let mutable i = 0
            while i < lines.Length do
                let line = lines.[i].TrimEnd()
                let cbMatch = Regex.Match(line, @"^```ZEST_CB_(\d+)```$")
                if cbMatch.Success then
                    flushAll()
                    html.Add(codeBlocks.[int cbMatch.Groups.[1].Value])
                elif hrPat.IsMatch line then flushAll(); html.Add("<hr />")
                elif Regex.IsMatch(line, @"^#{1,6}\s") then
                    flushAll()
                    let m = Regex.Match(line, @"^(#{1,6})\s+(.+)$")
                    let lvl = m.Groups.[1].Value.Length
                    let cnt = processInline m.Groups.[2].Value
                    // anchor id from slug
                    let anchorId = Regex.Replace(cnt.ToLowerInvariant(), @"[^\w]+", "-").Trim('-')
                    html.Add(sprintf "<h%d id=\"%s\">%s</h%d>" lvl anchorId cnt lvl)
                elif line.StartsWith "> " then
                    flushAll()
                    html.Add(sprintf "<blockquote><p>%s</p></blockquote>" (processInline line.[2..]))
                // Table: look ahead for separator row
                elif line.Contains("|") && i + 1 < lines.Length && isTableSep lines.[i + 1] then
                    flushAll()
                    let headers = parseTableRow line
                    html.Add("<table><thead><tr>")
                    for h in headers do html.Add(sprintf "<th>%s</th>" (processInline h))
                    html.Add("</tr></thead><tbody>")
                    i <- i + 2  // skip separator row
                    while i < lines.Length && lines.[i].Contains("|") do
                        html.Add("<tr>")
                        for c in parseTableRow lines.[i] do html.Add(sprintf "<td>%s</td>" (processInline c))
                        html.Add("</tr>")
                        i <- i + 1
                    html.Add("</tbody></table>")
                    i <- i - 1  // outer i <- i+1 will correct
                // Unordered list
                elif (let t = line.TrimStart() in t.StartsWith("- ") || t.StartsWith("* ")) then
                    flushP(); flushO()
                    if not inU then inU <- true; listU.Clear()
                    let t = line.TrimStart()
                    listU.Add(t.[2..].Trim())
                // Ordered list
                elif Regex.IsMatch(line.TrimStart(), @"^\d+\.\s") then
                    flushP(); flushU()
                    if not inO then inO <- true; listO.Clear()
                    listO.Add(Regex.Replace(line.TrimStart(), @"^\d+\.\s+", ""))
                elif String.IsNullOrWhiteSpace line then flushAll()
                else
                    flushU(); flushO()
                    inP <- true; para.Add(line)
                i <- i + 1

            flushAll()
            String.Join("\n", html)

// ============================================================
// HtmlUtils — helpers for use inside .zest.fsx templates
// ============================================================

module HtmlUtils =

    /// Inline Markdown string as an HtmlNode.
    let md (markdownText: string) : HtmlNode =
        Raw(Markdown.toHtml markdownText)

    /// Compile a ZSS snippet inline as a `<style>` node.
    let styleBlock (zssSource: string) : HtmlNode =
        Element("style", [], [Raw(Zss.Processor.processText zssSource)])

    /// Reference an external stylesheet (.zss → .css auto-rewritten).
    let stylesheet (href: string) : HtmlNode =
        let cssHref =
            if href.EndsWith(".zss", StringComparison.OrdinalIgnoreCase)
            then href.[..href.Length - 5] + "css"
            else href
        Element("link", ["rel", "stylesheet"; "href", cssHref], [])

    /// Reference an external script.
    let jsFile (src: string) : HtmlNode =
        Element("script", ["src", src], [])

    /// Inline JavaScript as a `<script>` node.
    let jsInline (code: string) : HtmlNode =
        Element("script", [], [Raw code])

    /// Generate a `<meta>` charset declaration.
    let charset (enc: string) : HtmlNode =
        Element("meta", ["charset", enc], [])

    /// Generate an Open Graph `<meta>` tag.
    let ogMeta (property: string) (content: string) : HtmlNode =
        Element("meta", ["property", "og:" + property; "content", content], [])

    /// Render a simple two-column definition list from a list of (term, definition) pairs.
    let dl (pairs: (string * string) list) : HtmlNode =
        Element("dl", [],
            pairs |> List.collect (fun (t, d) ->
                [ Element("dt", [], [Text t])
                  Element("dd", [], [Text d]) ]))

    /// Breadcrumb nav from a list of (label, url) pairs.
    let breadcrumb (items: (string * string) list) : HtmlNode =
        Element("nav", ["aria-label", "breadcrumb"],
            [ Element("ol", ["class", "breadcrumb"],
                items |> List.mapi (fun i (label, url) ->
                    let isLast = i = items.Length - 1
                    if isLast then Element("li", ["class", "breadcrumb-item active"], [Text label])
                    else Element("li", ["class", "breadcrumb-item"],
                             [Element("a", ["href", url], [Text label])]))) ])

// ============================================================
// 11ty.js-style Shortcodes
// ============================================================

/// 短代码注册表：允许用户定义可复用的模板片段。
type ShortcodeFunc = IDictionary<string, obj> -> string -> string

module ShortcodeRegistry =

    let private store = Dictionary<string, ShortcodeFunc>()

    /// 注册一个短代码。
    let add (name: string) (fn: ShortcodeFunc) : unit =
        store.[name] <- fn

    /// 注册一个简单短代码（仅返回字符串，无上下文）。
    let addSimple (name: string) (fn: string -> string) : unit =
        store.[name] <- fun _ ctx -> fn ctx

    /// 执行短代码（如果已注册）。
    let execute (name: string) (ctx: IDictionary<string, obj>) (arg: string) : string option =
        match store.TryGetValue name with
        | true, fn -> Some(fn ctx arg)
        | _       -> None

    /// 内置短代码：将内联 Markdown 渲染为 HTML。
    let private builtinMd (ctx: IDictionary<string, obj>) (arg: string) =
        Markdown.toHtml arg

    /// 内置短代码：获取全局数据值。
    let private builtinData (ctx: IDictionary<string, obj>) (key: string) =
        match ctx.TryGetValue key with
        | true, v -> string v
        | _       -> ""

    do
        store.["md"]   <- builtinMd
        store.["data"]  <- builtinData
        store.["date"]  <- fun _ _ -> DateTime.Now.ToString("yyyy-MM-dd")
        store.["year"]  <- fun _ _ -> DateTime.Now.Year.ToString()

/// 在模板上下文中执行短代码替换（{{ key param }} 或 {% key param %}）。
module ShortcodeRenderer =
    let inlineShortcodes (ctx: IDictionary<string, obj>) (text: string) =
        let pattern = Regex(@"\{\{\s*(\w+)\s*(.*?)\s*\}\}", RegexOptions.Compiled)
        pattern.Replace(text, MatchEvaluator(fun m ->
            let name = m.Groups.[1].Value
            let arg  = m.Groups.[2].Value
            match ShortcodeRegistry.execute name ctx arg with
            | Some v -> v
            | None   -> m.Value))

// ============================================================
// Collection 分组与页面列表辅助
// ============================================================

module Collections =

    /// 按标签分组页面。
    let groupByTags (pages: Page list) : (string * Page list) list =
        pages
        |> List.collect (fun p -> p.Tags |> List.map (fun t -> t, p))
        |> List.groupBy fst
        |> List.map (fun (tag, items) -> tag, items |> List.map snd)

    /// 按集合名称分组页面（基于 detectCollection 相似逻辑）。
    let groupByCollection (pages: Page list) : (string * Page list) list =
        pages
        |> List.groupBy (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.sortBy fst

    /// 渲染页面列表为 <ul>。
    let renderPageList (pages: Page list) : HtmlNode =
        ul [
            for p in pages do
                li [ aHref p.Url p.Title ]
        ]

// ============================================================
// 分页辅助 (Pagination)
// ============================================================

module Pagination =

    type PaginatedResult<'a> = {
        Items: 'a list
        CurrentPage: int
        TotalPages: int
        TotalItems: int
        PreviousUrl: string option
        NextUrl: string option
        PageUrl: int -> string
    }

    /// 将列表分页。
    let paginate<'a> (items: 'a list) (pageSize: int) (pageUrlFn: int -> string) : PaginatedResult<'a> list =
        let total = items.Length
        let pages = (total + pageSize - 1) / pageSize
        [ for i in 0 .. pages - 1 ->
            let pageItems = items |> List.skip (i * pageSize) |> List.truncate pageSize
            { Items = pageItems
              CurrentPage = i + 1
              TotalPages = pages
              TotalItems = total
              PreviousUrl = if i > 0 then Some(pageUrlFn i) else None
              NextUrl = if i < pages - 1 then Some(pageUrlFn (i + 2)) else None
              PageUrl = pageUrlFn } ]

    /// 渲染分页导航。
    let renderPagination (p: PaginatedResult<'a>) : HtmlNode =
        nav [
            if p.PreviousUrl.IsSome then
                aHref p.PreviousUrl.Value "← Previous"
            span [ text (sprintf "Page %d of %d" p.CurrentPage p.TotalPages) ]
            if p.NextUrl.IsSome then
                aHref p.NextUrl.Value "Next →"
        ]

// ============================================================
// 模板数据上下文辅助
// ============================================================

module TemplateData =

    /// 从站点全局数据中安全地获取值。
    let siteData (data: IDictionary<string, obj>) (key: string) : string =
        match data.TryGetValue key with
        | true, (:? string as s) -> s
        | true, v -> string v
        | _       -> ""

    /// 从站点全局数据中获取子对象字典。
    let siteSection (data: IDictionary<string, obj>) (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in data do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    /// 获取当前页面数据。
    let pageData (page: Page) (key: string) : string =
        match page.Data.TryGetValue key with
        | true, v -> string v
        | _       -> ""
