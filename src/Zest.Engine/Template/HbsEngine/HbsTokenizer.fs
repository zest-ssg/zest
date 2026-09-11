namespace Zest.Engine.Template

open System
open HbsTypes

// HbsTokenizer.fs
//
// Scans a Handlebars/Mustache template into HbsToken values in a single pass.
// Handles `{{ }}`, `{{{ }}}`, `{{!-- --}}`, and `{{! }}` in one scan.
//
// Invariant: tokens are produced in source order with no lookahead buffering.

module internal HbsTokenizer =

    let tokenize (src: string) : HbsToken list =
        let tokens = ResizeArray<HbsToken>()
        let sb = Text.StringBuilder()
        let len = src.Length
        let mutable i = 0
        let flushText () =
            if sb.Length > 0 then
                tokens.Add(TText(sb.ToString()))
                sb.Clear() |> ignore
        let findClose (start: int) (closeLen: int) =
            let mutable j = start
            let mutable found = -1
            while found < 0 && j + closeLen <= len do
                if src.[j] = '}' && (closeLen = 1 || (closeLen = 2 && src.[j + 1] = '}') || (closeLen = 3 && j + 2 < len && src.[j + 1] = '}' && src.[j + 2] = '}')) then
                    found <- j
                else j <- j + 1
            found
        while i < len do
            let c = src.[i]
            if c = '{' && i + 1 < len && src.[i + 1] = '{' then
                // triple mustache `{{{ ... }}}`?
                let triple = i + 2 < len && src.[i + 2] = '{' && i + 3 < len && src.[i + 3] <> '{'
                let openLen = if triple then 3 else 2
                let start = i + openLen
                // comment `{{!-- ... --}}`?
                if not triple && start + 2 < len && src.[start] = '!' && src.[start + 1] = '-' && src.[start + 2] = '-' then
                    let closeIdx = src.IndexOf("--}}", start + 3, StringComparison.Ordinal)
                    if closeIdx >= 0 then
                        flushText ()
                        tokens.Add(TComment)
                        i <- closeIdx + 4
                    else
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                else
                    let closeLen = if triple then 3 else 2
                    let closeIdx = findClose start closeLen
                    if closeIdx < 0 then
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                    else
                        flushText ()
                        let raw = src.Substring(start, closeIdx - start).Trim()
                        let classify =
                            if raw.StartsWith("!") then TComment
                            elif raw.StartsWith(">") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TPartial(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("#") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TBlockOpen(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("/") then
                                TBlockClose(raw.Substring(1).Trim())
                            elif raw.StartsWith("^") then
                                let rest = raw.Substring(1).Trim()
                                let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
                                if parts.Length = 0 then TComment
                                else TInverted(parts.[0], String.Join(" ", parts.[1..]))
                            elif raw.StartsWith("else if", StringComparison.Ordinal) then
                                TElseIf(raw.Substring(8).Trim())
                            elif raw = "else" then TElse
                            elif raw.StartsWith("&") then
                                TExpr(raw.Substring(1).Trim(), true)
                            else TExpr(raw, triple)
                        tokens.Add(classify)
                        i <- closeIdx + closeLen
            else
                sb.Append(c) |> ignore
                i <- i + 1
        flushText ()
        tokens |> Seq.toList
