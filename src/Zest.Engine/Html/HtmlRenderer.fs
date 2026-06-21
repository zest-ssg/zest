namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// HTML Renderer
// ============================================================

module HtmlRenderer =

    let private voidTags = set ["area";"base";"br";"col";"embed";"hr";"img";"input";"link";"meta";"param";"source";"track";"wbr"]

    let rec renderNode (node: HtmlNode) : string =
        match node with
        | Text s -> WebUtility.HtmlEncode s
        | Raw  s -> s
        | Fragment ns     -> ns |> List.map renderNode |> String.concat ""
        | Conditional(true,  n) -> renderNode n
        | Conditional(false, _) -> ""
        | Repeat items         -> items |> List.map renderNode |> String.concat ""
        | Element(tag, attrs, ch) ->
            let attrStr =
                attrs
                |> List.map (fun (k, v) -> sprintf " %s=\"%s\"" k (WebUtility.HtmlEncode v))
                |> String.concat ""
            if voidTags.Contains tag then sprintf "<%s%s />" tag attrStr
            else
                let inner = ch |> List.map renderNode |> String.concat ""
                sprintf "<%s%s>%s</%s>" tag attrStr inner tag

    let render (nodes: HtmlNode list) : string =
        nodes |> List.map renderNode |> String.concat ""
