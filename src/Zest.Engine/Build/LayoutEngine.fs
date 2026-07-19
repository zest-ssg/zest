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

    /// Extensions routed through the Nunjucks compat layer in native mode.
    /// Excludes .haml / .pug (those convert-then-render via ScriptEvaluator).
    let private nunjucksRenderExts =
        [ FileExtensions.Nunjucks; FileExtensions.Liquid
          FileExtensions.Handlebars; FileExtensions.Mustache
          FileExtensions.Html; FileExtensions.HtmlLong
          FileExtensions.WebC ]

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
    let private includePattern =
        Regex(@"\{\{\s*include\s+([\w\.]+)\s*\}\}", RegexOptions.Compiled)

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

    let mutable private registeredLayoutEngines = HashSet<string>()

    let rec internal applyLayout (name: string) (content: string) (layouts: Map<string, string * string>)
                                (replacements: IDictionary<string, string>) (includes: IDictionary<string, string>)
                                (page: ContentPage) (config: SiteConfig) (globalData: IDictionary<string, obj>) =
        match layouts.TryFind name with
        | None -> content
        | Some (path, layoutText) ->
            // In native mode, HTML layouts are routed through the Nunjucks
            // compat layer so `{{ }}` / `{% %}` syntax works in plain HTML.
            // `.zest.fsx`/`.fsx` layouts are F# and stay on the placeholder
            // path (their HTML output is produced by the script evaluator).
            // Nunjucks also handles the legacy `{{ page.title }}` placeholder
            // syntax, so this is a strict compatibility improvement.
            let isFsx =
                path.EndsWith(FileExtensions.ZestScript, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(FileExtensions.FSharpScript, StringComparison.OrdinalIgnoreCase)
            let isNunjucks =
                nunjucksRenderExts
                |> List.exists (fun e -> path.EndsWith(e, StringComparison.OrdinalIgnoreCase))

            let rendered =
                if isFsx then
                    // F# layouts are evaluated by FSI; `content`/`page`/`site`
                    // are injected as top-level bindings (see ScriptRunner).
                    match ScriptRunner.evaluateLayoutScript layoutText content page config globalData with
                    | Ok html -> html
                    | Error e ->
                        eprintfn "[Zest] F# layout error in '%s': %s" name e
                        sprintf "<!-- F# layout error: %s -->" e
                elif isNunjucks then
                    let engine = TemplateManager.getOrCreateEngine "nunjucks" {
                        Engine = "nunjucks"
                        EnableCache = true
                        Extension = FileExtensions.Nunjucks
                        Filters = []
                    }
                    match engine with
                    | Some e ->
                        let engineKey = e.GetHashCode().ToString()
                        if registeredLayoutEngines.Add(engineKey) then
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

                        pairs.Add("pages", box (PageQuery.getPagesForNunjucks () |> Array.map box))
                        pairs.Add("tags", box (PageQuery.getTagsForNunjucks ()))
                        pairs.Add("collections", box (PageQuery.getCollectionsForNunjucks ()))
                        let ctx = TemplateManager.buildNestedContext pairs
                        // Process legacy `{{ include name }}` partials BEFORE
                        // handing the merged text to Nunjucks so that includes
                        // work in native (Nunjucks) mode.
                        let layoutText' = applyLayoutCached path layoutText includes
                        match e.Render layoutText' ctx with
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
                else
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

            // Check for nested layout via TOML front matter or HTML comment
            let nestedLayout =
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
            match nestedLayout with
            | Some nl when nl <> name -> applyLayout nl rendered layouts replacements includes page config globalData
            | _ -> rendered
