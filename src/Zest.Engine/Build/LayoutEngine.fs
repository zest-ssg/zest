namespace Zest.Engine.Build

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Scripting
open Zest.Engine.Template

/// Layout loading, include processing, placeholder replacement, and recursive layout application.
/// Optimized with static Regex, single-pass directory traversal, HashSet-based key lookup, and filter registration caching.
module LayoutEngine =

    let private allowedLayoutExts =
        set [ FileExtensions.Html; FileExtensions.HtmlLong
              FileExtensions.Nunjucks; FileExtensions.Liquid
              FileExtensions.Handlebars; FileExtensions.Mustache
              FileExtensions.ZestScript; FileExtensions.FSharpScript ]

    let private layoutCache2 = ConcurrentDictionary<string, struct(DateTime * Map<string, string * string>)>()
    let internal loadLayouts (layoutsDir: string) =
        if not (Directory.Exists layoutsDir) then Map.empty
        else
            let mutable maxTicks = 0L
            let files = ResizeArray<string * string>()
            for f in Directory.EnumerateFiles(layoutsDir, "*.*", SearchOption.AllDirectories) do
                let ticks = File.GetLastWriteTimeUtc(f).Ticks
                if ticks > maxTicks then maxTicks <- ticks
                let ext = Path.GetExtension(f).ToLowerInvariant()
                if allowedLayoutExts.Contains ext then
                    let rec stripExts (name: string) =
                        let e = Path.GetExtension(name)
                        if String.IsNullOrEmpty e then name
                        else stripExts (Path.GetFileNameWithoutExtension(name))
                    let key = stripExts (Path.GetFileName(f))
                    files.Add(key, f)
            let mtime = if maxTicks > 0L then DateTime(maxTicks) else DateTime.MinValue
            let dirTicks = Directory.GetLastWriteTimeUtc(layoutsDir).Ticks
            let mtime = if dirTicks > mtime.Ticks then DateTime(dirTicks) else mtime
            match layoutCache2.TryGetValue(layoutsDir) with
            | true, (cachedMtime, cachedLayouts) when cachedMtime = mtime -> cachedLayouts
            | _ ->
                let result =
                    files
                    |> Seq.map (fun (key, f) -> key, (f, File.ReadAllText(f)))
                    |> Map.ofSeq
                layoutCache2.[layoutsDir] <- struct(mtime, result)
                result

    let private includesCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, string>)>()
    // Highest mtime seen across all loadIncludes calls — used by BuildEngine
    // to invalidate the include-processed layout cache without re-traversing.
    let private lastIncludesMtimeRef = ref DateTime.MinValue
    let internal getLastIncludesMtime () = !lastIncludesMtimeRef
    let internal loadIncludes (includesDir: string) : IDictionary<string, string> =
        if not (Directory.Exists includesDir) then
            Dictionary<string, string>() :> IDictionary<string, string>
        else
            let mutable maxTicks = 0L
            let files = ResizeArray<string * string>()
            for f in Directory.EnumerateFiles(includesDir, "*.*", SearchOption.AllDirectories) do
                let ticks = File.GetLastWriteTimeUtc(f).Ticks
                if ticks > maxTicks then maxTicks <- ticks
                files.Add(Path.GetFileName(f), f)
            let mtime = if maxTicks > 0L then DateTime(maxTicks) else DateTime.MinValue
            let dirTicks = Directory.GetLastWriteTimeUtc(includesDir).Ticks
            let mtime = if dirTicks > mtime.Ticks then DateTime(dirTicks) else mtime
            lastIncludesMtimeRef := Operators.max !lastIncludesMtimeRef mtime
            match includesCache.TryGetValue(includesDir) with
            | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
            | _ ->
                let d = Dictionary<string, string>()
                for (name, f) in files do
                    d.[name] <- File.ReadAllText(f)
                let result = d :> IDictionary<string, string>
                includesCache.[includesDir] <- struct(mtime, result)
                result

    let internal buildReplacements (page: ContentPage) (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        let d = Dictionary<string, string>()
        d.["page.title"] <- page.Title
        d.["page.url"]   <- page.Url
        d.["page.slug"]  <- page.Slug
        if page.Date.IsSome then d.["page.date"] <- page.Date.Value.ToString("yyyy-MM-dd")
        if not page.Tags.IsEmpty then d.["page.tags"] <- String.Join(", ", page.Tags)
        d.["page.description"] <-
            match page.Data.TryGetValue("description") with
            | true, v when v <> null -> v.ToString()
            | _ -> config.Description
        d.["site.title"]       <- config.Title
        d.["site.description"] <- config.Description
        d.["site.base_url"]    <- config.BaseUrl
        d.["site.version"]     <- config.SiteVersion
        d.["site.author"]      <- config.Author
        d.["site.language"]    <- config.Language
        for kv in globalData do
            let key = "site." + kv.Key
            if not (d.ContainsKey key) then d.[key] <- kv.Value.ToString()
        for kv in page.Data do
            let key = if kv.Key.Contains "." then kv.Key else "page." + kv.Key
            if not (d.ContainsKey key) then d.[key] <- kv.Value.ToString()
        d :> IDictionary<string, string>

    // ── Static compiled Regex ──────────────────────────────────────────
    // Include names may contain hyphens (e.g. `page-shell.html`); without the
    // `-` class the tag falls through to Nunjucks and is misread as an
    // arithmetic expression (`include - page - shell`), rendering as 0.
    let private includePattern =
        Regex(@"\{\{\s*include\s+([\w\.\-]+)\s*\}\}", RegexOptions.Compiled)

    let private placeholderPattern =
        Regex(@"\{\{\s*([\w\.]+)\s*\}\}", RegexOptions.Compiled)

    let private nestedLayoutInfoPattern =
        Regex(@"^<!--\s*@layout\s+(.+?)\s*-->", RegexOptions.Compiled ||| RegexOptions.Multiline)

    let private processIncludes (text: string) (includes: IDictionary<string, string>) =
        let rec processText (t: string) (depth: int) =
            if depth > 10 then t
            else
                includePattern.Replace(t, fun (m: Match) ->
                    let name = m.Groups.[1].Value
                    match includes.TryGetValue(name) with
                    | true, content -> processText content (depth + 1)
                    | _ -> m.Value)
        processText text 0

    let private processedLayoutCache = ConcurrentDictionary<string, string>()
    let private includesMtimeRef = ref DateTime.MinValue
    let internal setIncludesMtime (t: DateTime) =
        if t > !includesMtimeRef then
            processedLayoutCache.Clear()
        includesMtimeRef := t
    let internal currentIncludesMtime () = !includesMtimeRef

    let private applyLayoutCached (path: string) (layoutText: string) (includes: IDictionary<string, string>) =
        let key = path + "|" + (currentIncludesMtime ()).Ticks.ToString()
        match processedLayoutCache.TryGetValue(key) with
        | true, cached -> cached
        | _ ->
            let processed = processIncludes layoutText includes
            processedLayoutCache.[key] <- processed
            processed

    // Thread-safe registry of engines that already had filters registered —
    // pages render in parallel, so a plain HashSet would race on first Add.
    let private registeredLayoutEngines = ConcurrentDictionary<string, bool>()

    // ── Layout chain ─────────────────────────────────────────────────────
    // A layout may nest another via TOML front matter, an HTML comment
    // (`<!-- @layout name -->`) or an F# comment (`// @layout name`). The
    // chain is resolved statically so the pipeline can apply every level in
    // order and batch FSI evaluations across pages per level.

    let private nestedLayoutOf (layoutText: string) : string option =
        if layoutText.TrimStart().StartsWith("+++") then
            let endIdx = layoutText.IndexOf("+++", 3)
            if endIdx > 0 then
                let tomlBlock = layoutText.Substring(3, endIdx - 3)
                try
                    let table = Tomlyn.Toml.ToModel(tomlBlock)
                    if table <> null && fst (table.TryGetValue("layout")) then
                        Some (table.["layout"].ToString())
                    else None
                with _ -> None
            else None
        else
            let m = nestedLayoutInfoPattern.Match(layoutText)
            if m.Success then Some (m.Groups.[1].Value.Trim())
            else
                // F#-style layout front matter: `// @layout name`
                let fm = Regex.Match(layoutText, @"//\s*@layout\s+(\S+)")
                if fm.Success then Some (fm.Groups.[1].Value.Trim()) else None

    /// Resolve the full layout chain (top first) for a layout name.
    /// Returns (name, path, isFsx) per level. Depth is capped so layouts that
    /// nest each other cyclically cannot cause unbounded recursion.
    let internal layoutChain (name: string) (layouts: Map<string, string * string>)
                             : (string * string * bool) list =
        let rec walk (current: string) (depth: int) (acc: (string * string * bool) list) =
            if depth >= 10 then acc
            else
                match layouts.TryFind current with
                | None -> acc
                | Some (path, text) ->
                    let isFsx =
                        path.EndsWith(FileExtensions.ZestScript, StringComparison.OrdinalIgnoreCase)
                        || path.EndsWith(FileExtensions.FSharpScript, StringComparison.OrdinalIgnoreCase)
                    let entry = (current, path, isFsx)
                    match nestedLayoutOf text with
                    | Some nested when nested <> current ->
                        walk nested (depth + 1) (entry :: acc)
                    | _ -> entry :: acc
        walk name 0 [] |> List.rev

    /// Render one non-F# layout level: `.hbs`/`.mustache` layouts run on the
    /// standalone Hbs engine (native Mustache/Handlebars semantics); everything
    /// else runs on the Nunjucks compat layer with legacy placeholder support.
    let private renderNonFsx (name: string) (path: string) (layoutText: string)
                             (content: string) (replacements: IDictionary<string, string>)
                             (includes: IDictionary<string, string>)
                             (page: ContentPage) (config: SiteConfig)
                             (globalData: IDictionary<string, obj>) : string =
        let isHbs =
            path.EndsWith(FileExtensions.Handlebars, StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(FileExtensions.Mustache, StringComparison.OrdinalIgnoreCase)
        let engineName = if isHbs then "hbs" else "nunjucks"
        let engine = TemplateManager.getOrCreateEngine engineName {
            Engine = engineName
            EnableCache = true
            Extension = if isHbs then FileExtensions.Handlebars else FileExtensions.Nunjucks
            Filters = []
        }
        match engine with
        | Some e ->
            // Hbs layouts load `{{> name}}` partials from the includes
            // dictionary directly (no conversion step).
            if isHbs then
                match e with
                | :? HbsEngine as h ->
                    h.SetPartialLoader(fun name ->
                        match includes.TryGetValue name with
                        | true, src -> Some src
                        | _ -> None)
                | _ -> ()
            let engineKey = e.GetHashCode().ToString()
            if not isHbs && registeredLayoutEngines.TryAdd(engineKey, true) then
                FilterRegistry.registerAllFilters e

            let pairs = ResizeArray<string * obj>()
            for kv in replacements do pairs.Add(kv.Key, box kv.Value)
            pairs.Add("content", box content)
            pairs.Add("page.content", box content)
            pairs.Add("page.url", box (replacements.TryGetValue "page.url" |> function true,v -> box v | _ -> box ""))
            pairs.Add("page.date", box (replacements.TryGetValue "page.date" |> function true,v -> box v | _ -> box ""))

            // Pass tags as array directly — avoid join/split roundtrip
            match replacements.TryGetValue "page.tags" with
            | true, tagsStr when not (String.IsNullOrEmpty tagsStr) ->
                pairs.Add("page.tags", box (tagsStr.Split(',') |> Array.map (fun t -> t.Trim())))
            | _ -> pairs.Add("page.tags", box [||])

            // Use HashSet for O(1) lookup when adding includes — avoid O(n*m) Seq.exists
            let addedKeys = HashSet<string>(pairs |> Seq.map fst)
            for kv in includes do
                if addedKeys.Add(kv.Key) then
                    pairs.Add(kv.Key, box kv.Value)

            // Replacements from buildReplacements flatten all globalData
            // values to strings via ToString(), which destroys nested
            // structure (arrays / dicts become "System.Object[]"). Re-add
            // them here as their native objects so Nunjucks can traverse
            // dotted keys and iterate arrays. Duplicate keys are harmless
            // because buildNestedContext overwrites with the last value.
            for kv in globalData do
                pairs.Add("site." + kv.Key, kv.Value)
            // Expose raw global data keys (e.g. pjaxScript) for
            // direct template access without site. prefix.
            for kv in globalData do
                pairs.Add(kv.Key, kv.Value)

            pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
            pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
            pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
            let ctx = TemplateManager.buildNestedContext pairs
            // Process legacy `{{ include name }}` partials BEFORE
            // handing the merged text to Nunjucks so that includes
            // work in native (Nunjucks) mode.
            let layoutText' = applyLayoutCached path layoutText includes
            // `.liquid` layouts run through the converter to Nunjucks
            // syntax first (assign/unless/case/filter args), matching
            // how ScriptEvaluator renders `.liquid` content pages.
            let renderedText =
                if path.EndsWith(FileExtensions.Liquid, StringComparison.OrdinalIgnoreCase) then
                    LiquidConverter.convert layoutText'
                else layoutText'
            match e.Render renderedText ctx with
            | Ok html -> html
            | Error err ->
                eprintfn "[Zest] Nunjucks error in layout '%s': %O" name err
                sprintf "<!-- Template error: %O -->" err
        | None ->
            let withIncludes = applyLayoutCached path layoutText includes
            let ctx = Dictionary<string, string>()
            for kv in replacements do ctx.[kv.Key] <- kv.Value
            ctx.["content"]      <- content
            ctx.["page.content"] <- content
            placeholderPattern.Replace(withIncludes, fun (m: Match) ->
                let key = m.Groups.[1].Value.ToLowerInvariant()
                match ctx.TryGetValue key with
                | true, v -> v
                | _ -> m.Value)

    let rec internal applyLayout (name: string) (content: string) (layouts: Map<string, string * string>)
                                (replacements: IDictionary<string, string>) (includes: IDictionary<string, string>)
                                (page: ContentPage) (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        match layoutChain name layouts with
        | [] -> content
        | chain ->
            // Layouts are routed purely by file extension:
            //   `.zest.fsx`/`.fsx` → F# layout, evaluated by FSI (`content`/`page`/`site`
            //                        are injected as top-level bindings, see ScriptRunner).
            //   everything else    → Nunjucks compat layer, so `{{ }}` / `{% %}` syntax
            //                        (incl. legacy `{{ page.title }}` placeholders) works.
            // The `template_engine` config field is a PURE ANNOTATION for the primary
            // template language (native → .zest.fsx, nunjucks → .njk, liquid → .liquid, ...)
            // and does not affect routing.
            let mutable current = content
            for (lname, lpath, isFsx) in chain do
                let (_, ltext) = layouts.[lname]
                if isFsx then
                    match ScriptRunner.evaluateLayoutScript ltext current page config globalData with
                    | Ok html -> current <- html
                    | Error e ->
                        eprintfn "[Zest] F# layout error in '%s': %s" lname e
                        current <- sprintf "<!-- F# layout error: %s -->" e
                else
                    current <- renderNonFsx lname lpath ltext current replacements includes page config globalData
            current

    /// Apply the full layout chain to many pages, batching F# layout
    /// evaluations per chain level into a single FSI run per level. Non-F#
    /// levels render in-process. Returns a map keyed by page.SourcePath
    /// holding each page's final HTML.
    let internal applyLayoutsBatched
        (tasks: (ContentPage * string) list)
        (layouts: Map<string, string * string>)
        (includes: IDictionary<string, string>)
        (config: SiteConfig)
        (globalData: IDictionary<string, obj>)
        : Map<string, string> =
        if tasks.IsEmpty then Map.empty
        else
            // Per-page chains (top first). Pages whose top layout is missing
            // get an empty chain and fall through to their raw content.
            let chains = tasks |> List.map (fun (page, top) -> page, layoutChain top layouts)
            let maxLevel = chains |> List.map (fun (_, c) -> List.length c) |> List.max
            let inputs = Dictionary<string, string>()
            for (page, _) in chains do inputs.[page.SourcePath] <- page.Content

            for level in 0 .. maxLevel - 1 do
                let levelTasks =
                    chains |> List.choose (fun (page, chain) ->
                        if level < List.length chain then
                            let (lname, lpath, isFsx) = List.item level chain
                            Some (page, lname, lpath, isFsx, inputs.[page.SourcePath])
                        else None)
                let fsxTasks = levelTasks |> List.filter (fun (_, _, _, isFsx, _) -> isFsx)
                let inlineTasks = levelTasks |> List.filter (fun (_, _, _, isFsx, _) -> not isFsx)

                if not (List.isEmpty fsxTasks) then
                    let scriptTasks =
                        fsxTasks |> List.map (fun (page, lname, _, _, content) ->
                            let (_, ltext) = layouts.[lname]
                            (page.SourcePath + "|" + lname, ltext, content, page, config, globalData))
                    let results = ScriptRunner.evaluateLayoutScriptsBatch scriptTasks
                    for (page, lname, _, _, _) in fsxTasks do
                        let key = page.SourcePath + "|" + lname
                        match results.TryFind key with
                        | Some (Ok html) -> inputs.[page.SourcePath] <- html
                        | Some (Error e) ->
                            eprintfn "[Zest] F# layout error in '%s': %s" lname e
                            inputs.[page.SourcePath] <- sprintf "<!-- F# layout error: %s -->" e
                        | None -> () // every task key is always returned

                for (page, lname, lpath, _, content) in inlineTasks do
                    let (_, ltext) = layouts.[lname]
                    let replacements = buildReplacements page config globalData
                    inputs.[page.SourcePath] <- renderNonFsx lname lpath ltext content replacements includes page config globalData

            inputs |> Seq.map (fun kv -> kv.Key, kv.Value) |> Map.ofSeq
