namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO

// HbsEngine.fs
//
// Public Handlebars/Mustache engine entry point. Implements ITemplateEngine by
// delegating to the tokenizer, parser, context, and renderer modules.
//
// Invariant: the public type name and namespace stay stable so TemplateManager,
// LayoutEngine, and ScriptEvaluator keep working without changes.

type HbsEngine() =

    let fileCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    // Default loader confines every read to the working directory so a
    // malicious {% include %} cannot traverse outside the site root.
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        match TemplateUtils.resolveWithinRoot path with
        | Ok fullPath ->
            try Ok(File.ReadAllText(fullPath))
            with :? FileNotFoundException -> Error(sprintf "Template not found: %s" fullPath)
               | ex -> Error(ex.Message)
        | Error e -> Error e

    let mutable partialLoader: string -> string option = fun _ -> None

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn
    member _.SetPartialLoader(fn: string -> string option) = partialLoader <- fn

    interface ITemplateEngine with
        member _.Name = "hbs"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            HbsRenderer.render templateText variables partialLoader

        member _.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            try
                let text =
                    match fileCache.TryGetValue filePath with
                    | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                    | _ ->
                        let t = File.ReadAllText(filePath)
                        fileCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                        t
                HbsRenderer.render text variables partialLoader
            with :? FileNotFoundException -> Error(TemplateError.NotFound filePath)
               | ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

        member _.RegisterFilter(_name: string) (_fn: FilterFn) = () // Hbs has no filters

        member _.RegisterTag(_handler: TagHandler) = ()

        member _.ClearCache() =
            fileCache.Clear()
            HbsRenderer.clearCaches()
