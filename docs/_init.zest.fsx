// _init.zest.fsx — Zest 展示站点初始化脚本
// 构建前自动执行，用于动态注入全局数据。

open System
open System.IO
open System.Text.Json

console_log "Zest Showcase _init.zest.fsx running..."

// ── 加载 _data/*.zest.fsx 数据文件 ──
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

// ── 注入构建信息 ──
let env = loadEnv "ZEST_ENV"
if env <> "" then addGlobal "env" env

let gitHash =
    try exec "git" "rev-parse --short HEAD"
    with _ -> "unknown"
addGlobal "git_hash" gitHash
addGlobal "build_time" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))

console_log "Zest Showcase _init.zest.fsx completed."
