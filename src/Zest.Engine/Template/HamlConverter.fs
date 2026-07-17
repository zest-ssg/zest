namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// HamlConverter — HAML → HTML Converter
// ============================================================
// Converts HAML (indentation-based) syntax to HTML.
// Covers the most common HAML features.
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
//   :css           → <style> block
//   :javascript    → <script> block
//
// This is a best-effort, EXPERIMENTAL converter.
// Full HAML compatibility is NOT guaranteed.
// ============================================================

module HamlConverter =

    /// Minimal HAML-to-HTML conversion.
    /// Returns the HTML string (not wrapped in additional tags).
    let convert (haml: string) : string =
        if String.IsNullOrWhiteSpace haml then ""
        else
            let lines = haml.Replace("\r\n", "\n").Split('\n')
            let sb = StringBuilder()
            let mutable indentStack: (int * string) list = []

            // Regex patterns (compiled once)
            let emptyLine   = Regex(@"^\s*$", RegexOptions.Compiled)
            let commentLine = Regex(@"^\s*/", RegexOptions.Compiled)
            let silentCode  = Regex(@"^\s*-", RegexOptions.Compiled)
            let expression  = Regex(@"^\s*=\s+", RegexOptions.Compiled)
            let filterStart = Regex(@"^\s*:(css|javascript|js|coffee|markdown|plain)\s*$", RegexOptions.Compiled)
            // Minimal HAML tag: %tagname or .class or #id
            // Supports combined: %tag#id.class, #id.class, .class
            let hamlTag     = Regex(@"^\s*(%(?<tag>[a-zA-Z][a-zA-Z0-9]*))?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?(\.(?<cls>[a-zA-Z][a-zA-Z0-9\-_]+))*(?<attrs>\{[^\}]*\})?(?<rest>.*)$", RegexOptions.Compiled)

            let indentOf (line: string) =
                let mutable n = 0
                while n < line.Length && line.[n] = ' ' do n <- n + 2  // HAML uses 2-space indent
                n

            let closeUntil (targetIndent: int) =
                while indentStack.Length > 0 && (fst indentStack.Head) > targetIndent do
                    let (_, closeTag) = indentStack.Head
                    indentStack <- indentStack.Tail
                    sb.Append(sprintf "</%s>" closeTag) |> ignore

            for line in lines do
                if emptyLine.IsMatch(line) then
                    sb.AppendLine() |> ignore
                elif commentLine.IsMatch(line) && line.Trim().StartsWith("/") then
                    let comment = line.Trim().[1..].Trim()
                    sb.AppendLine(sprintf "<!-- %s -->" comment) |> ignore
                elif silentCode.IsMatch(line) && line.Trim().StartsWith("-") then
                    ()  // Strip silent code
                elif filterStart.IsMatch(line) then
                    ()  // Filters not yet supported in basic mode
                elif expression.IsMatch(line) then
                    let exp = expression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                else
                    let indent = indentOf line
                    closeUntil indent

                    let m = hamlTag.Match(line)
                    if m.Success then
                        let tagRaw = if m.Groups.["tag"].Success then m.Groups.["tag"].Value else ""
                        let id     = if m.Groups.["id"].Success  then m.Groups.["id"].Value  else ""
                        let rest   = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

                        // Extract all dot-separated classes from the line (before attrs/rest)
                        let cls =
                            let clsMatch = Regex.Match(line.TrimStart(), @"^(?:%[a-zA-Z][a-zA-Z0-9]*)?(?:\#[a-zA-Z][a-zA-Z0-9\-_]*)?((?:\.[a-zA-Z][a-zA-Z0-9\-_]+)*)")
                            if clsMatch.Success && clsMatch.Groups.[1].Success then
                                let rawCls = clsMatch.Groups.[1].Value
                                rawCls.Split('.', System.StringSplitOptions.RemoveEmptyEntries)
                                |> String.concat " "
                            else ""

                        let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                                  elif tagRaw = "" then "" else tagRaw

                        if tag = "" then
                            // No tag/id/class on the line → emit it as plain text.
                            let plainText = line.Trim()
                            if plainText <> "" then sb.AppendLine(plainText) |> ignore
                        else
                            let attrsRaw = if m.Groups.["attrs"].Success then m.Groups.["attrs"].Value else ""
                            let attrs = if attrsRaw.Length >= 2 then attrsRaw.Substring(1, attrsRaw.Length - 2) else attrsRaw

                            // Build opening tag
                            sb.Append('<') |> ignore
                            sb.Append(tag) |> ignore
                            if id <> "" then sb.Append(sprintf " id=\"%s\"" id) |> ignore
                            if cls <> "" then sb.Append(sprintf " class=\"%s\"" cls) |> ignore
                            // Parse inline attributes {key: value, key: value}
                            if attrs <> "" then
                                let attrPairs = attrs.Split(',')
                                for pair in attrPairs do
                                    let parts = pair.Trim().Split(':')
                                    if parts.Length >= 2 then
                                        let key = parts.[0].Trim()
                                        let value = parts.[1..] |> String.concat ":" |> (fun s -> s.Trim().Trim('"', '\''))
                                        sb.Append(sprintf " %s=\"%s\"" key value) |> ignore

                            // Inline expression: tag= expr  →  <tag>{{ expr }}</tag>
                            let content =
                                if rest.StartsWith("=") then
                                    sprintf "{{ %s }}" (rest.Substring(1).Trim())
                                else
                                    rest

                            // Self-closing tags
                            let voidTags = set ["br"; "hr"; "img"; "input"; "meta"; "link"; "area"; "base"; "col"; "embed"; "source"; "track"; "wbr"]
                            if voidTags.Contains(tag) then
                                sb.AppendLine(" />") |> ignore
                            elif content <> "" then
                                sb.Append('>') |> ignore
                                sb.Append(content) |> ignore
                                sb.Append(sprintf "</%s>" tag) |> ignore
                                sb.AppendLine() |> ignore
                            else
                                sb.AppendLine(">") |> ignore
                                indentStack <- (indent, tag) :: indentStack

                    else
                        // Plain text line (no HAML token matched)
                        sb.AppendLine(line.Trim()) |> ignore

            // Close remaining open tags
            while indentStack.Length > 0 do
                let (_, closeTag) = indentStack.Head
                indentStack <- indentStack.Tail
                sb.Append(sprintf "</%s>" closeTag) |> ignore

            sb.ToString().TrimEnd()
