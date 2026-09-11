namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO

// NunjucksEngine.fs
//
// Public Nunjucks engine entry point. Implements ITemplateEngine by delegating
// to the tokenizer, evaluator, block collector, and renderer modules.
//
// Invariant: the public type name and namespace stay stable so TemplateManager
// and the build pipeline keep working without changes.

type NunjucksEngine() =

    let templateCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        try Ok(File.ReadAllText(path))
        with :? FileNotFoundException -> Error(sprintf "Template not found: %s" path)
           | ex -> Error(ex.Message)

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn

    interface ITemplateEngine with
        member _.Name = "nunjucks"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            let lastLine = ref 0
            try
                let tokens = NunjucksTokenizer.tokenize templateText
                let env: NunjucksRenderer.RenderEnv = {
                    Variables = variables
                    LoadTemplate = fun (path, depth) ->
                        if depth > 10 then Error("Circular include/extends detected")
                        else
                            match TemplateUtils.resolveWithinRoot path with
                            | Ok fullPath -> loadFileFn fullPath
                            | Error e -> Error e
                    ChildBlocks = dict [] :> IDictionary<_, _>
                    BlockStack = []
                    Depth = 0
                    Macros = Dictionary<string, ((string * string option) list * NunjucksTypes.Token list)>()
                    Blocks = dict [] :> IDictionary<_, _>
                    CurrentBlock = None
                    CallerBody = None
                    LoopNesting = 0
                    LastLine = lastLine
                    ControlFlow = ref ""
                }
                match NunjucksRenderer.renderTokens tokens env with
                | Ok s -> Ok s
                | Error msg -> Error(TemplateError.RuntimeError(msg, !lastLine))
            with ex -> Error(TemplateError.RuntimeError(ex.Message, !lastLine))

        member this.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            try
                let text =
                    match templateCache.TryGetValue filePath with
                    | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                    | _ ->
                        let t = File.ReadAllText(filePath)
                        templateCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                        t
                (this :> ITemplateEngine).Render text variables
            with :? FileNotFoundException -> Error(TemplateError.NotFound filePath)
               | ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

        member _.RegisterFilter(name: string) (fn: FilterFn) =
            NunjucksEvaluator.customFilters.[name] <- fn

        member _.RegisterTag(handler: TagHandler) = ()

        member _.ClearCache() =
            templateCache.Clear()
            NunjucksTokenizer.tokenCache.Clear()
