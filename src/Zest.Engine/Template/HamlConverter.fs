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
//   != expression  → unescaped output ({{ expr | safe }})
//   &= expression  → escaped output (alias of =)
//   #{expr}        → Nunjucks {{ expr }} interpolation
//   !!! / !!! 5    → DOCTYPE declaration
//   - code         → silent code (stripped)
//   / comment      → HTML comment
//   :css           → <style> block (indented body captured)
//   :javascript    → <script> block
//   :markdown      → passthrough (left for the Markdown pipeline)
//   Multiline `{...}` attribute hashes spread over following lines.
//
// Deliberately NOT supported (Ruby semantics have no Nunjucks equivalent):
//   [obj, :method] object references  → Ruby objects cannot be evaluated here;
//     the token passes through as literal text.
//   ~ expression (preserve whitespace) → no Nunjucks equivalent; falls through
//     as plain text.
//   Ruby code inside `-` lines is stripped, not executed.
//
// Optimisations vs. original:
//   • All regexes hoisted to module-level static fields (no per-call
//     compilation, no per-line allocation).
//   • Strict indentation: one tab or two spaces per level; mixed or odd-width
//     indents are reported as errors instead of being guessed.
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
    let private rawExpression = Regex(@"^\s*!=\s+", RegexOptions.Compiled)
    let private filterStart = Regex(@"^\s*:(css|javascript|js|coffee|scss|sass|less|markdown|plain)\s*$", RegexOptions.Compiled)
    let private hamlTag     =
        Regex(@"^\s*(%(?<tag>[a-zA-Z][a-zA-Z0-9]*))?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?(\.(?<cls>[a-zA-Z][a-zA-Z0-9\-_]+))*(?<attrs>\{[^\}]*\})?(?<rest>.*)$",
              RegexOptions.Compiled)
    // Standalone class/id extraction (for lines like `.a.b` or `#x.y`)
    let private clsIdPat =
        Regex(@"^(?:%[a-zA-Z][a-zA-Z0-9]*)?(?:\#[a-zA-Z][a-zA-Z0-9\-_]*)?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)",
              RegexOptions.Compiled)
    let private interpRegex    = Regex(@"#\{([^}]+)\}", RegexOptions.Compiled)
    let private doctypeLine    = Regex(@"^\s*!!!\s*(.*)$", RegexOptions.Compiled)
    let private ampExpression  = Regex(@"^\s*&=\s+", RegexOptions.Compiled)

    /// Count leading whitespace as an indent LEVEL.
    /// Strict: one tab or two spaces per level; returns None for mixed or
    /// odd-width indentation so the caller can report instead of guessing.
    let private indentLevel (line: string) = TemplateUtils.indentLevelStrict line

    /// Replace HAML `#{expr}` interpolation with Nunjucks `{{ expr }}`.
    let private renderInterp (s: string) : string =
        interpRegex.Replace(s, fun m -> "{{ " + m.Groups.[1].Value.Trim() + " }}")

    /// DOCTYPE string for the common HAML `!!!` variants.
    let private doctypeHtml (variant: string) : string =
        match variant.Trim().ToLowerInvariant() with
        | "" | "5" | "html" ->
            "<!DOCTYPE html>"
        | "strict" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Strict//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd\">"
        | "frameset" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Frameset//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-frameset.dtd\">"
        | "transitional" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">"
        | "1.1" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.1//EN\" \"http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd\">"
        | "basic" ->
            "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML Basic 1.1//EN\" \"http://www.w3.org/TR/xhtml-basic/xhtml-basic11.dtd\">"
        | "mobile" ->
            "<!DOCTYPE html PUBLIC \"-//WAPFORUM//DTD XHTML Mobile 1.2//EN\" \"http://www.openmobilealliance.org/tech/DTD/xhtml-mobile12.dtd\">"
        | "xml" ->
            "<?xml version=\"1.0\" encoding=\"utf-8\" ?>"
        | _ ->
            "<!DOCTYPE html>"

    /// Net `{` minus `}` on a line, used to detect attribute hashes that span
    /// multiple lines. Quote contents are not tracked, so a brace inside a
    /// string value is counted; multiline hashes rarely embed such braces.
    let private braceBalance (s: string) : int =
        (s |> Seq.filter (fun c -> c = '{') |> Seq.length)
        - (s |> Seq.filter (fun c -> c = '}') |> Seq.length)

    /// Minimal HAML-to-HTML conversion.
    /// Returns the HTML string (not wrapped in additional tags).
    let convert (haml: string) : string =
        TemplateUtils.cachedConvert haml (fun haml ->
        if String.IsNullOrWhiteSpace haml then ""
        else
            let lines = haml.Replace("\r\n", "\n").Split('\n')
            let sb = StringBuilder()
            // Document-level style check: Haml requires one indent style.
            if TemplateUtils.mixedIndentStyles lines then
                sb.AppendLine("<!-- Haml indent error: mixed tab and space indentation (pick one style) -->") |> ignore
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
                    match indentLevel line with
                    | None ->
                        sb.AppendLine(sprintf "<!-- Haml indent error: %s -->" (TemplateUtils.htmlEncode (line.Trim()))) |> ignore
                        i <- i + 1
                    | Some baseIndent ->
                        let body = StringBuilder()
                        i <- i + 1
                        while i < n && (emptyLine.IsMatch(lines.[i])
                                        || (match indentLevel lines.[i] with Some l -> l > baseIndent | None -> false)) do
                            if not (emptyLine.IsMatch(lines.[i])) then
                                // Strip one level of indent from the body line.
                                let trimmed = lines.[i].TrimStart()
                                body.AppendLine(trimmed) |> ignore
                            else body.AppendLine() |> ignore
                            i <- i + 1
                        let bodyText = body.ToString().TrimEnd()
                        match filterName with
                        | "css" | "scss" | "sass" | "less" ->
                            sb.AppendFormat("<style>\n{0}\n</style>\n", bodyText) |> ignore
                        | "javascript" | "js" -> sb.AppendFormat("<script>\n{0}\n</script>\n", bodyText) |> ignore
                        | "markdown" -> sb.AppendFormat("{0}\n", bodyText) |> ignore  // pass through to MD pipeline
                        | _ -> sb.Append(bodyText).Append('\n') |> ignore
                elif rawExpression.IsMatch(line) then
                    // `!= expr` — unescaped output (bypasses Nunjucks auto-escape).
                    let exp = rawExpression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s | safe }}" exp) |> ignore
                    i <- i + 1
                elif expression.IsMatch(line) then
                    let exp = expression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                    i <- i + 1
                elif ampExpression.IsMatch(line) then
                    // `&= expr` — escaped output (alias of `=`).
                    let exp = ampExpression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                    i <- i + 1
                elif doctypeLine.IsMatch(line) then
                    // `!!!` / `!!! 5` — DOCTYPE declaration.
                    let variant = doctypeLine.Match(line).Groups.[1].Value
                    sb.AppendLine(doctypeHtml variant) |> ignore
                    i <- i + 1
                else
                    // A tag line may carry a `{...}` attribute hash that spans
                    // several lines; join them until the braces balance. The
                    // original line still drives indentation, while `logicalLine`
                    // is what the tag parser sees.
                    let logicalLine, nextI =
                        if braceBalance line > 0 then
                            let acc = StringBuilder(line)
                            let mutable j = i + 1
                            let mutable depth = braceBalance line
                            while j < n && depth > 0 do
                                acc.Append(' ').Append(lines.[j].Trim()) |> ignore
                                depth <- depth + braceBalance lines.[j]
                                j <- j + 1
                            acc.ToString(), j
                        else line, i + 1

                    match indentLevel line with
                    | None ->
                        // Refuse to guess a nesting level: mixed tabs/spaces or
                        // an odd space width cannot build a sound tree.
                        sb.AppendLine(sprintf "<!-- Haml indent error: %s -->" (TemplateUtils.htmlEncode (line.Trim()))) |> ignore
                        i <- nextI
                    | Some indent ->
                        closeUntil indent

                        let m = hamlTag.Match(logicalLine)
                        if m.Success then
                            let tagRaw = if m.Groups.["tag"].Success then m.Groups.["tag"].Value else ""
                            let id     = if m.Groups.["id"].Success  then m.Groups.["id"].Value  else ""
                            let rest   = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

                            let cls =
                                let clsMatch = clsIdPat.Match(logicalLine.TrimStart())
                                if clsMatch.Success && clsMatch.Groups.[1].Success then
                                    clsMatch.Groups.[1].Value.Split('.', System.StringSplitOptions.RemoveEmptyEntries)
                                    |> String.concat " "
                                else ""

                            let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                                      elif tagRaw = "" then "" else tagRaw

                            if tag = "" then
                                let plainText = logicalLine.Trim()
                                if plainText <> "" then sb.AppendLine(TemplateUtils.htmlEncode (renderInterp plainText)) |> ignore
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
                                            match value.ToLowerInvariant() with
                                            | "true" -> sb.Append(sprintf " %s" key) |> ignore
                                            | "false" -> ()  // boolean false → attribute omitted
                                            | _ -> sb.Append(sprintf " %s=\"%s\"" key (TemplateUtils.attrEncode (renderInterp value))) |> ignore

                                let content =
                                    if rest.StartsWith("!=") then sprintf "{{ %s | safe }}" (rest.Substring(2).Trim())
                                    elif rest.StartsWith("=") then sprintf "{{ %s }}" (rest.Substring(1).Trim())
                                    else renderInterp rest

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
                            sb.AppendLine(TemplateUtils.htmlEncode (renderInterp (logicalLine.Trim()))) |> ignore
                        i <- nextI

            // Close remaining open tags
            while indentStack.Length > 0 do
                let (_, closeTag) = indentStack.Head
                indentStack <- indentStack.Tail
                sb.Append(sprintf "</%s>" closeTag) |> ignore

            sb.ToString().TrimEnd())
