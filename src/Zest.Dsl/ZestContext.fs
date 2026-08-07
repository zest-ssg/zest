// ZestContext.fs
//
// Exposes the build context (pages, includes, site data) to FSI scripts via a
// JSON file written by ScriptRunner. SiteData is converted from JsonElement to
// native CLR values so scripts can use ordinary string/bool/number operations.
//
// Dependencies: System.Text.Json

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

    /// Convert a JsonElement from the context file to a native CLR value so F#
    /// scripts can compare / index / format it directly (e.g. `data.[k] <> ""`,
    /// `url.TrimEnd('/')`). Previously these leaked through as JsonElement,
    /// which broke scripts that treated site data as plain strings (FS0001 /
    /// missing members). Objects and arrays are converted recursively.
    static member private jsonToNative (e: JsonElement) : obj =
        match e.ValueKind with
        | JsonValueKind.String ->
            e.GetString() :> obj
        | JsonValueKind.Number ->
            let mutable i = 0L
            let mutable d = 0.0
            if e.TryGetInt64(&i) then box i
            elif e.TryGetDouble(&d) then box d
            else box (e.GetRawText())
        | JsonValueKind.True -> box true
        | JsonValueKind.False -> box false
        | JsonValueKind.Null -> null
        | JsonValueKind.Array ->
            e.EnumerateArray()
            |> Seq.map ZestContext.jsonToNative
            |> Seq.toArray
            :> obj
        | JsonValueKind.Object ->
            e.EnumerateObject()
            |> Seq.map (fun p -> p.Name, ZestContext.jsonToNative p.Value)
            |> dict
            :> obj
        | _ ->
            box (e.GetRawText())

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
        |> Seq.map (fun m -> m.Name, ZestContext.jsonToNative m.Value)
        |> dict

/// Global context instance — set by ScriptRunner before evaluation
module Context =
    let mutable current: ZestContext option = None

    let get () =
        match current with
        | Some c -> c
        | None -> failwith "ZestContext not initialized. Call Context.set first."
