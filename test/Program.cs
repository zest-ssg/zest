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

        Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
        if (failed > 0) Console.WriteLine("❌ Some tests failed!");
        else Console.WriteLine("✅ All tests passed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 CRASH: {ex}");
        }
    }

    static void Test(string name, string zssSource, string[] expectContains)
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
            Console.WriteLine($"✅ PASS: {name}");
            passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAIL: {name} — {ex.Message}");
            failed++;
        }
    }

    static void TestFile(string name, string path, string[] expectContains)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"⚠️  SKIP: {name} — file not found: {path}");
            return;
        }
        var source = File.ReadAllText(path);
        Test(name, source, expectContains);
    }
}
