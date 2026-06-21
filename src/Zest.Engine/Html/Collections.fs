namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// Collection 分组与页面列表辅助
// ============================================================

module Collections =

    /// 按标签分组页面。
    let groupByTags (pages: Page list) : (string * Page list) list =
        pages
        |> List.collect (fun p -> p.Tags |> List.map (fun t -> t, p))
        |> List.groupBy fst
        |> List.map (fun (tag, items) -> tag, items |> List.map snd)

    /// 按集合名称分组页面（基于 detectCollection 相似逻辑）。
    let groupByCollection (pages: Page list) : (string * Page list) list =
        pages
        |> List.groupBy (fun p ->
            let parts = p.Url.Trim('/').Split('/')
            if parts.Length > 0 && parts.[0] <> "" then parts.[0] else "root")
        |> List.sortBy fst

    /// 渲染页面列表为 <ul>。
    let renderPageList (pages: Page list) : HtmlNode =
        ul [
            for p in pages do
                li [ aHref p.Url p.Title ]
        ]
