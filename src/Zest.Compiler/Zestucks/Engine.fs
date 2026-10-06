namespace Zest.Compiler.Zestucks
open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open Zest.Compiler.Model
// Engine.fs
//
// The Zestucks engine: the only template engine Zest has. It delegates to the
// tokenizer, evaluator, block collector, and renderer modules.
//
// Threading: Render must stay safe to call from parallel page workers, so no
// mutable per-render state is shared between calls. The only mutable slots are
// the file loader and the caches, both of which are written once during build
// setup.
//
// Dependencies: Tokens, Tokenizer, Renderer,
// Evaluator, FileTypes

/// <summary>
/// Renders Zestucks templates — Zest's Nunjucks-compatible template language.
/// </summary>
type Engine() =

    let templateCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    let mutable loadFileFn: string -> Result<string, string> = fun path ->
        try Ok(File.ReadAllText(path))
        with :? FileNotFoundException -> Error(sprintf "Template not found: %s" path)
           | ex -> Error(ex.Message)

    /// <summary>
    /// Replace the loader used to satisfy {% include %} / {% extends %}.
    /// </summary>
    /// <param name="fn">Maps a resolved absolute path to file contents.</param>
    member _.SetLoadFile(fn: string -> Result<string, string>) = loadFileFn <- fn

    /// <summary>
    /// Renders a template string with the supplied variables.
    /// </summary>
    /// <param name="templateText">Raw template source.</param>
    /// <param name="variables">Flat or nested context bag for interpolation.</param>
    /// <returns>Rendered output, or a typed error with a source location.</returns>
    member _.Render(templateText: string) (variables: IDictionary<string, obj>) : Result<string, ZestucksError> =
        let lastLine = ref 0
        try
            let tokens = Tokenizer.tokenize templateText
            let env: Renderer.RenderEnv = {
                Variables = variables
                LoadTemplate = fun (path, depth) ->
                    if depth > 10 then Error("Circular include/extends detected")
                    else
                        match ZestucksPaths.resolveWithinRoot path with
                        | Ok fullPath -> loadFileFn fullPath
                        | Error e -> Error e
                ChildBlocks = dict [] :> IDictionary<_, _>
                BlockStack = []
                Depth = 0
                Macros = Dictionary<string, ((string * string option) list * Tokens.TokenRange)>()
                Blocks = dict [] :> IDictionary<_, _>
                CurrentBlock = None
                CallerBody = None
                MacroDepth = 0
                LoopNesting = 0
                LastLine = lastLine
                ControlFlow = ref ""
            }
            match Renderer.renderTokens tokens env with
            | Ok s -> Ok s
            | Error msg -> Error(ZestucksError.RuntimeError(msg, !lastLine))
        with ex -> Error(ZestucksError.RuntimeError(ex.Message, !lastLine))

    /// <summary>
    /// Renders a template file, caching its contents by last-write time.
    /// </summary>
    /// <param name="filePath">Absolute path to the template file.</param>
    /// <param name="variables">Context bag for interpolation.</param>
    /// <returns>Rendered output, or NotFound for a missing file.</returns>
    member this.RenderFile(filePath: string) (variables: IDictionary<string, obj>) : Result<string, ZestucksError> =
        try
            let text =
                match templateCache.TryGetValue filePath with
                | true, struct(mtime, cached) when mtime = File.GetLastWriteTimeUtc(filePath) -> cached
                | _ ->
                    let t = File.ReadAllText(filePath)
                    templateCache.[filePath] <- struct(File.GetLastWriteTimeUtc(filePath), t)
                    t
            this.Render text variables
        with :? FileNotFoundException -> Error(ZestucksError.NotFound filePath)
           | ex -> Error(ZestucksError.RuntimeError(ex.Message, 0))

    /// <summary>
    /// Registers a custom filter usable as `| name` in templates.
    /// </summary>
    member _.RegisterFilter(name: string) (fn: ZestucksFilter) =
        Evaluator.customFilters.[name] <- fn

    /// <summary>Clears every engine-owned cache (templates, tokens, file cache).</summary>
    member _.ClearCache() =
        templateCache.Clear()
        Tokenizer.tokenCache.Clear()
