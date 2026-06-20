namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open FSharp.Compiler.Interactive.Shell
open Zest.Engine
open Zest.Engine.Html

/// <summary>
/// Runtime evaluator for .zest.fsx scripts using FSharp.Compiler.Service FsiEvaluationSession.
/// Scripts return rendered HTML as a string via HtmlRenderer.render or a pure F# expression.
/// </summary>
module ScriptRunner =

    let mutable private GlobalData : IDictionary<string, obj> = dict []

    let setGlobalData (data: IDictionary<string, obj>) = GlobalData <- data

    let getData (key: string) : obj =
        let mutable v : obj = null
        if GlobalData.TryGetValue(key, &v) then v else null

    let getDataString (key: string) : string =
        let v = getData key
        if isNull v then "" else v.ToString()

    let getDataSection (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in GlobalData do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
            elif kv.Key = prefix then
                match kv.Value with
                | :? IDictionary<string, obj> as nested ->
                    for nkv in nested do d.[nkv.Key] <- nkv.Value
                | _ -> ()
        d :> _

    /// Check if a file should be evaluated as F# script vs plain Markdown.
    /// Files must start with F# code (render, let, page, #r, open) to be treated as scripts.
    let isPageScript (ext: string) (text: string) =
        if ext = ".md" || ext = ".markdown" then false
        else
            text.Split('\n')
            |> Array.map (fun l -> l.Trim())
            |> Array.tryFind (fun l ->
                not (String.IsNullOrEmpty l)
                && not (l.StartsWith("//"))
                && not (l.StartsWith("#r "))
                && not (l.StartsWith("#load ")))
            |> Option.map (fun l ->
                l.StartsWith("render") || l.StartsWith("page {") || l = "page"
                || l.StartsWith("let ") || l.StartsWith("open ")
                || l.StartsWith("HtmlRenderer."))
            |> Option.defaultValue false

    /// Evaluate a .zest.fsx script via FsiEvaluationSession.
    /// The script must evaluate to a string (rendered HTML).
    let evaluatePageScript (scriptText: string) : Result<string, string> =
        try
            let engineDll = typeof<HtmlNode>.Assembly.Location

            let argv = [| "C:\\zest.exe"; "--noninteractive" |]
            use inStream = new StringReader("")
            use outStream = new StringWriter()
            use errStream = new StringWriter()

            let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
            use fsiSession =
                FsiEvaluationSession.Create(fsiConfig, argv, inStream, outStream, errStream)

            // Phase 1: Setup — load assembly, opens, and helper functions
            let setupCodes = [|
                sprintf "#r @\"%s\"" engineDll
                "open System"
                "open System.Collections.Generic"
                "open Zest.Engine"
                "open Zest.Engine.Html"
                "open Zest.Engine.Html.HtmlUtils"
                "let render (nodes: HtmlNode list) = HtmlRenderer.render nodes"
            |]
            for code in setupCodes do
                let _, diags = fsiSession.EvalInteractionNonThrowing(code)
                for d in diags do
                    if d.Severity.IsError then
                        eprintfn "[Zest] FSI setup: %s" d.Message

            // Phase 2: Evaluate user script as expression (returns string)
            let result, evalDiags = fsiSession.EvalExpressionNonThrowing(scriptText)

            for d in evalDiags do
                if d.Severity.IsError then
                    eprintfn "[Zest] FSI error: %s" d.Message
                elif d.Severity.IsWarning then
                    eprintfn "[Zest] FSI warning: %s" d.Message

            let errors = errStream.ToString()
            if not (String.IsNullOrEmpty errors) then
                eprintfn "[Zest] FSI stderr: %s" (errors.Trim())

            if evalDiags |> Array.exists (fun d -> d.Severity.IsError) then
                Error("FSI evaluation reported errors")
            else
                match result with
                | Choice1Of2 valueOpt ->
                    match valueOpt with
                    | Some value ->
                        match value.ReflectionValue with
                        | :? string as html -> Ok html
                        | other ->
                            Error(sprintf "Script returned %s instead of string" (other.GetType().Name))
                    | None -> Error("Script returned no value")
                | Choice2Of2 ex ->
                    Error(sprintf "FSI: %s" ex.Message)
        with
        | ex ->
            Error(sprintf "ScriptRunner threw: %s" ex.Message)
