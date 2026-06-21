namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// 模板数据上下文辅助
// ============================================================

module TemplateData =

    /// 从站点全局数据中安全地获取值。
    let siteData (data: IDictionary<string, obj>) (key: string) : string =
        match data.TryGetValue key with
        | true, (:? string as s) -> s
        | true, v -> string v
        | _       -> ""

    /// 从站点全局数据中获取子对象字典。
    let siteSection (data: IDictionary<string, obj>) (prefix: string) : IDictionary<string, obj> =
        let d = Dictionary<string, obj>()
        for kv in data do
            if kv.Key.StartsWith(prefix + ".") then
                d.[kv.Key.Substring(prefix.Length + 1)] <- kv.Value
        d :> _

    /// 获取当前页面数据。
    let pageData (page: Page) (key: string) : string =
        match page.Data.TryGetValue key with
        | true, v -> string v
        | _       -> ""
