// _data/settings.zest.fsx — Zest 数据配置文件
// 此文件由 _init.zest.fsx 在构建时自动加载。
// 输出 JSON 到 stdout，由 exec() 捕获后注入全局数据。
//
// 命名约定：
//   _init.zest.fsx    — 构建时初始化（自动执行）
//   _data/*.zest.fsx — 数据定义（由 _init 加载）
//   *.zpage.fsx       — 页面模板

open System
open System.IO
open System.Text.Json

// ── 站点主题配置 ─────────────────────────────────────────
let theme =
    {| primary   = "#6c63ff"
       primary2  = "#a78bfa"
       accent    = "#f59e0b"
       fontSans  = "system-ui, -apple-system, sans-serif"
       fontMono  = "'JetBrains Mono', 'Fira Code', monospace"
       maxWidth  = "820px"
    |}

// ── 社交链接 ──────────────────────────────────────────────
let social =
    {| github = "https://github.com/zest-ssg"
       docs   = "https://zest-ssg.dev"
    |}

// ── 输出 JSON — 以 section.key 格式展平 ──────────────────
let output = dict [
    "theme.primary"   , box theme.primary
    "theme.primary2"  , box theme.primary2
    "theme.accent"    , box theme.accent
    "theme.font_sans" , box theme.fontSans
    "theme.font_mono" , box theme.fontMono
    "theme.max_width" , box theme.maxWidth
    "social.github"   , box social.github
    "social.docs"     , box social.docs
]

printfn "%s" (JsonSerializer.Serialize (output |> Seq.map (fun kv -> kv.Key, kv.Value.ToString()) |> dict))
