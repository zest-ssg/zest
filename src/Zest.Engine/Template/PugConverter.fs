namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// PugConverter — Pug → Nunjucks/HTML Converter
// ============================================================
// Converts Pug (indentation-based) syntax into HTML with Nunjucks
// directives so the Nunjucks engine can render `.pug` files.
//
// Supported:
//   tag / tag.class / tag#id / tag(attr="v")
//   .class / #id (div shorthand)
//   | text                     → literal text (with #{expr} interpolation)
//   = expr / != expr           → {{ expr }} / {{ expr | safe }}
//   - var x = expr             → {% set x = expr %}
//   if / else if / else / unless → {% if %}/{% elif %}/{% else %}/{% if not %}
//   each item in list / each val, key in obj → {% for %} (key, val swapped)
//   mixin / +mixin             → {% macro %} / {{ macro() }}
//   extends / block            → {% extends %}/{% block %}
//   include path               → {% include "path" %}
//   doctype                    → <!DOCTYPE ...>
//   tag. (script./style.)      → raw text block
//   // comment / //-           → HTML comment / stripped
//   #{expr}                    → {{ expr }} (text interpolation)
//
// Build as an indentation tree (recursive descent) so block constructs
// (if/each/mixin/extends/block) can be converted to their Nunjucks
// equivalents instead of being flattened.
// ============================================================

module PugConverter =

    // ── Module-level compiled regexes (created once) ──
    let private commentPat   = Regex(@"^\s*//-?\s*", RegexOptions.Compiled)
    let private pipeText     = Regex(@"^\s*\|\s*", RegexOptions.Compiled)
    let private exprLine     = Regex(@"^\s*(!?=)\s+", RegexOptions.Compiled)
    let private codeLine     = Regex(@"^\s*-\s+", RegexOptions.Compiled)
    let private varDeclPat   = Regex(@"^\s*-\s*var\s+([\w]+)\s*=\s*(.+)$", RegexOptions.Compiled)
    let private includePat   = Regex(@"^\s*include\s+(.+)$", RegexOptions.Compiled)
    let private extendsPat   = Regex(@"^\s*extends\s+(.+)$", RegexOptions.Compiled)
    let private mixinPat     = Regex(@"^\s*mixin\s+([A-Za-z_][\w-]*)\s*(\(([^)]*)\))?\s*$", RegexOptions.Compiled)
    let private mixinCallPat = Regex(@"^\s*\+([A-Za-z_][\w-]*)\s*(\(([^)]*)\))?\s*$", RegexOptions.Compiled)
    let private blockPat     = Regex(@"^\s*block\s+([\w-]+)\s*$", RegexOptions.Compiled)
    let private ifPat        = Regex(@"^\s*if\s+(.+)$", RegexOptions.Compiled)
    let private unlessPat    = Regex(@"^\s*unless\s+(.+)$", RegexOptions.Compiled)
    let private elsePat      = Regex(@"^\s*else\s*(if\s+(.+))?\s*$", RegexOptions.Compiled)
    let private eachPat      = Regex(@"^\s*each\s+([\w, ]+?)\s+in\s+(.+)$", RegexOptions.Compiled)
    let private doctypePat   = Regex(@"^\s*doctype\s*([\w.]*)\s*$", RegexOptions.Compiled)
    let private pugTag       =
        Regex(@"^\s*(?<tag>[a-zA-Z][a-zA-Z0-9-]*)?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)(\((?<attrs>[^\)]*)\))?(?<rest>.*)$",
              RegexOptions.Compiled)
    let private clsIdPat     =
        Regex(@"^(?:[a-zA-Z][a-zA-Z0-9-]*)?(?:\#[a-zA-Z][a-zA-Z0-9\-_]*)?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)",
              RegexOptions.Compiled)
    let private interpRegex  = Regex(@"#\{([^}]+)\}", RegexOptions.Compiled)
    let private numPat       = Regex(@"^-?\d+(\.\d+)?$", RegexOptions.Compiled)

    /// Replace Pug `#{expr}` text interpolation with Nunjucks `{{ expr }}`.
    let private renderInterp (s: string) : string =
        interpRegex.Replace(s, fun m -> "{{ " + m.Groups.[1].Value.Trim() + " }}")

    /// DOCTYPE string for the common Pug doctype variants.
    let private doctypeHtml (variant: string) : string =
        match variant.Trim().ToLowerInvariant() with
        | "" | "html" | "5" ->
            "<!DOCTYPE html>"
        | "strict" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">"
        | "transitional" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">"
        | "1.1" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">"
        | "xml" ->
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>"
        | _ ->
            "<!DOCTYPE html>"

    /// Render a single attribute value: quoted literal → escaped text;
    /// numeric → literal; bare identifier → {{ path }}; true/false → kept.
    let private renderAttrValue (raw: string) (isBare: bool) : string =
        if isBare then
            if raw = "true" || raw = "false" then raw
            elif numPat.IsMatch raw then raw
            else "{{ " + raw + " }}"
        else
            TemplateUtils.attrEncode raw

    /// Parse a Pug attribute list `a="x" b='y' c=3 d=var e` into (key, rendered) pairs.
    /// A key without a value (or =true) renders as a bare attribute; =false drops it.
    let private parseAttrs (s: string) : (string * string) list =
        let res = ResizeArray<string * string>()
        let n = s.Length
        let mutable i = 0
        while i < n do
            while i < n && Char.IsWhiteSpace s.[i] do i <- i + 1
            let kb = i
            while i < n && (Char.IsLetterOrDigit s.[i] || s.[i] = '-' || s.[i] = '_') do i <- i + 1
            if i > kb then
                let key = s.Substring(kb, i - kb)
                while i < n && Char.IsWhiteSpace s.[i] do i <- i + 1
                if i < n && s.[i] = '=' then
                    i <- i + 1
                    while i < n && Char.IsWhiteSpace s.[i] do i <- i + 1
                    if i < n && (s.[i] = '"' || s.[i] = '\'') then
                        let q = s.[i]
                        let vStart = i + 1
                        let vEnd = s.IndexOf(q, vStart)
                        if vEnd < 0 then i <- n
                        else
                            res.Add(key, renderAttrValue (s.Substring(vStart, vEnd - vStart)) false)
                            i <- vEnd + 1
                    else
                        let vStart = i
                        while i < n && not (Char.IsWhiteSpace s.[i]) && s.[i] <> ',' do i <- i + 1
                        res.Add(key, renderAttrValue (s.Substring(vStart, i - vStart)) true)
                else
                    res.Add(key, "")   // boolean attribute (renders bare)
        List.ofSeq res

    // ── AST ──────────────────────────────────────────────────
    type PNode =
        | Element of tag: string * id: string * cls: string * attrs: (string * string) list * rest: string * children: PNode list
        | Text of string
        | TextBlock of tag: string * content: string      // script. / style. raw block
        | Expr of expr: string * safe: bool
        | If of cond: string * thenNodes: PNode list * elseOpt: (PNode list) option
        | Unless of cond: string * body: PNode list
        | Each of varName: string * iter: string * body: PNode list * elseOpt: (PNode list) option
        | MixinDef of name: string * args: string * body: PNode list
        | MixinCall of name: string * args: string
        | Extends of path: string
        | Block of name: string * body: PNode list
        | VarDecl of name: string * expr: string
        | Include of path: string
        | Comment of text: string * emit: bool
        | Doctype of variant: string
        | Silent of code: string

    // ── Indentation parser ───────────────────────────────────
    /// Split into (leadingWhitespaceCount, trimmedLine), dropping blank lines.
    let private parseLines (pug: string) : (int * string) list =
        pug.Replace("\r\n", "\n").Split('\n')
        |> Array.toList
        |> List.map (fun line ->
            let mutable i = 0
            while i < line.Length && (line.[i] = ' ' || line.[i] = '\t') do i <- i + 1
            (i, line.Substring(i)))
        |> List.filter (fun (_, t) -> not (String.IsNullOrWhiteSpace t))

    /// Collect sibling nodes at exactly `level` indentation.
    let rec parseBlock (lines: (int * string) list) (level: int) : PNode list * (int * string) list =
        let nodes = ResizeArray<PNode>()
        let mutable rest = lines
        let mutable stop = false
        while not stop do
            match rest with
            | [] -> stop <- true
            | (ind, _) :: _ when ind < level -> stop <- true
            | (ind, _) :: _ when ind > level -> stop <- true   // malformed indent; stop
            | (ind, text) :: tail ->
                let node, restAfter = parseNode ind text tail
                nodes.Add node
                rest <- restAfter
        List.ofSeq nodes, rest

    /// Parse a single node and consume its children (lines indented deeper).
    and parseNode (ind: int) (text: string) (tail: (int * string) list) : PNode * (int * string) list =
        let t = text.Trim()

        let takeChildren (restAfter: (int * string) list) : PNode list * (int * string) list =
            match restAfter with
            | (cind, _) :: _ when cind > ind -> parseBlock restAfter cind
            | _ -> [], restAfter

        // Raw text block: consume every consecutive deeper line verbatim.
        let takeTextBlock () =
            let lines = ResizeArray<string>()
            let mutable rest = tail
            let mutable stop = false
            while not stop do
                match rest with
                | (cind, txt) :: rest2 when cind > ind ->
                    lines.Add txt
                    rest <- rest2
                | _ -> stop <- true
            String.concat "\n" (lines |> List.ofSeq), rest

        // if / else if / else chain.
        let rec ifNode (cond: string) (rest: (int * string) list) : PNode * (int * string) list =
            let body, restAfter = takeChildren rest
            match restAfter with
            | (eind, etxt) :: tail2 when eind = ind ->
                let et = etxt.Trim()
                if et.StartsWith("else", StringComparison.OrdinalIgnoreCase) then
                    if et.StartsWith("else if", StringComparison.OrdinalIgnoreCase) then
                        let m = elsePat.Match etxt
                        let cond2 = if m.Success then m.Groups.[2].Value else et.Substring(7).Trim()
                        let node2, restAfter2 = ifNode cond2 tail2
                        If(cond, body, Some [node2]), restAfter2
                    else
                        let elseBody, restAfter2 = takeChildren tail2
                        If(cond, body, Some elseBody), restAfter2
                else If(cond, body, None), restAfter
            | _ -> If(cond, body, None), restAfter

        // `each val, key in obj` — Nunjucks for binds (key, val), so swap.
        let parseEachVar (vars: string) : string =
            let parts = vars.Split(',') |> Array.map (fun x -> x.Trim())
            match Array.toList parts with
            | [v; k] -> k + ", " + v
            | l -> String.concat ", " l

        if t.StartsWith("doctype", StringComparison.OrdinalIgnoreCase) then
            let m = doctypePat.Match text
            Doctype(if m.Success && m.Groups.[1].Success then m.Groups.[1].Value else ""), tail
        elif t.StartsWith("extends", StringComparison.OrdinalIgnoreCase) then
            let m = extendsPat.Match text
            let path = if m.Success then m.Groups.[1].Value.Trim().Trim('"', '\'') else t.Substring(7).Trim()
            Extends path, tail
        elif t.StartsWith("include", StringComparison.OrdinalIgnoreCase) then
            let m = includePat.Match text
            let path = if m.Success then m.Groups.[1].Value.Trim().Trim('"', '\'') else t.Substring(7).Trim()
            Include path, tail
        elif t.StartsWith("block", StringComparison.OrdinalIgnoreCase) then
            let m = blockPat.Match text
            if m.Success then
                let body, restAfter = takeChildren tail
                Block(m.Groups.[1].Value, body), restAfter
            else Silent t, tail
        elif t.StartsWith("mixin", StringComparison.OrdinalIgnoreCase) then
            let m = mixinPat.Match text
            if m.Success then
                let name = m.Groups.[1].Value
                let args = if m.Groups.[3].Success then m.Groups.[3].Value.Trim() else ""
                let body, restAfter = takeChildren tail
                MixinDef(name, args, body), restAfter
            else Silent t, tail
        elif t.StartsWith("+") then
            let m = mixinCallPat.Match text
            if m.Success then
                let name = m.Groups.[1].Value
                let args = if m.Groups.[3].Success then m.Groups.[3].Value.Trim() else ""
                MixinCall(name, args), tail
            else Silent t, tail
        elif t.StartsWith("if ", StringComparison.OrdinalIgnoreCase) then
            let m = ifPat.Match text
            match m.Success with
            | true -> ifNode (m.Groups.[1].Value.Trim()) tail
            | false -> Silent t, tail
        elif t.StartsWith("unless", StringComparison.OrdinalIgnoreCase) then
            let m = unlessPat.Match text
            if m.Success then
                let body, restAfter = takeChildren tail
                Unless(m.Groups.[1].Value.Trim(), body), restAfter
            else Silent t, tail
        elif t.StartsWith("each", StringComparison.OrdinalIgnoreCase) then
            let m = eachPat.Match text
            if m.Success then
                let body, restAfter = takeChildren tail
                // Optional `else` block on the each loop.
                let elseOpt, restAfter2 =
                    match restAfter with
                    | (eind, etxt) :: tail2 when eind = ind ->
                        let et = etxt.Trim()
                        if et = "else" then
                            let elseBody, ra = takeChildren tail2
                            Some elseBody, ra
                        else None, restAfter
                    | _ -> None, restAfter
                Each(parseEachVar m.Groups.[1].Value, m.Groups.[2].Value.Trim(), body, elseOpt), restAfter2
            else Silent t, tail
        elif t.StartsWith("//") then
            let emit = not (t.StartsWith("//-"))
            let c = commentPat.Replace(text, "").Trim()
            Comment(c, emit), tail
        elif t.StartsWith("|") then
            Text(pipeText.Replace(text, "")), tail
        elif t.StartsWith("=") || t.StartsWith("!=") then
            let m = exprLine.Match text
            if m.Success then
                let safe = m.Groups.[1].Value.StartsWith("!")
                Expr(text.Substring(m.Index + m.Length).Trim(), safe), tail
            else Silent t, tail
        elif codeLine.IsMatch text then
            let vd = varDeclPat.Match text
            if vd.Success then VarDecl(vd.Groups.[1].Value, vd.Groups.[2].Value.Trim()), tail
            else Silent(t.Substring(1).Trim()), tail
        else
            // tag element (or text line)
            let tm = pugTag.Match text
            if tm.Success then
                let tagRaw = if tm.Groups.["tag"].Success then tm.Groups.["tag"].Value else ""
                let id = if tm.Groups.["id"].Success then tm.Groups.["id"].Value else ""
                let rest = if tm.Groups.["rest"].Success then tm.Groups.["rest"].Value.Trim() else ""
                // `script.` / `style.` → raw text block
                if rest = "." && id = "" then
                    let rawText, restAfter = takeTextBlock ()
                    TextBlock(tagRaw, rawText), restAfter
                else
                    let cls =
                        let cm = clsIdPat.Match (text.TrimStart())
                        if cm.Success && cm.Groups.[1].Success then
                            cm.Groups.[1].Value.Split('.', StringSplitOptions.RemoveEmptyEntries)
                            |> String.concat " "
                        else ""
                    let attrs = if tm.Groups.["attrs"].Success then parseAttrs tm.Groups.["attrs"].Value else []
                    let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                              elif tagRaw = "" then "" else tagRaw
                    if tag = "" then
                        // plain text line (e.g. indented prose)
                        if t <> "" then Text t, tail else Silent t, tail
                    else
                        let children, restAfter = takeChildren tail
                        Element(tag, id, cls, attrs, rest, children), restAfter
            else
                // Unknown line → treat as literal text
                Text t, tail

    // ── Conversion (AST → Nunjucks text) ─────────────────────
    let rec renderNodes (nodes: PNode list) (sb: StringBuilder) (indent: int) : unit =
        for node in nodes do renderNode node sb indent

    and renderIfBranches (cond: string) (thenNodes: PNode list) (elseOpt: PNode list option)
                         (sb: StringBuilder) (indent: int) (isElif: bool) : unit =
        let pad = String(' ', indent * 2)
        let kw = if isElif then "elif" else "if"
        sb.Append(pad).Append(sprintf "{%% %s %s %%}\n" kw cond) |> ignore
        renderNodes thenNodes sb (indent + 1)
        match elseOpt with
        | Some [If(c2, t2, e2)] -> renderIfBranches c2 t2 e2 sb indent true
        | Some nodes when not nodes.IsEmpty ->
            sb.Append(pad).Append("{% else %}\n") |> ignore
            renderNodes nodes sb (indent + 1)
            sb.Append(pad).Append("{% endif %}\n") |> ignore
        | _ -> sb.Append(pad).Append("{% endif %}\n") |> ignore

    and renderElement (tag: string) (id: string) (cls: string) (attrs: (string * string) list)
                      (rest: string) (children: PNode list) (sb: StringBuilder) (indent: int) : unit =
        let pad = String(' ', indent * 2)
        let openSb = StringBuilder("<")
        openSb.Append(tag) |> ignore
        if id <> "" then openSb.Append(sprintf " id=\"%s\"" (TemplateUtils.attrEncode id)) |> ignore
        if cls <> "" then openSb.Append(sprintf " class=\"%s\"" (TemplateUtils.attrEncode cls)) |> ignore
        for k, v in attrs do
            match v with
            | "" | "true" -> openSb.Append(sprintf " %s" k) |> ignore
            | "false" -> ()  // boolean false → attribute omitted
            | _ -> openSb.Append(sprintf " %s=\"%s\"" k v) |> ignore
        if TemplateUtils.isVoidElement tag then
            sb.Append(pad).Append(openSb.ToString()).Append(">\n") |> ignore
        elif rest <> "" then
            sb.Append(pad).Append(openSb.ToString()).Append('>').Append(renderInterp rest) |> ignore
            if children.IsEmpty then
                sb.Append(sprintf "</%s>\n" tag) |> ignore
            else
                sb.Append('\n') |> ignore
                renderNodes children sb (indent + 1)
                sb.Append(pad).Append(sprintf "</%s>\n" tag) |> ignore
        elif not children.IsEmpty then
            sb.Append(pad).Append(openSb.ToString()).Append(">\n") |> ignore
            renderNodes children sb (indent + 1)
            sb.Append(pad).Append(sprintf "</%s>\n" tag) |> ignore
        else
            sb.Append(pad).Append(openSb.ToString()).Append(sprintf "></%s>\n" tag) |> ignore

    and renderNode (node: PNode) (sb: StringBuilder) (indent: int) : unit =
        let pad = String(' ', indent * 2)
        match node with
        | Text txt -> sb.Append(pad).Append(renderInterp txt).Append('\n') |> ignore
        | TextBlock(tag, content) ->
            sb.Append(pad).Append('<').Append(tag).Append(">\n") |> ignore
            for ln in content.Split('\n') do
                sb.Append(pad).Append(' ').Append(ln).Append('\n') |> ignore
            sb.Append(pad).Append(sprintf "</%s>\n" tag) |> ignore
        | Expr(expr, false) -> sb.Append(pad).Append("{{ ").Append(expr).Append(" }}\n") |> ignore
        | Expr(expr, true) -> sb.Append(pad).Append("{{ ").Append(expr).Append(" | safe }}\n") |> ignore
        | Comment(c, true) -> sb.Append(pad).Append(sprintf "<!-- %s -->\n" (TemplateUtils.htmlEncode c)) |> ignore
        | Comment(_, false) -> ()
        | Silent _ -> ()
        | VarDecl(name, expr) -> sb.Append(pad).Append(sprintf "{%% set %s = %s %%}\n" name expr) |> ignore
        | Include(path) -> sb.Append(pad).Append(sprintf "{%% include \"%s\" %%}\n" path) |> ignore
        | Extends(path) -> sb.Append(pad).Append(sprintf "{%% extends \"%s\" %%}\n" path) |> ignore
        | Doctype(variant) -> sb.Append(pad).Append(doctypeHtml variant).Append('\n') |> ignore
        | MixinCall(name, args) ->
            sb.Append(pad).Append("{{ ").Append(name).Append('(').Append(args).Append(") }}\n") |> ignore
        | MixinDef(name, args, body) ->
            sb.Append(pad).Append(sprintf "{%% macro %s(%s) %%}\n" name args) |> ignore
            renderNodes body sb (indent + 1)
            sb.Append(pad).Append("{% endmacro %}\n") |> ignore
        | Block(name, body) ->
            sb.Append(pad).Append(sprintf "{%% block %s %%}\n" name) |> ignore
            renderNodes body sb (indent + 1)
            sb.Append(pad).Append("{% endblock %}\n") |> ignore
        | Unless(cond, body) ->
            sb.Append(pad).Append(sprintf "{%% if not (%s) %%}\n" cond) |> ignore
            renderNodes body sb (indent + 1)
            sb.Append(pad).Append("{% endif %}\n") |> ignore
        | Each(varName, iter, body, elseOpt) ->
            sb.Append(pad).Append(sprintf "{%% for %s in %s %%}\n" varName iter) |> ignore
            renderNodes body sb (indent + 1)
            match elseOpt with
            | Some eb when not eb.IsEmpty ->
                sb.Append(pad).Append("{% else %}\n") |> ignore
                renderNodes eb sb (indent + 1)
            | _ -> ()
            sb.Append(pad).Append("{% endfor %}\n") |> ignore
        | If(cond, thenNodes, elseOpt) -> renderIfBranches cond thenNodes elseOpt sb indent false
        | Element(tag, id, cls, attrs, rest, children) -> renderElement tag id cls attrs rest children sb indent

    /// Convert Pug template source to HTML with Nunjucks directives.
    let convert (pug: string) : string =
        TemplateUtils.cachedConvert pug (fun pug ->
            if String.IsNullOrWhiteSpace pug then ""
            else
                let lines = parseLines pug
                let sb = StringBuilder()
                // Document-level style check: Pug requires one indent style.
                if TemplateUtils.mixedIndentStyles (lines |> List.map snd |> List.toArray) then
                    sb.AppendLine("<!-- Pug indent error: mixed tab and space indentation (pick one style) -->") |> ignore
                let nodes, _ = parseBlock lines 0
                renderNodes nodes sb 0
                sb.ToString().TrimEnd('\n'))
