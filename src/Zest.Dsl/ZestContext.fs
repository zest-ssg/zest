namespace Zest.Dsl

open System
open System.IO
open System.Text.Json

// ============================================================
// Zest DSL — Pre-compiled helpers for FSI script evaluation
// ============================================================
// This module is compiled to a DLL and referenced via #r in
// FSI scripts. This avoids recompiling ~250 lines of helper
// code on every script evaluation, dramatically improving
// build performance.
// ============================================================

/// Context data passed from the build engine to FSI scripts via a JSON file.
type ZestContext(ctxFile: string) =
    let json = File.ReadAllText(ctxFile)
    let doc = JsonDocument.Parse(json)
    let root = doc.RootElement

    member _.Pages =
        root.GetProperty("pages").EnumerateArray()
        |> Seq.map (fun e ->
            let tags =
                e.GetProperty("tags").EnumerateArray()
                |> Seq.map (fun t -> t.GetString())
                |> Seq.toArray
            {| url=e.GetProperty("url").GetString()
               title=e.GetProperty("title").GetString()
               date=e.GetProperty("date").GetString()
               slug=e.GetProperty("slug").GetString()
               description=e.GetProperty("description").GetString()
               tags=tags |})
        |> Seq.toArray

    member _.Includes =
        root.GetProperty("includes").EnumerateObject()
        |> Seq.map (fun m -> m.Name, m.Value.GetString())
        |> dict

    member _.SiteData =
        root.GetProperty("siteData").EnumerateObject()
        |> Seq.map (fun m -> m.Name, m.Value.GetString())
        |> dict

/// Global context instance — set by ScriptRunner before evaluation
module Context =
    let mutable current: ZestContext option = None

    let get () =
        match current with
        | Some c -> c
        | None -> failwith "ZestContext not initialized. Call Context.set first."
