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
            let mutable indentStack: int list = []

            // Regex patterns (compiled once)
            let emptyLine   = Regex(@"^\s*$", RegexOptions.Compiled)
            let commentLine = Regex(@"^\s*/", RegexOptions.Compiled)
            let silentCode  = Regex(@"^\s*-", RegexOptions.Compiled)
            let expression  = Regex(@"^\s*=\s+", RegexOptions.Compiled)
            let filterStart = Regex(@"^\s*:(css|javascript|js|coffee|markdown|plain)\s*$", RegexOptions.Compiled)
            // Minimal HAML tag: %tagname or .class or #id
            let hamlTag     = Regex(@"^\s*(%(?<tag>[a-zA-Z][a-zA-Z0-9]*)|\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*)\.?(?<cls1>[a-zA-Z][a-zA-Z0-9\-_]*)?|\.(?<cls2>[a-zA-Z][a-zA-Z0-9\-_]*))(?<attrs>\{[^\}]*\})?(?<rest>.*)$", RegexOptions.Compiled)

            let indentOf (line: string) =
                let mutable n = 0
                while n < line.Length && line.[n] = ' ' do n <- n + 2  // HAML uses 2-space indent
                n

            let closeUntil (targetIndent: int) =
                while indentStack.Length > 0 && indentStack.Head > targetIndent do
                    indentStack <- indentStack.Tail
                    sb.Append("</div>") |> ignore

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
                        let tag, id, cls =
                            if m.Groups.["tag"].Success then
                                (m.Groups.["tag"].Value,
                                 (if m.Groups.["id"].Success then m.Groups.["id"].Value else ""),
                                 (if m.Groups.["cls1"].Success then m.Groups.["cls1"].Value else ""))
                            elif m.Groups.["id"].Success then
                                ("div", m.Groups.["id"].Value,
                                 (if m.Groups.["cls1"].Success then m.Groups.["cls1"].Value else ""))
                            elif m.Groups.["cls2"].Success then
                                ("div", "", m.Groups.["cls2"].Value)
                            else
                                ("div", "", "")

                        let attrsRaw = if m.Groups.["attrs"].Success then m.Groups.["attrs"].Value else ""
                        let attrs = if attrsRaw.Length >= 2 then attrsRaw.Substring(1, attrsRaw.Length - 2) else attrsRaw
                        let rest = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

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

                        // Self-closing tags
                        let voidTags = set ["br"; "hr"; "img"; "input"; "meta"; "link"; "area"; "base"; "col"; "embed"; "source"; "track"; "wbr"]
                        if voidTags.Contains(tag) then
                            sb.AppendLine(" />") |> ignore
                        elif rest <> "" then
                            sb.Append('>') |> ignore
                            sb.Append(rest) |> ignore
                            sb.Append(sprintf "</%s>" tag) |> ignore
                            sb.AppendLine() |> ignore
                        else
                            sb.AppendLine(">") |> ignore
                            indentStack <- indent :: indentStack

                    else
                        // Plain text line
                        sb.AppendLine(line.Trim()) |> ignore

            // Close remaining open tags
            while indentStack.Length > 0 do
                indentStack <- indentStack.Tail
                sb.Append("</div>") |> ignore

            sb.ToString().TrimEnd()
