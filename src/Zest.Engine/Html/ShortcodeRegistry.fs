namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// 11ty.js-style Shortcodes
// ============================================================

/// Shortcode registry: allows users to define reusable template fragments.
type ShortcodeFunc = IDictionary<string, obj> -> string -> string

module ShortcodeRegistry =

    let private store = Dictionary<string, ShortcodeFunc>()

    /// Register a shortcode.
    let add (name: string) (fn: ShortcodeFunc) : unit =
        store.[name] <- fn

    /// Register a simple shortcode (returns a string only, no context).
    let addSimple (name: string) (fn: string -> string) : unit =
        store.[name] <- fun _ ctx -> fn ctx

    /// Execute a registered shortcode.
    let execute (name: string) (ctx: IDictionary<string, obj>) (arg: string) : string option =
        match store.TryGetValue name with
        | true, fn -> Some(fn ctx arg)
        | _       -> None

    /// Built-in shortcode: render inline Markdown as HTML.
    let private builtinMd (ctx: IDictionary<string, obj>) (arg: string) =
        Markdown.toHtml arg

    /// Built-in shortcode: get a global data value.
    let private builtinData (ctx: IDictionary<string, obj>) (key: string) =
        match ctx.TryGetValue key with
        | true, v -> string v
        | _       -> ""

    do
        store.["md"]   <- builtinMd
        store.["data"]  <- builtinData
        store.["date"]  <- fun _ _ -> DateTime.Now.ToString("yyyy-MM-dd")
        store.["year"]  <- fun _ _ -> DateTime.Now.Year.ToString()

/// Perform shortcode replacement in template context ({{ key param }} or {% key param %}).
module ShortcodeRenderer =
    let inlineShortcodes (ctx: IDictionary<string, obj>) (text: string) =
        let pattern = Regex(@"\{\{\s*(\w+)\s*(.*?)\s*\}\}", RegexOptions.Compiled)
        pattern.Replace(text, MatchEvaluator(fun m ->
            let name = m.Groups.[1].Value
            let arg  = m.Groups.[2].Value
            match ShortcodeRegistry.execute name ctx arg with
            | Some v -> v
            | None   -> m.Value))
