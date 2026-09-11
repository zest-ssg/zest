namespace Zest.Engine.Template

open System
open HbsTypes

// HbsTokenizer.fs
//
// Scans a Handlebars/Mustache template into HbsToken values in a single pass.
// Handles `{{ }}`, `{{{ }}}`, `{{! }}`, `{{!-- --}}`, `{{> }}`, `{{#> }}`,
// `{{# }}`, `{{/ }}`, `{{^ }}`, `else`, `else if`, block params (`as |x y|`),
// and the `~` whitespace-control marker on either side of a tag.
//
// Invariant: tokens are produced in source order with no lookahead buffering.
// Whitespace control is applied eagerly to the text buffer so a later token
// never needs to rewrite an earlier one.

module internal HbsTokenizer =

    /// Split `name args as |a b|` into the main text and block-parameter names.
    let private splitBlockParams (s: string) : string * string list =
        let marker = " as |"
        let idx = s.IndexOf(marker, StringComparison.Ordinal)
        if idx >= 0 && s.EndsWith("|") then
            let main = s.[..idx - 1].Trim()
            let paramsText = s.Substring(idx + marker.Length, s.Length - idx - marker.Length - 1).Trim()
            let ps =
                paramsText.Split([|' '; '\t'; '\r'; '\n'|], StringSplitOptions.RemoveEmptyEntries)
                |> Array.toList
            main, ps
        else
            s, []

    /// Classify a `{{...}}` body into the token it represents.
    let private classify (raw: string) (triple: bool) : HbsToken =
        if raw.StartsWith("!") then TComment
        elif raw.StartsWith("#>") then
            let rest = raw.Substring(2).Trim()
            let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length = 0 then TComment
            else TPartialBlock(parts.[0], String.Join(" ", parts.[1..]))
        elif raw.StartsWith(">") then
            let rest = raw.Substring(1).Trim()
            let parts = rest.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length = 0 then TComment
            else TPartial(parts.[0], String.Join(" ", parts.[1..]))
        elif raw.StartsWith("#") then
            let rest = raw.Substring(1).Trim()
            let main, bp = splitBlockParams rest
            let parts = main.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length = 0 then TComment
            else TBlockOpen(parts.[0], String.Join(" ", parts.[1..]), bp)
        elif raw.StartsWith("/") then
            TBlockClose(raw.Substring(1).Trim())
        elif raw.StartsWith("^") then
            let rest = raw.Substring(1).Trim()
            let main, bp = splitBlockParams rest
            let parts = main.Split([| ' '; '\t'; '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            if parts.Length = 0 then TComment
            else TInverted(parts.[0], String.Join(" ", parts.[1..]), bp)
        elif raw.StartsWith("else if", StringComparison.Ordinal) then
            TElseIf(raw.Substring(8).Trim())
        elif raw = "else" then TElse
        elif raw.StartsWith("&") then
            TExpr(raw.Substring(1).Trim(), true)
        else
            TExpr(raw, triple)

    let tokenize (src: string) : HbsToken list =
        let tokens = ResizeArray<HbsToken>()
        let sb = Text.StringBuilder()
        let len = src.Length
        let mutable i = 0
        let mutable stripLeftNext = false   // strip leading WS of the next text token (set by a trailing `~`)

        let flush () =
            if sb.Length > 0 then
                let t = if stripLeftNext then (stripLeftNext <- false; sb.ToString().TrimStart()) else sb.ToString()
                tokens.Add(TText t)
                sb.Clear() |> ignore

        // Strip trailing whitespace already captured into the text buffer
        // (set by a leading `~` on the current tag).
        let removeTrailingWs () =
            if sb.Length > 0 then
                let t = sb.ToString()
                let trimmed = t.TrimEnd()
                sb.Clear() |> ignore
                sb.Append(trimmed) |> ignore

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
                // triple mustache `{{{ ... }}}` unless it is `{{{{` (a literal brace).
                let triple = i + 2 < len && src.[i + 2] = '{' && (i + 3 >= len || src.[i + 3] <> '{')
                let openLen = if triple then 3 else 2
                let mutable cursor = i + openLen

                // `{{!-- ... --}}` comment (only for double mustache).
                if not triple && cursor + 2 < len && src.[cursor] = '!' && src.[cursor + 1] = '-' && src.[cursor + 2] = '-' then
                    let closeIdx = src.IndexOf("--}}", cursor + 3, StringComparison.Ordinal)
                    if closeIdx >= 0 then
                        flush ()
                        tokens.Add TComment
                        i <- closeIdx + 4
                    else
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                else
                    // Leading `~` strips whitespace already in the text buffer.
                    if cursor < len && src.[cursor] = '~' then
                        removeTrailingWs ()
                        cursor <- cursor + 1
                    let closeLen = if triple then 3 else 2
                    let closeIdx = findClose cursor closeLen
                    if closeIdx < 0 then
                        sb.Append(src, i, len - i) |> ignore
                        i <- len
                    else
                        // Trailing `~` strips the leading whitespace of the next text token.
                        let mutable innerEnd = closeIdx
                        if innerEnd > cursor && src.[innerEnd - 1] = '~' then
                            stripLeftNext <- true
                            innerEnd <- innerEnd - 1
                        flush ()
                        let raw = src.Substring(cursor, innerEnd - cursor).Trim()
                        tokens.Add (classify raw triple)
                        i <- closeIdx + closeLen
            else
                sb.Append(c) |> ignore
                i <- i + 1
        flush ()
        tokens |> Seq.toList
