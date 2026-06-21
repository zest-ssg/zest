using System;
using System.Collections.Generic;
using System.IO;
using Zest.Engine.Zss;

/// <summary>
/// Extended ZSS test suite — covers advanced features and edge cases.
/// Tests are written but not executed in the main program.
/// </summary>
static class ExtendedTests
{
    public static int Passed = 0;
    public static int Failed = 0;

    public static void Run()
    {
        Console.WriteLine("\n=== ZSS Extended Test Suite ===\n");

        // ── 16. 条件指令 @if/@else ──
        RunTest("Conditional @if true", @"
$dark-mode: false
.card
  bgc: #fff
  @if $dark-mode
    bgc: #1a1a1a
    c: #fff
", expectContains: new[] { ".card", "background-color: #fff" });

        RunTest("Conditional @if/@else", @"
$dark-mode: true
.card
  @if $dark-mode
    bgc: #1a1a1a
  @else
    bgc: #ffffff
", expectContains: new[] { ".card", "background-color: #1a1a1a" });

        // ── 17. @for 循环 ──
        RunTest("@for loop", @"
@for $i from 1 through 3
  .col-$i
    w: $i * 100px
", expectContains: new[] { ".col-1", ".col-2", ".col-3", "width: 100px", "width: 200px", "width: 300px" });

        // ── 18. @each 映射循环 ──
        RunTest("@each map loop", @"
$colors: red, green, blue
@each $name, $value in $colors
  .text-$name
    c: $value
", expectContains: new[] { ".text-red", ".text-green", ".text-blue" });

        // ── 19. 嵌套规则和父选择器 ──
        RunTest("Nested rules with parent selector", @"
.btn
  d: inline-block
  p: 1r 2r
  &:hover
    o: 0.85
  &-primary
    bgc: #3b82f6
  &-secondary
    bgc: #64748b
", expectContains: new[] { ".btn", ".btn:hover", ".btn-primary", ".btn-secondary", "opacity: 0.85" });

        // ── 20. 多层嵌套 ──
        RunTest("Deep nesting", @"
nav
  ul
    li
      a
        c: blue
        &:hover
          c: red
", expectContains: new[] { "nav ul li a", "nav ul li a:hover" });

        // ── 21. Mixin 参数默认值 ──
        RunTest("Mixin default parameters", @"
@mixin button($bg: #3b82f6, $color: #fff, $pad: 0.5r 1r)
  d: inline-block
  bgc: $bg
  c: $color
  p: $pad
  bdr: 0.3r

.btn-default { @include button() }
.btn-custom  { @include button(#ef4444, #fff, 1r 2r) }
", expectContains: new[] { ".btn-default", ".btn-custom", "background-color: #3b82f6", "background-color: #ef4444" });

        // ── 22. Mixin 内容块 @content ──
        RunTest("Mixin content block", @"
@mixin responsive
  @media (max-width: 768px)
    @content

.card
  fs: 1.5r
  @include responsive
    fs: 1r
", expectContains: new[] { "@media", "max-width: 768px", "font-size: 1.5rem", "font-size: 1rem" });

        // ── 23. 颜色函数链式调用 ──
        RunTest("Chained color functions", @"
let base = #3b82f6
.btn
  bgc = base |> lighten(20%) |> alpha(0.5)
  bdc = base |> darken(30%)
", expectContains: new[] { ".btn", "background-color:", "border-color:" });

        // ── 24. 数学表达式复杂运算 ──
        RunTest("Complex math expressions", @"
let base = 16px
.container
  w = (base * 4) + 20px
  h = base * 2.5
  p = base / 4
  m = base * 1.5 - 4px
", expectContains: new[] { ".container", "width: 84px", "height: 40px", "padding: 4px" });

        // ── 25. CSS 变量导出 ──
        RunTest("CSS variable export", @"
let primary = #6c63ff
let radius  = 0.5r
let spacing = 1.5r
@export $primary
@export $radius
@export $spacing
", expectContains: new[] { ":root", "--primary: #6c63ff", "--radius: 0.5rem", "--spacing: 1.5rem" });

        // ── 26. @apply 工具类组合 ──
        RunTest("@apply utility classes", @"
@use ""zest:utilities""
.card
  @apply d-flex ai-center jc-between p-4 gap-2
  bdr: 0.5r
  bxsh: 0 2px 8px rgba(0,0,0,0.1)
", expectContains: new[] { ".card", "display: flex", "align-items: center", "justify-content: space-between" });

        // ── 27. 响应式断点嵌套 ──
        RunTest("Responsive breakpoints", @"
.grid
  d: grid
  gtc: 1fr
  gap: 1r
  @sm
    gtc: repeat(2, 1fr)
  @md
    gtc: repeat(3, 1fr)
  @lg
    gtc: repeat(4, 1fr)
", expectContains: new[] { "@media", "min-width: 640px", "min-width: 768px", "min-width: 1024px" });

        // ── 28. 自动厂商前缀 ──
        RunTest("Auto vendor prefixing", @"
.modal
  backdrop-filter: blur(10px)
  user-select: none
  appearance: none
  transition: all 0.3s ease
", expectContains: new[] { "-webkit-backdrop-filter", "backdrop-filter", "-webkit-user-select", "user-select", "-webkit-appearance", "appearance" });

        // ── 29. @extend 继承 ──
        RunTest("@extend inheritance", @"
.base-button
  d: inline-block
  p: 0.5r 1r
  bdr: 0.3r
  td: none

.primary-button
  @extend .base-button
  bgc: #3b82f6
  c: #fff
", expectContains: new[] { ".base-button", ".primary-button", "display: inline-block", "background-color: #3b82f6" });

        // ── 30. @option 指令 ──
        RunTest("@option directive", @"
@option minify: true
@option prefix: true
.card
  bgc: #fff
  bdr: 0.5r
", expectContains: new[] { ".card" });

        // ── 31. @warn 和 @debug ──
        RunTest("@warn directive", @"
@warn ""This is a warning""
.card
  bgc: #fff
", expectContains: new[] { ".card", "background-color: #fff" });

        // ── 32. 混合语法风格 ──
        RunTest("Mixed syntax styles", @"
let primary = #3b82f6
$secondary: #64748b

.card
  bgc = primary
  bdr: 0.5r

  .inner
    bgc: $secondary
    p = 1r
", expectContains: new[] { ".card", ".card .inner", "background-color: #3b82f6", "background-color: #64748b" });

        // ── 33. 复杂选择器 ──
        RunTest("Complex selectors", @"
.card > .title
  fs: 1.5r
  fw: 700

.card + .card
  mt: 1r

.card ~ .card
  mt: 0.5r

input[type=""text""]
  bdr: 1px solid #ccc
  p: 0.5r
", expectContains: new[] { ".card > .title", ".card + .card", ".card ~ .card", "input[type=\"text\"]" });

        // ── 34. 伪类和伪元素 ──
        RunTest("Pseudo-classes and pseudo-elements", @"
.button
  bgc: #3b82f6
  tr: all 0.2s
  &:hover
    bgc: #2563eb
  &:active
    trf: scale(0.98)
  &:focus
    outline: 2px solid #3b82f6
  &::before
    content: ""→""
    mr: 0.5r
  &::after
    content: """"
    d: block
", expectContains: new[] { ".button:hover", ".button:active", ".button:focus", ".button::before", ".button::after" });

        // ── 35. CSS Minification ──
        RunTest("CSS minification", @"
@option minify: true
.card
  bgc: #ffffff
  c: #000000
  p: 0.5r 1r 0.5r 1r
  m: 0 0 0 0
", expectContains: new[] { ".card" });

        // ── 36. 空值和默认值处理 ──
        RunTest("Empty and default values", @"
$default: #ccc
.card
  bgc: $default
  bdr: 0
  bd: none
", expectContains: new[] { ".card", "background-color: #ccc", "border-radius: 0", "border: none" });

        // ── 37. 单位转换 ──
        RunTest("Unit conversion", @"
.card
  w: 100p
  h: 100vh
  fs: 1.5r
  gap: 2r
  mt: 10p
", expectContains: new[] { "width: 100%", "height: 100vh", "font-size: 1.5rem", "gap: 2rem", "margin-top: 10%" });

        // ── 38. @import 指令 ──
        RunTest("@import directive", @"
@import ""variables""
@import ""mixins""
.card
  bgc: #fff
", expectContains: new[] { ".card", "background-color: #fff" });

        // ── 39. 通用 at-rules ──
        RunTest("Generic at-rules", @"
@keyframes fadeIn
  from
    o: 0
  to
    o: 1

@supports (display: grid)
  .grid
    d: grid
", expectContains: new[] { "@keyframes fadeIn", "@supports", "display: grid" });

        // ── 40. 注释处理 ──
        RunTest("Comment handling", @"
// This is a line comment
/* This is a block comment */
.card
  bgc: #fff // inline comment
  /* block comment */
  bdr: 0.5r
", expectContains: new[] { ".card", "background-color: #fff", "border-radius: 0.5rem" });

        // ── 41. 大型样式表性能 ──
        RunTest("Large stylesheet performance", @"
@for $i from 1 through 10
  .col-$i
    w: $i * 10p
", expectContains: new[] { ".col-1", ".col-10", "width: 10%", "width: 100%" });

        // ── 42. 嵌套属性简写扩展 ──
        RunTest("Extended nested property shorthand", @"
.card
  margin.top = 10px
  margin.right = 20px
  margin.bottom = 30px
  margin.left = 40px
  border.width = 1px
  border.style = solid
  border.color = #ccc
  padding.x = 1r
  padding.y = 0.5r
", expectContains: new[] { "margin-top: 10px", "margin-right: 20px", "margin-bottom: 30px", "margin-left: 40px", "border-width: 1px", "border-style: solid" });

        // ── 43. F# 风格管道运算符组合 ──
        RunTest("F# pipe operator composition", @"
let base = #3b82f6
let accent = #f59e0b
.btn
  bgc = base |> lighten(10%) |> alpha(0.8)
  bdc = accent |> darken(15%)
  bxsh = base |> alpha(0.2)
", expectContains: new[] { ".btn", "background-color:", "border-color:", "box-shadow:" });

        // ── 44. CSS Grid 完整布局 ──
        RunTest("CSS Grid layout", @"
.grid
  d: grid
  gtc: repeat(3, 1fr)
  gap: 1.5r
  gta: ""header header header"" ""sidebar main main"" ""footer footer footer""

  @md
    gtc: 1fr
    gta: ""header"" ""sidebar"" ""main"" ""footer""
", expectContains: new[] { "display: grid", "grid-template-columns: repeat(3, 1fr)", "grid-template-areas:" });

        // ── 45. 动画关键帧 ──
        RunTest("Animation keyframes", @"
@keyframes slideIn
  0p
    trf: translateX(-100p)
    o: 0
  100p
    trf: translateX(0)
    o: 1

.animated
  anim: slideIn 0.3s ease-out
", expectContains: new[] { "@keyframes slideIn", "transform: translateX(-100%)", "transform: translateX(0)", "animation: slideIn 0.3s ease-out" });

        Console.WriteLine($"\n=== Extended Results: {Passed} passed, {Failed} failed ===");
    }

    static void RunTest(string name, string zssSource, string[] expectContains)
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
                    Failed++;
                    return;
                }
            }
            Console.WriteLine($"✅ PASS: {name}");
            Passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAIL: {name} — {ex.Message}");
            Failed++;
        }
    }
}
