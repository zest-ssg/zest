namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions

// ============================================================
// PugConverter — Pug → HTML Converter
// ============================================================
// Converts Pug (indentation-based) syntax to HTML.
// Covers the most common Pug features (formerly Jade).
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
//   :markdown      → pass-through (no conversion)
//
// This is a best-effort, EXPERIMENTAL converter.
// Full Pug compatibility is NOT guaranteed.
// ============================================================

module PugConverter =

    /// Minimal Pug-to-HTML conversion.
    /// Returns the HTML string (not wrapped in additional tags).
    let convert (pug: string) : string =
        if String.IsNullOrWhiteSpace pug then ""
        else
            let lines = pug.Replace("\r\n", "\n").Split('\n')
            let sb = StringBuilder()
            let mutable indentStack: int list = []

            let emptyLine  = Regex(@"^\s*$", RegexOptions.Compiled)
            let comment    = Regex(@"^\s*//-?\s*", RegexOptions.Compiled)
            let expression = Regex(@"^\s*=\s+", RegexOptions.Compiled)
            let silentCode = Regex(@"^\s*-\s+", RegexOptions.Compiled)
            let pipeText   = Regex(@"^\s*\|\s*", RegexOptions.Compiled)
            let pugTag     = Regex(@"^\s*(?<tag>[a-zA-Z][a-zA-Z0-9]*)?(\#(?<id>[a-zA-Z][a-zA-Z0-9\-_]*))?(\.(?<cls>[a-zA-Z][a-zA-Z0-9\-_]+))*(\((?<attrs>[^\)]*)\))?(?<rest>.*)$", RegexOptions.Compiled)

            let indentOf (line: string) =
                let mutable n = 0
                while n < line.Length && line.[n] = ' ' do n <- n + 2
                n

            let closeUntil (targetIndent: int) =
                while indentStack.Length > 0 && indentStack.Head > targetIndent do
                    indentStack <- indentStack.Tail
                    sb.Append("</div>") |> ignore

            for line in lines do
                if emptyLine.IsMatch(line) then
                    sb.AppendLine() |> ignore
                elif comment.IsMatch(line) && line.Trim().StartsWith("//") then
                    let c = comment.Replace(line.Trim(), "")
                    if not (line.Trim().StartsWith("//-")) then
                        sb.AppendLine(sprintf "<!-- %s -->" (c.Trim())) |> ignore
                elif silentCode.IsMatch(line) && line.Trim().StartsWith("-") then
                    ()
                elif expression.IsMatch(line) then
                    let exp = expression.Replace(line.Trim(), "")
                    sb.AppendLine(sprintf "{{ %s }}" exp) |> ignore
                elif pipeText.IsMatch(line) then
                    let text = pipeText.Replace(line, "")
                    sb.Append(text).AppendLine() |> ignore
                else
                    let indent = indentOf line
                    closeUntil indent

                    let m = pugTag.Match(line)
                    if m.Success then
                        let tagRaw = if m.Groups.["tag"].Success then m.Groups.["tag"].Value else ""
                        let id     = if m.Groups.["id"].Success  then m.Groups.["id"].Value  else ""
                        let cls    = if m.Groups.["cls"].Success then m.Groups.["cls"].Value else ""
                        let attrs  = if m.Groups.["attrs"].Success then m.Groups.["attrs"].Value else ""
                        let rest   = if m.Groups.["rest"].Success then m.Groups.["rest"].Value.Trim() else ""

                        // If no tag specified but there's a class or id, default to div
                        let tag = if tagRaw = "" && (id <> "" || cls <> "") then "div"
                                  elif tagRaw = "" then ""  // text
                                  else tagRaw

                        if tag = "" then
                            // Plain text
                            let text = line.Trim()
                            if text <> "" then sb.AppendLine(text) |> ignore
                        else
                            sb.Append('<') |> ignore
                            sb.Append(tag) |> ignore
                            if id <> "" then sb.Append(sprintf " id=\"%s\"" id) |> ignore
                            if cls <> "" then
                                let clsNormalized = cls.Replace(".", " ")
                                sb.Append(sprintf " class=\"%s\"" clsNormalized) |> ignore
                            if attrs <> "" then
                                // Parse Pug attributes: key=value or key="value with spaces"
                                let attrRegex = Regex(@"(?<key>\w+)=(""|'|)(?<value>.*?)\2(?=\s|$)", RegexOptions.Compiled)
                                for am in attrRegex.Matches(attrs) do
                                    let ak = am.Groups.["key"].Value
                                    let av = am.Groups.["value"].Value
                                    sb.Append(sprintf " %s=\"%s\"" ak av) |> ignore

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
                        sb.AppendLine(line.Trim()) |> ignore

            while indentStack.Length > 0 do
                indentStack <- indentStack.Tail
                sb.Append("</div>") |> ignore

            sb.ToString().TrimEnd()
