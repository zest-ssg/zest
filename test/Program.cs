using System;
using System.IO;
using Zest.Engine.Zss;

class Program
{
    static int passed = 0, failed = 0;

    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== ZSS 2.0 Test Suite ===\n");

        try
        {

        // 1. 基础大括号语法
        Test("Brace syntax", @"
$primary: #6c63ff
.card {
  bgc: $primary
  bdr: 0.5r
  p: 1.5r
}
", expectContains: new[]{ ".card", "background-color: #6c63ff", "border-radius: 0.5rem", "padding: 1.5rem" });

        // 2. Python 缩进风格
        Test("Indent syntax", @"
$primary: #6c63ff
.card
  bgc: $primary
  bdr: 0.5r
  p: 1.5r
", expectContains: new[]{ ".card", "background-color: #6c63ff" });

        // 3. F# let 绑定
        Test("F# let bindings", @"
let primary = #6c63ff
let radius  = 0.5r
.card
  bgc = $primary
  bdr = $radius
", expectContains: new[]{ ".card", "background-color: #6c63ff", "border-radius: 0.5rem" });

        // 4. 管道运算符
        Test("Pipe operator", @"
let base = #6c63ff
.btn
  bgc = $base |> lighten(10%)
", expectContains: new[]{ ".btn", "background-color:" });

        // 5. 数学表达式
        Test("Math expressions", @"
let base = 16px
.title
  fs = $base * 1.5
  p  = $base / 2 + 4px
", expectContains: new[]{ ".title", "font-size: 24px" });

        // 6. 嵌套属性简写
        Test("Nested property shorthand", @"
.card
  margin.top = 10px
  margin.bottom = 20px
  border.radius = 0.5r
", expectContains: new[]{ ".card", "margin-top: 10px", "margin-bottom: 20px", "border-radius: 0.5rem" });

        // 7. 颜色函数
        Test("Color functions", @"
let brand = #6c63ff
.btn
  bgc = $brand |> lighten(20%)
  bdc = $brand |> darken(20%)
", expectContains: new[]{ ".btn", "background-color:", "border-color:" });

        // 8. @use 模块
        Test("@use utilities", @"
@use ""zest:utilities""
.custom
  @apply d-flex ai-center
  bdr: 0.5r
", expectContains: new[]{ ".custom", "display: flex", "align-items: center" });

        // 9. @export CSS 变量
        Test("@export CSS vars", @"
let primary = #3b82f6
let radius  = 0.5r
@export $primary
@export $radius
", expectContains: new[]{ ":root", "--primary: #3b82f6", "--radius: 0.5rem" });

        // 10. @each 循环
        Test("@each loop", @"
@each $size in (sm, md, lg)
  .text-#{$size}
    fs: 1r
", expectContains: new[]{ ".text-sm", ".text-md", ".text-lg" });

        // 11. Mixin + @include
        Test("Mixin and include", @"
@mixin card($pad: 1.5r)
  bgc: #fff
  bdr: 0.5r
  p: $pad
.post { @include card() }
.sidebar { @include card(1r) }
", expectContains: new[]{ ".post", ".sidebar", "background-color: #fff" });

        // 12. 嵌套规则
        Test("Nested rules", @"
nav
  d: flex
  a
    c: blue
    &:hover
      c: red
", expectContains: new[]{ "nav", "nav a", "nav a:hover" });

        // 13. 响应式断点
        Test("Responsive breakpoints", @"
.grid
  d: grid
  gtc: 1fr
  @md
    gtc: repeat(2, 1fr)
", expectContains: new[]{ "@media", "min-width: 768px" });

        // 14. 自动前缀
        Test("Auto prefixing", @"
.modal
  backdrop-filter: blur(10px)
  user-select: none
", expectContains: new[]{ "-webkit-backdrop-filter", "backdrop-filter" });

        // 15. 实际 style.zss 文件
        TestFile("Full style.zss", @"d:\Project\Zest\docs\assets\css\style.zss",
            expectContains: new[]{ "body", ".site-nav", ".hero", ".feature-card" });

        // ── 回归测试 (针对 docs/_site/assets/css/style.css 的 bug) ────────

        // 16. mn-width 这种带连字符的 shorthand 必须解析为 min-width
        Test("Regression: mn-width → min-width", @"
.x
  mn-width: 0
", expectContains: new[] { "min-width: 0" });

        // 17. letter-spacing: none 是非法 CSS,应转换为 normal
        Test("Regression: ls: none → letter-spacing: normal", @"
ul
  ls: none
", expectContains: new[] { "letter-spacing: normal" });

        // 18. bdr: 3px solid ... 应当解析为 border,不是 border-radius
        Test("Regression: bdr with style keyword → border", @"
.x
  bdr: 3px solid #ccc
", expectContains: new[] { "border: 3px solid" });

        // 19. bdr: 8px (无 style keyword) 应当解析为 border-radius
        Test("Regression: bdr single value → border-radius", @"
.x
  bdr: 8px
", expectContains: new[] { "border-radius: 8px" });

        // 20. blc shorthand
        Test("Regression: blc shorthand", @"
.x
  blc: #f00
", expectContains: new[] { "border-left-color: #f00" });

        // 21. utility class 中的 $var 必须使用 user 定义的变量
        Test("Regression: utility $var resolves from user scope", @"
$primary: #6c63ff
@use ""zest:utilities""
", expectContains: new[] { "color: #6c63ff", "background-color: #6c63ff" });

        // 22. @apply 内联展开
        Test("Regression: @apply inline expansion", @"
@use ""zest:utilities""
.x
  @apply d-flex ai-center
", expectContains: new[] { "display: flex", "align-items: center" });

        // 23. 嵌套在 rule 中的 @media 内部裸声明应继承 parent selector
        Test("Regression: nested @media inherits parent", @"
.docs-sidebar
  pos: sticky
  @media (max-width: 768px)
    pos: relative
", expectContains: new[] { ".docs-sidebar {", "@media (max-width: 768px) {", "position: relative" });

        // 24. box-shadow 多值(逗号)不应被解析为 selector list
        Test("Regression: comma in box-shadow value", @"
.card
  bxsh: 0 1px 3px 0 rgba(0,0,0,0.1), 0 1px 2px 0 rgba(0,0,0,0.06)
", expectContains: new[] { "box-shadow:", "rgba(0,0,0" });

        // 25. 用户文档产物中不应再出现任何旧 bug 痕迹
        TestFile("Regression: docs CSS has no invalid properties", @"d:\Project\Zest\docs\_site\assets\css\style.css",
            expectContains: new[] { "min-width", "border-radius", "letter-spacing" },
            expectMissing: new[] { "mn-width", " blc:", "border-radius:3px solid", "border-radius: 3px solid", "bgMuted", "$primary" });

        Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
        if (failed > 0) Console.WriteLine("❌ Some tests failed!");
        else Console.WriteLine("✅ All tests passed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 CRASH: {ex}");
        }
    }

    static void Test(string name, string zssSource, string[] expectContains, string[]? expectMissing = null)
    {
        try
        {
            var css = Processor.processText(zssSource);
            foreach (var expected in expectContains)
            {
                if (!css.Contains(expected))
                {
                    Console.WriteLine($"❌ FAIL: {name}");
                    Console.WriteLine($"   Expected to contain: '{expected}'");
                    Console.WriteLine($"   Output:\n{css}\n");
                    failed++;
                    return;
                }
            }
            if (expectMissing != null)
            {
                foreach (var banned in expectMissing)
                {
                    if (css.Contains(banned))
                    {
                        Console.WriteLine($"❌ FAIL: {name}");
                        Console.WriteLine($"   Should NOT contain: '{banned}'");
                        Console.WriteLine($"   Output:\n{css}\n");
                        failed++;
                        return;
                    }
                }
            }
            Console.WriteLine($"✅ PASS: {name}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAIL: {name} — {ex.Message}");
            failed++;
        }
    }

    static void TestFile(string name, string path, string[] expectContains, string[]? expectMissing = null)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"⚠️  SKIP: {name} — file not found: {path}");
            return;
        }
        // TestFile treats the file as a CSS artifact (not as ZSS source).
        var css = File.ReadAllText(path);
        try
        {
            foreach (var expected in expectContains)
            {
                if (!css.Contains(expected))
                {
                    Console.WriteLine($"❌ FAIL: {name}");
                    Console.WriteLine($"   Expected to contain: '{expected}'");
                    Console.WriteLine($"   Output (first 500 chars):\n{css.Substring(0, Math.Min(500, css.Length))}\n");
                    failed++;
                    return;
                }
            }
            if (expectMissing != null)
            {
                foreach (var banned in expectMissing)
                {
                    if (css.Contains(banned))
                    {
                        Console.WriteLine($"❌ FAIL: {name}");
                        Console.WriteLine($"   Should NOT contain: '{banned}'");
                        failed++;
                        return;
                    }
                }
            }
            Console.WriteLine($"✅ PASS: {name}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAIL: {name} — {ex.Message}");
            failed++;
        }
    }
}
