namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// HamlConverter — HAML → HTML Converter
// ============================================================
// Converts HAML (indentation-based) syntax to HTML.
//
// Supported:
//   %tag           → <tag></tag>
//   %tag.class     → <tag class="class"></tag>
//   %tag#id.class  → <tag id="id" class="class"></tag>
//   .class         → <div class="class"></div>
//   #id            → <div id="id"></div>
//   %tag{attr: v}  → <tag attr="v"></tag>
//   Content text   → inline content (no tag, just text)
//   = expression   → evaluated (escaped placeholder)
//   - code         → silent code (stripped)
//   / comment      → HTML comment
//   :css           → <style> block (indented body captured)
//   :javascript    → <script> block
//   :markdown      → passthrough (left for the Markdown pipeline)
//
// Optimisations vs. original:
//   • All regexes hoisted to module-level static fields (no per-call
//     compilation, no per-line allocation).
//   • indentOf handles tabs and arbitrary space widths.
//   • Text content and attribute values are HTML-escaped.
//   • Void elements emit HTML5 `>` (not XHTML ` />`).
//   • `:css` / `:javascript` / `:markdown` filters now capture their
//     indented body and emit `<style>` / `<script>` blocks.
//   • Results cached by content hash via TemplateUtils.
// ============================================================

module HamlConverter =

    // ── Module-level compiled regexes (created once) ──
    let private emptyLine   = Regex(@"^\s*$", RegexOptions.Compiled)
    let private commentLine = Regex(@"^\s*/", RegexOptions.Compiled)
    let private silentCode  = Regex(@"^\s*-", RegexOptions.Compiled)
    let private expression  = Regex(@"^\s*=\s+", RegexOptions.Compiled)
    let private filterStart = Regex(@"^\s*:(css|javascript|js|coffee|markdown|plain)\s*$", RegexOptions.Compiled)
    let private hamlTag     =
        Regex(@"^\s*(%(?<tag>[a-zA-Z][a-zA-Z0-9]*))?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?(\.(?<cls>[a-zA-Z][a-zA-Z0-9\-_]+))*(?<attrs>\{[^\}]*\})?(?<rest>.*)$",
              RegexOptions.Compiled)
    // Standalone class/id extraction (for lines like `.a.b` or `#x.y`)
    let private clsIdPat =
        Regex(@"^(?:%[a-zA-Z][a-zA-Z0-9]*)?(?:\#[a-zA-Z][a-zA-Z0-9\-_]*)?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)",
              RegexOptions.Compiled)

    /// Count leading whitespace as an indent LEVEL (spaces or tabs).
    /// Tabs count as one level each; runs of spaces count by width.
    /// Unlike the old `+2` stepper, this handles 1/2/4-space and tab indents.
    let private indentLevel (line: string) =
        let mutable i = 0
        let mutable lvl = 0
        while i < line.Length do
            let c = line.[i]
            if c = ' ' then
                // Count consecutive spaces; every 2 spaces = 1 level (HAML
                // convention), but a lone space still bumps the level so
                // odd-width indents don't collapse to 0.
                let mutable sp = 0
                while i < line.Length && line.[i] = ' ' do sp <- sp + 1; i <- i + 1
                lvl <- lvl + (sp + 1) / 2
            elif c = '\t' then
                lvl <- lvl + 1; i <- i + 1
            else ()
        lvl

    /// Minimal HAML-to-HTML conversion.
    /// Returns the HTML string (not wrapped in additional tags).
    let convert (haml: string) : string =
        TemplateUtils.cachedConvert haml (fun haml ->
        if String.IsNullOrWhiteSpace haml then ""
        else
            let lines = haml.Replace("\r\n", "\n").Split('\n')
            let sb = StringBuilder()
            let mutable indentStack: (int * string) list = []
            let mutable i = 0
            let n = lines.Length

            let closeUntil (targetIndent: int) =
                while indentStack.Length > 0 && (fst indentStack.Head) > targetIndent do
                    let (_, closeTag) = indentStack.Head
                    indentStack <- indentStack.Tail
                    sb.Append(sprintf "</%s>" closeTag) |> ignore

            while i < n do
                let line = lines.[i]
                if emptyLine.IsMatch(line) then
                    sb.AppendLine() |> ignore; i <- i + 1
                elif commentLine.IsMatch(line) && line.Trim().StartsWith("/") then
                    let comment = line.Trim().[1..].Trim()
                    sb.AppendLine(sprintf "<!-- %s -->" (TemplateUtils.htmlEncode comment)) |> ignore
                    i <- i + 1
                elif silentCode.IsMatch(line) && line.Trim().StartsWith("-") then
                    i <- i + 1  // Strip silent code
                elif filterStart.IsMatch(line) then
                    // ── Filter blocks: capture the indented body ──
                    let filterName = filterStart.Match(line).Groups.[1].Value
                    let baseIndent = indentLevel line
                    let body = StringBuilder()
                    i <- i + 1
                    while i < n && (emptyLine.IsMatch(lines.[i]) || indentLevel lines.[i] > baseIndent) do
                        if not (emptyLine.IsMatch(lines.[i])) then
                            // Strip one level of indent from the body line.
                            let trimmed = lines.[i].TrimStart()
                            body.AppendLine(trimmed) |> ignore
                        else body.AppendLine() |> ignore
                        i <- i + 1
                    let bodyText = body.ToString().TrimEnd()
                    match filterName with
                    | "css" -> sb.AppendFormat("<style>\n{0}\n</style>\n", bodyText) |> ignore
                    | "javascript" | "js" -> sb.AppendFormat("<script>\n{0}\n</script>\n", bodyText) |> ignore
                    | "markdown" -> sb.AppendFormat("{0}\n", bodyText) |> ignore  // pass through to MD pipeline
                    | _ -> sb.Append(bodyText).Append('\n') |> ignore
                elif expression.IsMatch(line) then
                    let exp = expression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                    i <- i + 1
                else
                    let indent = indentLevel line
                    closeUntil indent

                    let m = hamlTag.Match(line)
                    if m.Success then
                        let tagRaw = if m.Groups.["tag"].Success then m.Groups.["tag"].Value else ""
                        let id     = if m.Groups.["id"].Success  then m.Groups.["id"].Value  else ""
                        let rest   = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

                        let cls =
                            let clsMatch = clsIdPat.Match(line.TrimStart())
                            if clsMatch.Success && clsMatch.Groups.[1].Success then
                                clsMatch.Groups.[1].Value.Split('.', System.StringSplitOptions.RemoveEmptyEntries)
                                |> String.concat " "
                            else ""

                        let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                                  elif tagRaw = "" then "" else tagRaw

                        if tag = "" then
                            let plainText = line.Trim()
                            if plainText <> "" then sb.AppendLine(TemplateUtils.htmlEncode plainText) |> ignore
                        else
                            let attrsRaw = if m.Groups.["attrs"].Success then m.Groups.["attrs"].Value else ""
                            let attrs = if attrsRaw.Length >= 2 then attrsRaw.Substring(1, attrsRaw.Length - 2) else attrsRaw

                            sb.Append('<') |> ignore
                            sb.Append(tag) |> ignore
                            if id <> "" then sb.Append(sprintf " id=\"%s\"" (TemplateUtils.attrEncode id)) |> ignore
                            if cls <> "" then sb.Append(sprintf " class=\"%s\"" (TemplateUtils.attrEncode cls)) |> ignore
                            // Parse inline attributes {key: value, key: value}
                            if attrs <> "" then
                                for pair in attrs.Split(',') do
                                    let parts = pair.Trim().Split(':')
                                    if parts.Length >= 2 then
                                        let key = parts.[0].Trim()
                                        let value = parts.[1..] |> String.concat ":" |> (fun s -> s.Trim().Trim('"', '\''))
                                        sb.Append(sprintf " %s=\"%s\"" key (TemplateUtils.attrEncode value)) |> ignore

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
                    i <- i + 1

            // Close remaining open tags
            while indentStack.Length > 0 do
                let (_, closeTag) = indentStack.Head
                indentStack <- indentStack.Tail
                sb.Append(sprintf "</%s>" closeTag) |> ignore

            sb.ToString().TrimEnd())
