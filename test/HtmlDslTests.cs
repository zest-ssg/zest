using System;
using Zest.Engine;
using Zest.Engine.Html;

/// <summary>
/// HTML DSL test suite — covers element builders, modifiers, and components.
/// Tests are written but not executed in the main program.
/// </summary>
static class HtmlDslTests
{
    public static int Passed = 0;
    public static int Failed = 0;

    public static void Run()
    {
        Console.WriteLine("\n=== HTML DSL Test Suite ===\n");

        // ── 1. 基本元素构建 ──
        RunTest("Basic element construction", () =>
        {
            var node = div [ text "Hello" ];
            var html = Renderer.render node;
            return html.Contains("<div>") && html.Contains("Hello") && html.Contains("</div>");
        });

        // ── 2. 带属性的元素 ──
        RunTest("Element with attributes", () =>
        {
            var node = a "https://example.com" [ text "Link" ];
            var html = Renderer.render node;
            return html.Contains("<a href=\"https://example.com\">") && html.Contains("Link");
        });

        // ── 3. 类快捷构造器 ──
        RunTest("Class shortcut constructors", () =>
        {
            var node = divC "container" [ p [ text "Content" ] ];
            var html = Renderer.render node;
            return html.Contains("<div class=\"container\">") && html.Contains("<p>") && html.Contains("Content");
        });

        // ── 4. ID 快捷构造器 ──
        RunTest("ID shortcut constructors", () =>
        {
            var node = divId "main" [ text "Main content" ];
            var html = Renderer.render node;
            return html.Contains("<div id=\"main\">") && html.Contains("Main content");
        });

        // ── 5. 组合 class+id 构造器 ──
        RunTest("Combined class+id constructors", () =>
        {
            var node = divCI "container" "main" [ text "Content" ];
            var html = Renderer.render node;
            return html.Contains("class=\"container\"") && html.Contains("id=\"main\"");
        });

        // ── 6. withClass 修饰符 ──
        RunTest("withClass modifier", () =>
        {
            var node = div [ text "Test" ] |> withClass "active";
            var html = Renderer.render node;
            return html.Contains("<div class=\"active\">");
        });

        // ── 7. withId 修饰符 ──
        RunTest("withId modifier", () =>
        {
            var node = div [ text "Test" ] |> withId "header";
            var html = Renderer.render node;
            return html.Contains("<div id=\"header\">");
        });

        // ── 8. withAttr 修饰符 ──
        RunTest("withAttr modifier", () =>
        {
            var node = div [ text "Test" ] |> withAttr "data-value" "123";
            var html = Renderer.render node;
            return html.Contains("data-value=\"123\"");
        });

        // ── 9. withStyle 修饰符 ──
        RunTest("withStyle modifier", () =>
        {
            var node = div [ text "Test" ] |> withStyle "color: red;";
            var html = Renderer.render node;
            return html.Contains("style=\"color: red;\"");
        });

        // ── 10. withClasses 多类 ──
        RunTest("withClasses multiple classes", () =>
        {
            var node = div [ text "Test" ] |> withClasses ["btn", "btn-primary", "active"];
            var html = Renderer.render node;
            return html.Contains("class=\"btn btn-primary active\"");
        });

        // ── 11. toggleClass 条件类 ──
        RunTest("toggleClass conditional", () =>
        {
            var node1 = div [ text "Test" ] |> toggleClass "active" true;
            var node2 = div [ text "Test" ] |> toggleClass "active" false;
            var html1 = Renderer.render node1;
            var html2 = Renderer.render node2;
            return html1.Contains("class=\"active\"") && !html2.Contains("class=\"active\"");
        });

        // ── 12. withoutClass 移除类 ──
        RunTest("withoutClass remove class", () =>
        {
            var node = divC "btn active" [ text "Test" ] |> withoutClass "active";
            var html = Renderer.render node;
            return html.Contains("class=\"btn\"") && !html.Contains("active");
        });

        // ── 13. showIf/hideIf 条件渲染 ──
        RunTest("showIf/hideIf conditional render", () =>
        {
            var node1 = showIf true (text "Visible");
            var node2 = showIf false (text "Hidden");
            var html1 = Renderer.render node1;
            var html2 = Renderer.render node2;
            return html1.Contains("Visible") && !html2.Contains("Hidden");
        });

        // ── 14. each 列表渲染 ──
        RunTest("each list rendering", () =>
        {
            var items = ["Apple", "Banana", "Cherry"];
            var node = ul (each items (fun item -> li [ text item ]));
            var html = Renderer.render node;
            return html.Contains("<ul>") && html.Contains("Apple") && html.Contains("Banana") && html.Contains("Cherry");
        });

        // ── 15. eachI 带索引列表渲染 ──
        RunTest("eachI indexed list rendering", () =>
        {
            var items = ["A", "B", "C"];
            var node = ul (eachI items (fun i item -> li [ text $"{i}: {item}" ]));
            var html = Renderer.render node;
            return html.Contains("0: A") && html.Contains("1: B") && html.Contains("2: C");
        });

        // ── 16. renderIf 条件渲染带回退 ──
        RunTest("renderIf with fallback", () =>
        {
            var node = renderIf true (text "Yes") (text "No");
            var html = Renderer.render node;
            return html.Contains("Yes") && !html.Contains("No");
        });

        // ── 17. renderOpt 可选值渲染 ──
        RunTest("renderOpt optional rendering", () =>
        {
            var opt = Some("Hello");
            var node = renderOpt opt (fun s -> text s);
            var html = Renderer.render node;
            return html.Contains("Hello");
        });

        // ── 18. joinWith 分隔符连接 ──
        RunTest("joinWith separator", () =>
        {
            var nodes = [text "A"; text "B"; text "C"];
            var node = div (joinWith (text " | ") nodes);
            var html = Renderer.render node;
            return html.Contains("A") && html.Contains("B") && html.Contains("C") && html.Contains("|");
        });

        // ── 19. emptyState 空状态 ──
        RunTest("emptyState component", () =>
        {
            var node = emptyState "No items found";
            var html = Renderer.render node;
            return html.Contains("empty-state") && html.Contains("No items found");
        });

        // ── 20. 嵌套元素结构 ──
        RunTest("Nested element structure", () =>
        {
            var node = divC "card" [
                h1 [ text "Title" ]
                p [ text "Content" ]
                a "https://example.com" [ text "Link" ]
            ];
            var html = Renderer.render node;
            return html.Contains("<div class=\"card\">") && html.Contains("<h1>Title</h1>") && html.Contains("<p>Content</p>") && html.Contains("<a href=\"https://example.com\">Link</a>");
        });

        // ── 21. 表单元素 ──
        RunTest("Form elements", () =>
        {
            var node = formC "login" "/login" [
                labelC "form-label" "username" [ text "Username" ]
                input "text" "username" "username"
                buttonC "btn-primary" [ text "Login" ]
            ];
            var html = Renderer.render node;
            return html.Contains("<form") && html.Contains("action=\"/login\"") && html.Contains("<label") && html.Contains("<input") && html.Contains("<button");
        });

        // ── 22. 表格结构 ──
        RunTest("Table structure", () =>
        {
            var node = tableC "data-table" [
                thead [ tr [ th [ text "Name" ]; th [ text "Age" ] ] ]
                tbody [
                    tr [ td [ text "Alice" ]; td [ text "30" ] ]
                    tr [ td [ text "Bob" ]; td [ text "25" ] ]
                ]
            ];
            var html = Renderer.render node;
            return html.Contains("<table") && html.Contains("<thead>") && html.Contains("<tbody>") && html.Contains("Alice") && html.Contains("Bob");
        });

        // ── 23. 列表结构 ──
        RunTest("List structure", () =>
        {
            var node = ulC "item-list" [
                liC "item" [ text "First" ]
                liC "item" [ text "Second" ]
                liC "item" [ text "Third" ]
            ];
            var html = Renderer.render node;
            return html.Contains("<ul class=\"item-list\">") && html.Contains("First") && html.Contains("Second") && html.Contains("Third");
        });

        // ── 24. 图片元素 ──
        RunTest("Image element", () =>
        {
            var node = img "photo.jpg" "A photo";
            var html = Renderer.render node;
            return html.Contains("<img src=\"photo.jpg\" alt=\"A photo\"");
        });

        // ── 25. 文档结构 ──
        RunTest("Document structure", () =>
        {
            var node = html [
                head [
                    title [ text "Test Page" ]
                    meta ["charset", "utf-8"]
                    stylesheet "style.css"
                ]
                body [
                    h1 [ text "Hello World" ]
                ]
            ];
            var rendered = Renderer.render node;
            return rendered.Contains("<html>") && rendered.Contains("<head>") && rendered.Contains("<title>Test Page</title>") && rendered.Contains("<body>") && rendered.Contains("<h1>Hello World</h1>");
        });

        Console.WriteLine($"\n=== HTML DSL Results: {Passed} passed, {Failed} failed ===");
    }

    static void RunTest(string name, Func<bool> test)
    {
        try
        {
            if (test())
            {
                Console.WriteLine($"✅ PASS: {name}");
                Passed++;
            }
            else
            {
                Console.WriteLine($"❌ FAIL: {name}");
                Failed++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ FAIL: {name} — {ex.Message}");
            Failed++;
        }
    }
}
