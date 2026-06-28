// _data/authors.zest.fsx — 作者信息数据文件
// 输出 JSON，由 _init.zest.fsx 通过 exec() 加载。

open System
open System.Text.Json

let authors = [|
    {| id = "zest-team"
       name = "Zest Team"
       bio = "Zest SSG 核心开发团队"
       url = "https://github.com/zest-ssg"
    |}
    {| id = "contributor"
       name = "Contributor"
       bio = "社区贡献者"
       url = ""
    |}
|]

let output = dict [
    "authors", box (authors |> Array.map (fun a -> sprintf "%s|%s|%s" a.id a.name a.bio))
]

printfn "%s" (JsonSerializer.Serialize (output |> Seq.map (fun kv -> kv.Key, kv.Value.ToString()) |> dict))
