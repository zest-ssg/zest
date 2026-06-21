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

/// 短代码注册表：允许用户定义可复用的模板片段。
type ShortcodeFunc = IDictionary<string, obj> -> string -> string

module ShortcodeRegistry =

    let private store = Dictionary<string, ShortcodeFunc>()

    /// 注册一个短代码。
    let add (name: string) (fn: ShortcodeFunc) : unit =
        store.[name] <- fn

    /// 注册一个简单短代码（仅返回字符串，无上下文）。
    let addSimple (name: string) (fn: string -> string) : unit =
        store.[name] <- fun _ ctx -> fn ctx

    /// 执行短代码（如果已注册）。
    let execute (name: string) (ctx: IDictionary<string, obj>) (arg: string) : string option =
        match store.TryGetValue name with
        | true, fn -> Some(fn ctx arg)
        | _       -> None

    /// 内置短代码：将内联 Markdown 渲染为 HTML。
    let private builtinMd (ctx: IDictionary<string, obj>) (arg: string) =
        Markdown.toHtml arg

    /// 内置短代码：获取全局数据值。
    let private builtinData (ctx: IDictionary<string, obj>) (key: string) =
        match ctx.TryGetValue key with
        | true, v -> string v
        | _       -> ""

    do
        store.["md"]   <- builtinMd
        store.["data"]  <- builtinData
        store.["date"]  <- fun _ _ -> DateTime.Now.ToString("yyyy-MM-dd")
        store.["year"]  <- fun _ _ -> DateTime.Now.Year.ToString()

/// 在模板上下文中执行短代码替换（{{ key param }} 或 {% key param %}）。
module ShortcodeRenderer =
    let inlineShortcodes (ctx: IDictionary<string, obj>) (text: string) =
        let pattern = Regex(@"\{\{\s*(\w+)\s*(.*?)\s*\}\}", RegexOptions.Compiled)
        pattern.Replace(text, MatchEvaluator(fun m ->
            let name = m.Groups.[1].Value
            let arg  = m.Groups.[2].Value
            match ShortcodeRegistry.execute name ctx arg with
            | Some v -> v
            | None   -> m.Value))
