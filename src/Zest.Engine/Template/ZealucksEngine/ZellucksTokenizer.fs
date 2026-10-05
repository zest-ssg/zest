namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.IO
open System.Text
open NunjucksTypes

// NunjucksTokenizer.fs
//
// Turns raw template text into a Token list in a single pass.
// Caches tokens per file by last-write time so repeated renders skip scanning.
//
// Invariant: tokenCache keys are absolute, normalized paths.

module internal NunjucksTokenizer =

    // ── Tokenizer (idempotent, cached) ─────────────────────
    // ConcurrentDictionary: many threads may populate the cache for the same
    // uncached template simultaneously; a plain Dictionary can corrupt/throw.
    let tokenCache = ConcurrentDictionary<string, struct(DateTime * Token list)>()

    let tokenize (text: string) : Token list =
        let tokens = ResizeArray<Token>()
        let sb = StringBuilder()
        let len = text.Length
        let mutable i = 0
        let mutable line = 1
        let mutable stripLeftNext = false   // strip leading WS of the next text token (set by `-}}` / `-%}`)

        // Helpers that track the current source line as characters are consumed.
        let addChar (ch: char) =
            if ch = '\n' then line <- line + 1
            sb.Append(ch) |> ignore
        let addStr (s: string) =
            for ch in s do if ch = '\n' then line <- line + 1
            sb.Append(s) |> ignore

        // Remove trailing whitespace from the last emitted text token (for `{{-` / `{%-`).
        let removeTrailingWs () =
            if tokens.Count > 0 then
                match tokens.[tokens.Count - 1] with
                | TextToken(t, l) ->
                    if t.Length > 0 && (t |> Seq.forall Char.IsWhiteSpace) then tokens.RemoveAt(tokens.Count - 1)
                    else
                        let trimmed = t.TrimEnd()
                        if trimmed <> t then tokens.[tokens.Count - 1] <- TextToken(trimmed, l)
                | _ -> ()

        let flush () =
            if sb.Length > 0 then
                let t = if stripLeftNext then (stripLeftNext <- false; sb.ToString().TrimStart()) else sb.ToString()
                tokens.Add(TextToken(t, line)); sb.Clear() |> ignore

        while i < len do
            if i + 2 < len then
                let c = text.[i]
                if c = '{' && text.[i+1] = '#' then               // {# comment #} (supports nesting)
                    flush()
                    let cl = line
                    let mutable j = i + 2
                    let mutable depth = 1
                    let mutable endPos = -1
                    while j + 1 < len && endPos < 0 do
                        if text.[j] = '{' && text.[j+1] = '#' then depth <- depth + 1; j <- j + 2
                        elif text.[j] = '#' && text.[j+1] = '}' then
                            depth <- depth - 1
                            if depth = 0 then endPos <- j else j <- j + 2
                        else j <- j + 1
                    if endPos < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let commentText = text.Substring(i+2, endPos - (i+2))
                        // advance the line counter over any newlines inside the comment
                        for ch in commentText do if ch = '\n' then line <- line + 1
                        tokens.Add(CmtToken(commentText, cl)); i <- endPos + 2
                elif c = '{' && text.[i+1] = '{' then              // {{ var }} / {{- var -}}
                    flush()
                    let cl = line
                    let mutable lstrip = false
                    let mutable ci = i + 2
                    if ci < len && text.[ci] = '-' then lstrip <- true; ci <- ci + 1
                    let e = text.IndexOf("}}", ci)
                    if e < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let mutable rstrip = false
                        let innerEnd = if e >= 1 && text.[e-1] = '-' then (rstrip <- true; e - 1) else e
                        let expr = if innerEnd > ci then text.Substring(ci, innerEnd - ci).Trim() else ""
                        if lstrip then removeTrailingWs ()
                        tokens.Add(VarToken(expr, cl))
                        if rstrip then stripLeftNext <- true
                        i <- e + 2
                elif c = '{' && text.[i+1] = '%' then              // {% tag %} / {%- tag -%}
                    flush()
                    let cl = line
                    let mutable lstrip = false
                    let mutable ci = i + 2
                    if ci < len && text.[ci] = '-' then lstrip <- true; ci <- ci + 1
                    let e = text.IndexOf("%}", ci)
                    if e < 0 then addStr (text.Substring(i)); i <- len
                    else
                        let mutable rstrip = false
                        let innerEnd = if e >= 1 && text.[e-1] = '-' then (rstrip <- true; e - 1) else e
                        let raw = if innerEnd > ci then text.Substring(ci, innerEnd - ci).Trim() else ""
                        let parts = raw.Split([|' ';'\n';'\t';'\r'|], StringSplitOptions.RemoveEmptyEntries)
                        let tag = if parts.Length > 0 then parts.[0] else ""
                        let args = if parts.Length > 1 then parts.[1..] |> Array.toList else []
                        // Left-strip before emitting this tag's token.
                        if lstrip then removeTrailingWs ()
                        // raw tag: capture everything until {% endraw %} as literal text
                        if tag = "raw" then
                            let rawEnd = text.IndexOf("{% endraw %}", e+2)
                            if rawEnd >= e+2 then
                                let rawContent = text.Substring(e+2, rawEnd - (e+2))
                                tokens.Add(TextToken(rawContent, cl))
                                i <- rawEnd + "{% endraw %}".Length
                            else
                                tokens.Add(TagToken(tag, args, cl))
                                i <- e + 2
                        else
                            tokens.Add(TagToken(tag, args, cl)); i <- e + 2
                        if rstrip then stripLeftNext <- true
                else addChar c; i <- i + 1
            else addChar text.[i]; i <- i + 1
        flush()
        Seq.toList tokens

    let tokenizeFile (path: string) =
        let mtime = File.GetLastWriteTimeUtc(path)
        match tokenCache.TryGetValue path with
        | true, struct(cm, _) when cm = mtime -> tokenCache.[path]
        | _ ->
            let text = File.ReadAllText(path)
            let tokens = tokenize text
            tokenCache.[path] <- struct(mtime, tokens)
            struct(mtime, tokens)
