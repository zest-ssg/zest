// @title F# HTML DSL 实战
// @layout default
// @description Zest 的 F# DSL 模板系统 — render 函数、HTML 构造器、列表推导

let nums = [1..10]
let evens = nums |> List.filter (fun x -> x % 2 = 0)
let squares = evens |> List.map (fun x -> x * x)
let sum = squares |> List.sum

render [
    divC "page-header" [
        h1 [text "F# HTML DSL 实战"]
        p [text "用类型安全的 F# 代码构建 HTML"]
    ]

    divC "container" [
        yield divC "callout callout-info" [
            p [text "该页面完全由 .zpage.fsx 文件中的 F# 代码生成，未使用任何 Markdown。"]
        ]

        yield h2 [text "基础元素"]
        yield raw "<table>
  <thead><tr><th>F# 代码</th><th>输出 HTML</th></tr></thead>
  <tbody>
    <tr><td><code>h1 [text \"Title\"]</code></td><td>&lt;h1&gt;Title&lt;/h1&gt;</td></tr>
    <tr><td><code>p [text \"Content\"]</code></td><td>&lt;p&gt;Content&lt;/p&gt;</td></tr>
    <tr><td><code>a \"/url/\" [text \"Link\"]</code></td><td>&lt;a href=\"/url/\"&gt;Link&lt;/a&gt;</td></tr>
    <tr><td><code>ul [li [text \"A\"]]</code></td><td>&lt;ul&gt;&lt;li&gt;A&lt;/li&gt;&lt;/ul&gt;</td></tr>
  </tbody>
</table>"

        yield h2 [text "带类名的快捷构造"]
        yield codeBlock "fsharp" "divC    \"card\"  [ p [ text \"Content\" ] ]\nspanC   \"badge\" [ text \"New\" ]\nsectionC \"hero\"  [ h1 [ text \"Title\" ] ]\naC \"tag\" \"/url/\" [ text \"Tag\" ]"

        yield h2 [text "列表推导（for 循环）"]
        let items = ["F#"; "Zest"; "SSG"; "ZCSS"]
        yield ul [for item in items -> li [text item]]

        yield h2 [text "条件渲染"]
        yield p [text "条件为 true → ✅ 显示"]

        yield h2 [text "构建时计算"]
        yield raw (sprintf "<ul>
  <li>1..10 中的偶数：%s</li>
  <li>偶数的平方：%s</li>
  <li>偶数平方和：<strong>%d</strong></li>
</ul>"
            (evens |> List.map string |> String.concat ", ")
            (squares |> List.map string |> String.concat ", ")
            sum)

        yield h2 [text "原始 HTML 注入"]
        yield raw "<div class=\"callout callout-info\"><p>这是通过 <code>raw</code> 函数注入的原始 HTML。</p></div>"

        yield h2 [text "完整源代码"]
        yield codeBlock "fsharp" "// @title F# HTML DSL 实战\n// @layout default\n\nrender [\n    divC \"page-header\" [\n        h1 [text \"F# HTML DSL 实战\"]\n    ]\n    divC \"container\" [\n        h2 [text \"列表推导\"]\n        let items = [\"F#\"; \"Zest\"; \"SSG\"]\n        ul [for i in items -> li [text i]]\n\n        h2 [text \"构建时计算\"]\n        let sum = [1..10] |> List.filter (fun x -> x%2=0) |> List.map (fun x -> x*x) |> List.sum\n        p [text (sprintf \"偶数平方和 = %d\" sum)]\n    ]\n]"
    ]
]
