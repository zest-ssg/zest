namespace Zest.Engine.Zcss

open System

// ============================================================
// ZSS Parser — Main entry point
// ============================================================

module Parser =

    open ParserCore

    let parse (source: string) : ZssNode list =
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

    let getErrors () = ParserCore.getErrors()
