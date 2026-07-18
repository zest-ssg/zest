namespace Zest.Engine.Zcss

open System
open System.Text.RegularExpressions

// ============================================================
// ZCSS Parser — Main entry point
// ============================================================

module Parser =

    open ParserCore

    let parse (source: string) : ZcssNode list =
        clearErrors()
        if String.IsNullOrWhiteSpace source then []
        else
            let cleaned = stripComments source
            let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
            let vars = extractVars lines
            let mode = detectMode lines
            match mode with
            | BraceMode ->
                let result, _ = ParserBrace.parseBraceBlock 0 lines vars
                result
            | IndentMode ->
                let result, _ = ParserIndent.parseIndentBlock 0 lines 0 vars
                result
            | BracketMode ->
                // F#-style bracket syntax: `[ ... ]` blocks (F# list literals).
                // Convert block brackets to `{}` then reuse the brace parser.
                let converted = ParserCore.toBraceLines lines
                let result, _ = ParserBrace.parseBraceBlock 0 converted vars
                result

    let getErrors () = ParserCore.getErrors()
