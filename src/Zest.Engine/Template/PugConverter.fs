namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// PugConverter — Pug → HTML Converter
// ============================================================
// Converts Pug (indentation-based) syntax to HTML.
//
// Supported:
//   tag            → <tag></tag>
//   tag.class      → <tag class="class"></tag>
//   tag#id.class   → <tag id="id" class="class"></tag>
//   .class         → <div class="class"></div>
//   #id            → <div id="id"></div>
//   tag(attr="v")  → <tag attr="v"></tag>
//   | text         → literal text
//   // comment     → HTML comment
//   //-            → stripped comment
//   = expression   → evaluated expression
//   - code         → silent code (stripped)
//   doctype        → <!DOCTYPE html>
//   include path   → {% include "path" %}
//   :markdown      → passthrough
//
// Optimisations vs. original:
//   • All regexes hoisted to module-level static fields.
//   • indentOf handles tabs and arbitrary space widths.
//   • Text content and attribute values are HTML-escaped.
//   • Void elements emit HTML5 `>`.
//   • `include` now emits a Nunjucks include directive.
//   • Results cached by content hash via TemplateUtils.
// ============================================================

module PugConverter =

    // ── Module-level compiled regexes (created once) ──
    let private emptyLine  = Regex(@"^\s*$", RegexOptions.Compiled)
    let private comment    = Regex(@"^\s*//-?\s*", RegexOptions.Compiled)
    let private expression = Regex(@"^\s*=\s+", RegexOptions.Compiled)
    let private silentCode = Regex(@"^\s*-\s+", RegexOptions.Compiled)
    let private pipeText   = Regex(@"^\s*\|\s*", RegexOptions.Compiled)
    let private includePat = Regex(@"^\s*include\s+(.+)$", RegexOptions.Compiled)
    let private pugTag     =
        Regex(@"^\s*(?<tag>[a-zA-Z][a-zA-Z0-9]*)?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)(\((?<attrs>[^\)]*)\))?(?<rest>.*)$",
              RegexOptions.Compiled)
    let private clsIdPat =
        Regex(@"^(?:[a-zA-Z][a-zA-Z0-9]*)?(?:\#[a-zA-Z][a-zA-Z0-9\-_]*)?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)",
              RegexOptions.Compiled)
    // Attribute parser: key=value or key="value with spaces" or key='value'
    let private attrRegex =
        Regex(@"\s*(?<key>[\w-]+)=(?<value>(?:""[^""]*""|'[^']*'|[^\s)]+))", RegexOptions.Compiled)

    /// Count leading whitespace as an indent LEVEL.
    let private indentLevel (line: string) =
        let mutable i = 0
        let mutable lvl = 0
        while i < line.Length do
            let c = line.[i]
            if c = ' ' then
                let mutable sp = 0
                while i < line.Length && line.[i] = ' ' do sp <- sp + 1; i <- i + 1
                lvl <- lvl + (sp + 1) / 2
            elif c = '\t' then
                lvl <- lvl + 1; i <- i + 1
            else ()
        lvl

    /// Minimal Pug-to-HTML conversion.
    let convert (pug: string) : string =
        TemplateUtils.cachedConvert pug (fun pug ->
        if String.IsNullOrWhiteSpace pug then ""
        else
            let lines = pug.Replace("\r\n", "\n").Split('\n')
            let sb = StringBuilder()
            let mutable indentStack: (int * string) list = []

            let closeUntil (targetIndent: int) =
                while indentStack.Length > 0 && (fst indentStack.Head) > targetIndent do
                    let (_, closeTag) = indentStack.Head
                    indentStack <- indentStack.Tail
                    sb.Append(sprintf "</%s>" closeTag) |> ignore

            for line in lines do
                if emptyLine.IsMatch(line) then
                    sb.AppendLine() |> ignore
                elif comment.IsMatch(line) && line.Trim().StartsWith("//") then
                    let c = comment.Replace(line.Trim(), "")
                    if not (line.Trim().StartsWith("//-")) then
                        sb.AppendLine(sprintf "<!-- %s -->" (TemplateUtils.htmlEncode (c.Trim()))) |> ignore
                elif silentCode.IsMatch(line) && line.Trim().StartsWith("-") then
                    ()  // Strip silent code
                elif expression.IsMatch(line) then
                    let exp = expression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                elif pipeText.IsMatch(line) then
                    let text = pipeText.Replace(line, "")
                    sb.Append(TemplateUtils.htmlEncode text).AppendLine() |> ignore
                elif includePat.IsMatch(line) then
                    let incPath = includePat.Match(line).Groups.[1].Value.Trim().Trim('"', '\'')
                    sb.AppendLine(sprintf "{%% include \"%s\" %%}" incPath) |> ignore
                else
                    let indent = indentLevel line
                    closeUntil indent

                    let m = pugTag.Match(line)
                    if m.Success then
                        let tagRaw = if m.Groups.["tag"].Success then m.Groups.["tag"].Value else ""
                        let id     = if m.Groups.["id"].Success  then m.Groups.["id"].Value  else ""
                        let attrs  = if m.Groups.["attrs"].Success then m.Groups.["attrs"].Value else ""
                        let rest   = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

                        let cls =
                            let clsMatch = clsIdPat.Match(line.TrimStart())
                            if clsMatch.Success && clsMatch.Groups.[1].Success then
                                clsMatch.Groups.[1].Value.Split('.', System.StringSplitOptions.RemoveEmptyEntries)
                                |> String.concat " "
                            else ""

                        let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                                  elif tagRaw = "" then ""
                                  else tagRaw

                        if tag = "" then
                            let text = line.Trim()
                            if text <> "" then sb.AppendLine(TemplateUtils.htmlEncode text) |> ignore
                        elif tag = "doctype" then
                            sb.AppendLine("<!DOCTYPE html>") |> ignore
                        else
                            sb.Append('<') |> ignore
                            sb.Append(tag) |> ignore
                            if id <> "" then sb.Append(sprintf " id=\"%s\"" (TemplateUtils.attrEncode id)) |> ignore
                            if cls <> "" then sb.Append(sprintf " class=\"%s\"" (TemplateUtils.attrEncode cls)) |> ignore
                            if attrs <> "" then
                                for am in attrRegex.Matches(attrs) do
                                    let ak = am.Groups.["key"].Value
                                    let av = am.Groups.["value"].Value.Trim('"', '\'')
                                    sb.Append(sprintf " %s=\"%s\"" ak (TemplateUtils.attrEncode av)) |> ignore

                            let content =
                                if rest.StartsWith("=") then sprintf "{{ %s }}" (rest.Substring(1).Trim())
                                else rest

                            if TemplateUtils.isVoidElement tag then
                                sb.Append('>') |> ignore; sb.AppendLine() |> ignore
                            elif content <> "" then
                                sb.Append('>') |> ignore
                                sb.Append(content) |> ignore
                                sb.Append(sprintf "</%s>" tag) |> ignore
                                sb.AppendLine() |> ignore
                            else
                                sb.AppendLine(">") |> ignore
                                indentStack <- (indent, tag) :: indentStack
                    else
                        sb.AppendLine(TemplateUtils.htmlEncode (line.Trim())) |> ignore

            while indentStack.Length > 0 do
                let (_, closeTag) = indentStack.Head
                indentStack <- indentStack.Tail
                sb.Append(sprintf "</%s>" closeTag) |> ignore

            sb.ToString().TrimEnd())
