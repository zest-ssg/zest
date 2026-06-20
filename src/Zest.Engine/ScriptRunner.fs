namespace Zest.Engine.Scripting

open System
open System.Collections.Generic
open System.IO
open System.Diagnostics
open System.Text.Json
open Zest.Engine

/// Evaluates .zest.fsx scripts by spawning `dotnet fsi` as a subprocess.
/// Context data (pages, site config) is passed via a temp JSON file.
/// The script preamble injects DSL helpers + collections API via #load.
module ScriptRunner =

    // ── Global state ──────────────────────────────────────────────────────

    let private globalDataRef : IDictionary<string, obj> ref =
        ref (dict [] :> IDictionary<string, obj>)

    let setGlobalData (data: IDictionary<string, obj>) =
        System.Threading.Interlocked.Exchange(globalDataRef, data) |> ignore

    let getDataString (key: string) : string =
        let mutable v : obj = null
        if (!globalDataRef).TryGetValue(key, &v) then (if isNull v then "" else v.ToString())
        else ""

    let getDataSection (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in !globalDataRef do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    let mutable private allPagesRef : Page list = []
    let mutable private includesRef : IDictionary<string, string> = dict []

    let setAllPages (pages: Page list) = allPagesRef <- pages
    let setIncludes (includes: IDictionary<string, string>) = includesRef <- includes

    let getPages () = allPagesRef
    let getPagesByTag (tag: string) =
        allPagesRef |> List.filter (fun p -> p.Tags |> List.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
    let getPagesByDir (dirName: string) =
        allPagesRef |> List.filter (fun p ->
            p.SourcePath.Contains(Path.DirectorySeparatorChar.ToString() + dirName + Path.DirectorySeparatorChar.ToString())
            || p.SourcePath.Contains("/" + dirName + "/"))
    let getRecentPages (n: int) =
        allPagesRef |> List.filter (fun p -> p.Date.IsSome) |> List.sortByDescending (fun p -> p.Date.Value) |> List.truncate n
    let includePartial (name: string) =
        match includesRef.TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name

    // ── Context serialisation ─────────────────────────────────────────────

    /// Serialise all context to a JSON file that the subprocess preamble reads.
    let private writeContextFile (path: string) =
        let pageToObj (p: Page) =
            let date = p.Date |> Option.map (fun d -> d.ToString("yyyy-MM-dd")) |> Option.defaultValue ""
            let desc = match p.Data.TryGetValue("description") with true, v -> v.ToString() | _ -> ""
            {| url=p.Url; title=p.Title; date=date; slug=p.Slug; description=desc; tags=p.Tags |}
        let siteData =
            !globalDataRef
            |> Seq.map (fun kv -> kv.Key, kv.Value |> Option.ofObj |> Option.map (fun v -> v.ToString()) |> Option.defaultValue "")
            |> dict
        let payload = {|
            pages   = allPagesRef |> List.map pageToObj
            includes = includesRef |> Seq.map (fun kv -> kv.Key, kv.Value) |> dict
            siteData = siteData
        |}
        File.WriteAllText(path, JsonSerializer.Serialize(payload))

    // ── DSL preamble ──────────────────────────────────────────────────────

    /// Preamble injected at the top of every script before evaluation.
    /// Reads context JSON and exposes DSL helpers + collections API.
    let private buildPreamble (ctxFile: string) =
        let preamble = """
open System
open System.IO
open System.Text.Json

let private _ctx =
    let json = File.ReadAllText(@"ZEST_CTX_FILE")
    JsonDocument.Parse(json).RootElement

let private _pages =
    _ctx.GetProperty("pages").EnumerateArray()
    |> Seq.map (fun e ->
        let tags =
            e.GetProperty("tags").EnumerateArray()
            |> Seq.map (fun t -> t.GetString())
            |> Seq.toArray
        struct {| url=e.GetProperty("url").GetString()
                  title=e.GetProperty("title").GetString()
                  date=e.GetProperty("date").GetString()
                  slug=e.GetProperty("slug").GetString()
                  description=e.GetProperty("description").GetString()
                  tags=tags |})
    |> Seq.toArray

let private _includes =
    _ctx.GetProperty("includes").EnumerateObject()
    |> Seq.map (fun m -> m.Name, m.Value.GetString())
    |> dict

let private _siteData =
    _ctx.GetProperty("siteData").EnumerateObject()
    |> Seq.map (fun m -> m.Name, m.Value.GetString())
    |> dict

// DSL helpers
let htmlEncode (s: string) =
    s.Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;")
let text s = htmlEncode s
let raw  s = s
let attr k v = sprintf "%s=\"%s\"" k (htmlEncode v)
let elem tag (attrs: string list) (children: string list) =
    let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
    sprintf "<%s%s>%s</%s>" tag a (String.concat "" children) tag
let voidElem tag (attrs: string list) =
    let a = if attrs.IsEmpty then "" else " " + String.concat " " attrs
    sprintf "<%s%s />" tag a
let a url (ch: string list) = elem "a" [attr "href" url] ch
let span (ch: string list) = elem "span" [] ch
let code (ch: string list) = elem "code" [] ch
let strong (ch: string list) = elem "strong" [] ch
let em (ch: string list) = elem "em" [] ch
let img src alt = voidElem "img" [attr "src" src; attr "alt" alt]
let br () = voidElem "br" []
let hr () = voidElem "hr" []
let h1 ch = elem "h1" [] ch
let h2 ch = elem "h2" [] ch
let h3 ch = elem "h3" [] ch
let h4 ch = elem "h4" [] ch
let h5 ch = elem "h5" [] ch
let h6 ch = elem "h6" [] ch
let p ch = elem "p" [] ch
let div ch = elem "div" [] ch
let section ch = elem "section" [] ch
let article ch = elem "article" [] ch
let nav ch = elem "nav" [] ch
let header ch = elem "header" [] ch
let footer ch = elem "footer" [] ch
let main ch = elem "main" [] ch
let ul ch = elem "ul" [] ch
let ol ch = elem "ol" [] ch
let li ch = elem "li" [] ch
let blockquote ch = elem "blockquote" [] ch
let pre ch = elem "pre" [] ch
let table ch = elem "table" [] ch
let thead ch = elem "thead" [] ch
let tbody ch = elem "tbody" [] ch
let tr ch = elem "tr" [] ch
let th ch = elem "th" [] ch
let td ch = elem "td" [] ch
let divC cls ch = elem "div" [attr "class" cls] ch
let pC cls ch = elem "p" [attr "class" cls] ch
let spanC cls ch = elem "span" [attr "class" cls] ch
let sectionC cls ch = elem "section" [attr "class" cls] ch
let codeBlock lang c = elem "pre" [] [elem "code" [attr "class" ("lang-"+lang)] [c]]
let showIf cond ch = if cond then ch else ""
let hideIf cond ch = if cond then "" else ch
let render (nodes: string list) = printf "%s" (String.concat "\n" nodes)

// Collections API
let site_pages () = _pages
let recent_pages n = _pages |> Array.filter (fun r -> r.date <> "") |> Array.sortByDescending (fun r -> r.date) |> Array.truncate n
let pages_by_tag tag = _pages |> Array.filter (fun r -> r.tags |> Array.exists (fun t -> t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
let pages_by_dir dir = _pages |> Array.filter (fun r -> r.url.Contains("/" + dir + "/"))
let include_partial name = match _includes.TryGetValue(name) with true, c -> c | _ -> sprintf "<!-- include '%s' not found -->" name
let site_data key = match _siteData.TryGetValue(key) with true, v -> v | _ -> ""
"""
        let tmpl = preamble
        tmpl.Replace("ZEST_CTX_FILE", ctxFile)

    // ── Subprocess evaluation ─────────────────────────────────────────────

    let private runFsi (scriptPath: string) : Result<string, string> =
        let psi = ProcessStartInfo("dotnet", sprintf "fsi \"%s\"" scriptPath)
        psi.UseShellExecute        <- false
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError  <- true
        psi.CreateNoWindow         <- true
        use proc = Process.Start(psi)
        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        if proc.ExitCode = 0 then Ok stdout
        else
            // Only output the first few error lines to avoid noise
            let errLines = stderr.Split('\n') |> Array.filter (fun l -> l.Contains("error FS") || l.Contains("error:")) |> Array.truncate 5
            Error(String.concat "\n" errLines)

    // ── Context file path (per-build, shared) ─────────────────────────────

    let mutable private ctxFilePath = ""

    let resetSession () =
        ctxFilePath <- Path.Combine(Path.GetTempPath(), sprintf "zest-ctx-%s.json" (Guid.NewGuid().ToString("N")))
        writeContextFile ctxFilePath

    // ── Public API ────────────────────────────────────────────────────────

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
                || l.StartsWith("let ") || l.StartsWith("open "))
            |> Option.defaultValue false

    let evaluatePageScript (scriptText: string) : Result<string, string> =
        try
            // Ensure context file exists (resetSession may not have been called)
            if String.IsNullOrEmpty ctxFilePath || not (File.Exists ctxFilePath) then
                resetSession ()

            // Strip metadata comments from script
            let stripped =
                scriptText.Split('\n')
                |> Array.filter (fun l ->
                    let t = l.TrimStart()
                    not (t.StartsWith("// @")))
                |> String.concat "\n"

            let tmpFsx = Path.Combine(Path.GetTempPath(), sprintf "zest-page-%s.fsx" (Guid.NewGuid().ToString("N")))
            try
                File.WriteAllText(tmpFsx, (buildPreamble ctxFilePath) + "\n" + stripped)
                match runFsi tmpFsx with
                | Ok html -> Ok html
                | Error msg -> Error(sprintf "FSI evaluation reported errors — %s" msg)
            finally
                if File.Exists tmpFsx then File.Delete tmpFsx
        with ex ->
            Error(sprintf "ScriptRunner threw: %s" ex.Message)
