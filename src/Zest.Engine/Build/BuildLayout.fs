namespace Zest.Engine

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Zest.Engine.Scripting
open Zest.Engine.Template

/// Layout loading, include processing, placeholder replacement, and recursive layout application.
module BuildLayout =

    let private layoutCache2 = ConcurrentDictionary<string, struct(DateTime * Map<string, string * string>)>()
    let internal loadLayouts (layoutsDir: string) =
        if not (Directory.Exists layoutsDir) then Map.empty
        else
            let mtime =
                Directory.EnumerateFiles(layoutsDir, "*.*", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(layoutsDir).Ticks]
                |> Seq.max |> DateTime
            match layoutCache2.TryGetValue(layoutsDir) with
            | true, (cachedMtime, cachedLayouts) when cachedMtime = mtime -> cachedLayouts
            | _ ->
                let result =
                    Directory.GetFiles(layoutsDir, "*.*", SearchOption.AllDirectories)
                    |> Array.filter (fun f ->
                        let ext = Path.GetExtension(f).ToLowerInvariant()
                        List.contains ext [".html"; ".htm"; ".znjk"; ".zpage.fsx"; ".fsx"])
                    |> Array.map (fun f ->
                        let rec stripExts (name: string) =
                            let e = Path.GetExtension(name)
                            if String.IsNullOrEmpty e then name
                            else stripExts (Path.GetFileNameWithoutExtension(name))
                        let key = stripExts (Path.GetFileName(f))
                        key, (f, File.ReadAllText(f)))
                    |> Map.ofArray
                layoutCache2.[layoutsDir] <- struct(mtime, result)
                result

    let private includesCache = ConcurrentDictionary<string, struct(DateTime * IDictionary<string, string>)>()
    let internal loadIncludes (includesDir: string) : IDictionary<string, string> =
        let mtime =
            if not (Directory.Exists includesDir) then DateTime.MinValue
            else
                Directory.EnumerateFiles(includesDir, "*.*", SearchOption.AllDirectories)
                |> Seq.map (fun f -> File.GetLastWriteTimeUtc(f).Ticks)
                |> Seq.append [Directory.GetLastWriteTimeUtc(includesDir).Ticks]
                |> Seq.max |> DateTime
        match includesCache.TryGetValue(includesDir) with
        | true, (cachedMtime, cachedData) when cachedMtime = mtime -> cachedData
        | _ ->
            let d = Dictionary<string, string>()
            if Directory.Exists includesDir then
                for f in Directory.GetFiles(includesDir, "*.*", SearchOption.AllDirectories) do
                    d.[Path.GetFileName(f)] <- File.ReadAllText(f)
            let result = d :> IDictionary<string, string>
            includesCache.[includesDir] <- struct(mtime, result)
            result

    let internal buildReplacements (page: Page) (config: SiteConfig) (globalData: IDictionary<string, obj>) =
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

    // Cached compiled regexes
    let private includePattern =
        Regex(@"\{\{\s*include\s+([\w\.]+)\s*\}\}", RegexOptions.Compiled)
    let private placeholderPattern =
        Regex(@"\{\{\s*([\w\.]+)\s*\}\}", RegexOptions.Compiled)

    let private layoutCache = ConcurrentDictionary<string, struct(DateTime * string)>()
    let private getLayoutCached (path: string) (rawText: string) : string =
        let mtime = File.GetLastWriteTimeUtc(path)
        match layoutCache.TryGetValue(path) with
        | true, (cachedMtime, cachedText) when cachedMtime = mtime -> cachedText
        | _ ->
            layoutCache.[path] <- struct(mtime, rawText)
            rawText

    let private replacePlaceholders (text: string) (replacements: IDictionary<string, string>) =
        placeholderPattern.Replace(text, fun (m: Match) ->
            let key = m.Groups.[1].Value.ToLowerInvariant()
            match replacements.TryGetValue key with
            | true, v -> v
            | _ -> m.Value)

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

    let rec internal applyLayout (name: string) (content: string) (layouts: Map<string, string * string>)
                                (replacements: IDictionary<string, string>) (includes: IDictionary<string, string>) =
        match layouts.TryFind name with
        | None -> content
        | Some (path, layoutText) ->
            let isNunjucks = path.EndsWith(".znjk", StringComparison.OrdinalIgnoreCase)

            let rendered =
                if isNunjucks then
                    let engine = TemplateManager.getOrCreateEngine "znjk" {
                        Engine = "znjk"
                        EnableCache = true
                        Extension = ".znjk"
                        Filters = []
                    }
                    match engine with
                    | Some e ->
                        e.RegisterFilter "pages_by_tag" (fun value args ->
                            let tag = if args.Length > 0 then args.[0] else ""
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "tags" with
                                | true, (:? (string[]) as tags) -> tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase))
                                | _ -> false)
                            |> Array.map (fun d -> d :> obj) |> box)
                        e.RegisterFilter "recent" (fun value args ->
                            let n = if args.Length > 0 then (try int args.[0] with _ -> 5) else 5
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "date" with
                                | true, (:? string as d) -> d <> ""
                                | _ -> false)
                            |> Array.sortByDescending (fun p ->
                                match p.TryGetValue "date" with
                                | true, (:? string as d) -> d
                                | _ -> "")
                            |> Array.truncate n
                            |> Array.map (fun d -> d :> obj) |> box)
                        e.RegisterFilter "by_collection" (fun value args ->
                            let col = if args.Length > 0 then args.[0] else ""
                            ScriptRunner.getPagesForNunjucks ()
                            |> Array.filter (fun p ->
                                match p.TryGetValue "url" with
                                | true, (:? string as u) ->
                                    let parts = u.Trim('/').Split('/')
                                    parts.Length > 0 && parts.[0].Equals(col, StringComparison.OrdinalIgnoreCase)
                                | _ -> false)
                            |> Array.map (fun d -> d :> obj) |> box)

                        let pairs = ResizeArray<string * obj>()
                        for kv in replacements do pairs.Add(kv.Key, box kv.Value)
                        pairs.Add("content", box content)
                        pairs.Add("page.content", box content)
                        pairs.Add("page.url", box (replacements.TryGetValue "page.url" |> function true,v -> box v | _ -> box ""))
                        pairs.Add("page.date", box (replacements.TryGetValue "page.date" |> function true,v -> box v | _ -> box ""))
                        pairs.Add("page.tags", box (replacements.TryGetValue "page.tags" |> function true,v -> box (v.Split(',') |> Array.map (fun t -> t.Trim())) | _ -> box [||]))
                        for kv in includes do
                            if not (pairs |> Seq.exists (fun (k, _) -> k = kv.Key)) then
                                pairs.Add(kv.Key, box kv.Value)
                        pairs.Add("pages", box (ScriptRunner.getPagesForNunjucks () |> Array.map box))
                        pairs.Add("tags", box (ScriptRunner.getTagsForNunjucks ()))
                        pairs.Add("collections", box (ScriptRunner.getCollectionsForNunjucks ()))
                        let ctx = TemplateManager.buildNestedContext pairs
                        match e.Render layoutText ctx with
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
                        replacePlaceholders withIncludes ctx
                else
                    let withIncludes = applyLayoutCached path layoutText includes
                    let ctx = Dictionary<string, string>()
                    for kv in replacements do ctx.[kv.Key] <- kv.Value
                    ctx.["content"]      <- content
                    ctx.["page.content"] <- content
                    replacePlaceholders withIncludes ctx

            let nestedLayout =
                // Try TOML front matter (+++) first
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
                    // Then try HTML comment metadata
                    let m = Regex.Match(layoutText, @"^<!--\s*@layout\s+(.+?)\s*-->", RegexOptions.Multiline)
                    if m.Success then Some (m.Groups.[1].Value.Trim())
                    else None
            match nestedLayout with
            | Some nl when nl <> name -> applyLayout nl rendered layouts replacements includes
            | _ -> rendered
