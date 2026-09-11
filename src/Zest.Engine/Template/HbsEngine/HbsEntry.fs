namespace Zest.Engine.Template

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO

// HbsEntry.fs
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

    // User-registered helpers, keyed by helper name. Built-in helpers live in
    // HbsRenderer and are merged with these per render; a user helper of the
    // same name shadows the built-in.
    let userHelpers = ConcurrentDictionary<string, HbsHelper>()

    /// Snapshot the registered helpers into an immutable map for one render.
    let helperMap () =
        userHelpers |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq

    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn
    member _.SetPartialLoader(fn: string -> string option) = partialLoader <- fn

    /// <summary>
    /// Registers an inline helper under a name usable as `{{name arg key=value}}`.
    /// The helper receives resolved positional and hash arguments and its
    /// return value is HTML-escaped unless the template uses a triple mustache.
    /// </summary>
    member _.RegisterHelper(name: string, fn: HbsHelper) = userHelpers.[name] <- fn

    interface ITemplateEngine with
        member _.Name = "hbs"

        member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            HbsRenderer.render templateText variables (helperMap ()) partialLoader

        member _.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, TemplateError> =
            try
                let text =
                    match fileCache.TryGetValue filePath with
                    | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                    | _ ->
                        let t = File.ReadAllText(filePath)
                        fileCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                        t
                HbsRenderer.render text variables (helperMap ()) partialLoader
            with :? FileNotFoundException -> Error(TemplateError.NotFound filePath)
               | ex -> Error(TemplateError.RuntimeError(ex.Message, 0))

        member _.RegisterFilter(_name: string) (_fn: FilterFn) = () // Hbs has no filters

        member _.RegisterTag(_handler: TagHandler) = ()

        member _.ClearCache() =
            fileCache.Clear()
            HbsRenderer.clearCaches()
