// @title  像 11ty.js 一样编程 — 真实 F# 脚本构建
// @layout default
// @description  展示 Zest 如何像 11ty.js 那样在构建时执行 F# 代码生成 HTML
render [
    h1 [ text "像 11ty.js 一样编程" ]

    blockquote [
        p [ text "「Zest 的模版即代码——就像 11ty.js 中 JavaScript 模版在构建时执行，Zest 的 .zest.fsx 文件是真实 F# 脚本，在构建时编译求值。」" ]
    ]

    h2 [ text "概述" ]
    p [ text "Zest 的 .zest.fsx 文件不仅仅是模版——它们是真正的 F# 脚本。" ]
    p [ text "就像 11ty.js 中 JavaScript 模版文件在构建时执行一样，Zest 使用 FSharp.Compiler.Service 的 FsiEvaluationSession 在进程内编译并执行 F# 代码，将结果合并到生成的 HTML 页面中。" ]

    h2 [ text "页面结构" ]
    p [ text "一个典型的 .zest.fsx 文件由两部分组成：" ]
    h3 [ text "1. 元数据注释（// @ 前缀）" ]
    codeBlock "fsharp" "// @title 页面标题\n// @layout 布局模板\n// @description 页面描述\n// @tags [\"tag1\"; \"tag2\"]"

    h3 [ text "2. HTML 内容表达式" ]
    p [ text "使用 render 函数将 DSL 构建的 HTML 节点列表渲染为字符串：" ]
    codeBlock "fsharp" "render [\n    h1 [ text \"Hello\" ]\n    p  [ text \"World\" ]\n    ul [ li [ text \"Item 1\" ]; li [ text \"Item 2\" ] ]\n]"

    h2 [ text "构建时计算示例" ]
    p [ text "这些值在构建时由 F# 编译器真实计算：" ]

    divC "example-box" [
        p [ strong [ text "当前构建时间：" ] ]
        p [ text (sprintf "%s" (DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))) ]
        p [ strong [ text "列表运算结果：" ] ]
        p [ text (sprintf "1 到 10 中偶数的平方和 = %d" ([1..10] |> List.filter (fun x -> x % 2 = 0) |> List.map (fun x -> x * x) |> List.sum)) ]
        p [ strong [ text "条件分支：" ] ]
        p [ text (if DateTime.Now.Hour < 12 then "上午好！" else "下午好！") ]
    ]

    h2 [ text "HTML DSL 元素" ]
    p [ text "Zest 提供了一套完整的 HTML 构造函数，可直接在 F# 中使用：" ]

    h3 [ text "文本与内联元素" ]
    codeBlock "fsharp" "text \"纯文本\"        // 纯文本节点\nraw \"<em>原始HTML</em>\"  // 原始 HTML（不转义）\nstrong [ text \"粗体\" ]   // <strong>\nspan [ text \"内联\" ]     // <span>\na \"https://example.com\" [ text \"链接\" ]  // <a>"

    h3 [ text "块级元素" ]
    codeBlock "fsharp" "h1  [ text \"标题1\" ]\nh2  [ text \"标题2\" ]\nh3  [ text \"标题3\" ]\np   [ text \"段落\" ]\nul  [ li [ text \"项目\" ] ]\nol  [ li [ text \"项目\" ] ]\ndiv [ text \"容器\" ]\ndivC \"my-class\" [ text \"带类名的容器\" ]"

    h3 [ text "代码与预格式化" ]
    codeBlock "fsharp" "codeBlock \"fsharp\" \"let x = 42\"         // <pre><code class=\"lang-fsharp\">\ncodeBlock \"\" \"inline code\"               // 无语言标记\npre [ code [ text \"code block\" ] ]        // 手动构建"

    h2 [ text "render 辅助函数" ]
    p [ text "setup 阶段定义了 `render` 辅助函数，它是 `HtmlRenderer.render` 的别名：" ]
    codeBlock "fsharp" "// 以下两种写法等价：\nrender [ h1 [ text \"A\" ]; p [ text \"B\" ] ]\nHtmlRenderer.render [ h1 [ text \"A\" ]; p [ text \"B\" ] ]"

    h2 [ text "完整示例" ]
    p [ text "下面是一个完整的 .zest.fsx 页面文件：" ]
    codeBlock "fsharp" "// @title 我的文章\n// @layout default\n// @date 2026-06-20\n// @tags [\"demo\"]\n\nlet pageTitle = \"我的文章标题\"\nlet items = [\"F#\"; \"Zest\"; \"SSG\"]\n\nrender [\n    h1 [ text pageTitle ]\n    p  [ text \"这是一篇由 F# 脚本生成的页面。\" ]\n    p  [ text (sprintf \"共有 %d 个项目：\" items.Length) ]\n    ul [ for i in items -> li [ text i ] ]\n]"

    h2 [ text "工作原理" ]
    p [ text "Zest 的 F# 脚本执行流程：" ]
    ol [
        li [ p [ text "检测文件是否以 F# 脚本开头（通过 isPageScript 启发式检查）" ] ]
        li [ p [ text "解析 // @ 元数据注释，提取 title/layout/permalink/tags/date" ] ]
        li [ p [ text "将所有页面数据序列化为 JSON 临时文件" ] ]
        li [ p [ text "生成 preamble 脚本（DSL 函数 + collections API），通过 @\"...\" 路径读取 JSON" ] ]
        li [ p [ text "拼接 preamble + 用户脚本，写入临时 .fsx 文件" ] ]
        li [ p [ text "启动 dotnet fsi 子进程执行脚本，捕获 stdout 作为 HTML 输出" ] ]
        li [ p [ text "将 HTML 内容与元数据合并，应用布局模板，写出最终文件" ] ]
        li [ p [ text "如果脚本求值失败，自动回退到 Markdown 传统模式" ] ]
    ]
]
