namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Markdown → HTML
// ============================================================

module Markdown =
    let private codeBlockPat    = Regex(@"(?s)```(\w*)\r?\n(.*?)```",       RegexOptions.Compiled)
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
