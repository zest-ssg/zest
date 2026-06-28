// @title ZCSS 特性展示
// @layout default
// @description ZCSS 样式系统完整特性展示 — 三种语法、变量、管道、颜色函数

render [
    divC "page-header" [
        h1 [text "ZCSS 特性展示"]
        p [text "CSS 超集 — 三种语法风格、变量系统、管道运算符、颜色函数"]
    ]

    divC "container" [

        h2 [text "三种语法风格"]
        p [text "ZCSS 支持三种写法，可按需选择或混用："]
        yield raw "
<table>
  <thead><tr><th>风格</th><th>示例</th></tr></thead>
  <tbody>
    <tr><td>SCSS 风格</td><td><code>.card { bgc: #fff; bdr: 0.5r; }</code></td></tr>
    <tr><td>Python 缩进</td><td><code>.card<br/>&nbsp;&nbsp;bgc: #fff<br/>&nbsp;&nbsp;bdr: 0.5r</code></td></tr>
    <tr><td>F# 风格</td><td><code>let primary = #6c63ff<br/>.card<br/>&nbsp;&nbsp;bgc = primary</code></td></tr>
  </tbody>
</table>"

        h2 [text "变量与数学表达式"]
        codeBlock "zcss" "// F# 风格变量 + 数学运算\nlet primary  = #6c63ff\nlet radius   = 0.5r\nlet baseSize = 16px\n\n.title\n  c = primary\n  fs = baseSize * 1.5\n  p = baseSize / 2 + 4px\n  bdr = radius"

        h2 [text "管道运算符"]
        codeBlock "zcss" "let brand = #6c63ff\n\n.btn-primary\n  bgc = brand |> lighten(10%)\n  bxsh = #000 |> mix(brand, 25%) |> alpha(0.1)\n  tr = all 0.2s\n\n  &:hover\n    bgc = brand |> darken(5%)"

        h2 [text "颜色函数"]
        yield raw "
<table>
  <thead><tr><th>函数</th><th>说明</th></tr></thead>
  <tbody>
    <tr><td><code>lighten(c, 20%)</code></td><td>变亮</td></tr>
    <tr><td><code>darken(c, 20%)</code></td><td>变暗</td></tr>
    <tr><td><code>alpha(c, 0.5)</code></td><td>透明度</td></tr>
    <tr><td><code>mix(a, b, 50%)</code></td><td>混合</td></tr>
    <tr><td><code>complement(c)</code></td><td>互补色</td></tr>
    <tr><td><code>grayscale(c)</code></td><td>灰度</td></tr>
    <tr><td><code>adjust-hue(c, 30deg)</code></td><td>色相旋转</td></tr>
  </tbody>
</table>"

        h2 [text "属性简写"]
        p [text "ZCSS 提供 60+ 个属性简写，大幅减少样板代码："]
        yield raw "
<table>
  <thead><tr><th>简写</th><th>完整属性</th><th>简写</th><th>完整属性</th></tr></thead>
  <tbody>
    <tr><td><code>bgc</code></td><td>background-color</td><td><code>c</code></td><td>color</td></tr>
    <tr><td><code>fs</code></td><td>font-size</td><td><code>fw</code></td><td>font-weight</td></tr>
    <tr><td><code>bdr</code></td><td>border-radius</td><td><code>bxsh</code></td><td>box-shadow</td></tr>
    <tr><td><code>d</code></td><td>display</td><td><code>pos</code></td><td>position</td></tr>
    <tr><td><code>p/m</code></td><td>padding/margin</td><td><code>mt/mb/ml/mr</code></td><td>margin-*</td></tr>
    <tr><td><code>gtc</code></td><td>grid-template-columns</td><td><code>ai/jc</code></td><td>align-items/justify-content</td></tr>
  </tbody>
</table>"

        h2 [text "混入（Mixin）"]
        codeBlock "zcss" "@mixin card($pad: 1.5r)\n  bgc: #fff\n  bdr: 0.5r\n  p: $pad\n  bxsh: 0 2px 8px rgba(0,0,0,0.1)\n\n.post  { @include card() }\n.sidebar { @include card(1r) }"

        h2 [text "响应式断点简写"]
        codeBlock "zcss" ".grid\n  d: grid\n  gtc: 1fr\n\n  @md\n    gtc: repeat(2, 1fr)\n\n  @lg\n    gtc: repeat(3, 1fr)"

        h2 [text "@apply — 工具类"]
        codeBlock "zcss" "@use \"zest:utilities\"\n\n.custom-card\n  @apply d-block p-4 bg-white rounded-lg shadow-md"

        h2 [text "本页面的 ZCSS 源文件"]
        yield raw "<p>本演示站点全部样式由 <code>.zcss</code> 文件编写，由 Zest 构建时自动编译为标准的 <code>.css</code>。查看源码：</p>"
        codeBlock "zcss" "// core.zcss — 设计 token + base\nlet primary = #6c63ff\nlet text    = #0f172a\nlet fontMono = 'JetBrains Mono', monospace\n\nbody\n  font-family = fontSans\n  color = $text\n  -webkit-font-smoothing: antialiased"
    ]
]
