// _init.zest.fsx — Zest Docs 站点初始化脚本
// 构建前自动执行，用于动态注入全局数据。
//
// .zest.fsx 命名体系：
//   _init.zest.fsx       — 构建时初始化（自动执行）
//   _data/*.zest.fsx    — 数据定义（由 _init 加载）
//   *.zpage.fsx          — 页面模板
//
// 可用 API：
//   addGlobal key value   — 注入键值对到全局数据
//   loadJson path         — 解析 JSON 文件为对象
//   loadToml path         — 解析 TOML 文件为字典
//   loadEnv key           — 读取环境变量
//   console_log msg       — 调试输出到 stderr
//   exec cmd args         — 执行 shell 命令，返回 stdout

open System
open System.IO
open System.Text.Json

console_log "Zest Docs _init.zest.fsx running..."

// ── 加载 _data/*.zest.fsx 数据文件 ──────────────────────
// 每个数据脚本输出 JSON 到 stdout，exec 捕获后注入全局数据。
let loadDataFile (relPath: string) =
    try
        let json = exec "dotnet" (sprintf "fsi --quiet --nologo --exec \"%s\"" relPath)
        let doc = JsonDocument.Parse(json)
        for prop in doc.RootElement.EnumerateObject() do
            addGlobal prop.Name (prop.Value.GetString())
            console_log (sprintf "  + global: %s" prop.Name)
    with ex ->
        console_log (sprintf "  [WARN] 加载 %s 失败: %s" relPath ex.Message)

loadDataFile "_data/settings.zest.fsx"
loadDataFile "_data/authors.zest.fsx"

// ── 从环境变量注入构建信息 ──────────────────────────────
let env = loadEnv "ZEST_ENV"
if env <> "" then
    addGlobal "env" env
    console_log (sprintf "Environment: %s" env)

// ── 注入 Git 信息 ───────────────────────────────────────
let gitHash =
    try exec "git" "rev-parse --short HEAD"
    with _ -> "unknown"
addGlobal "git_hash" gitHash

// ── 注入当前时间 ────────────────────────────────────────
addGlobal "build_time" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))

console_log "Zest Docs _init.zest.fsx completed."
