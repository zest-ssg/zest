// @title Features
// @layout default
// @description Zest SSG features — inline JS, JSON injection, new components, and syntax sugar.

open Zest.Dsl

// Feature cards from site data
let featureCards =
    match Context.get().SiteData.TryGetValue "features" with
    | true, je ->
        card_grid (je.EnumerateArray() |> Seq.toList) (fun item ->
            let mutable v = Unchecked.defaultof<System.Text.Json.JsonElement>
            let title = if item.TryGetProperty("title", &v) then v.GetString() else ""
            let desc  = if item.TryGetProperty("desc", &v) then v.GetString() else ""
            card [
                h3 [ text title ]
                p [ text desc ]
            ])
    | _ -> empty

render [
    divC "features-page" [
        sectionC "hero" [
            h1C "hero__title" [ text "Zest Features" ]
            pC "hero__lead" [
                text "A tour of the DSL capabilities: inline JavaScript, type-safe "
                text "data injection, semantic components, and syntax sugar."
            ]
        ]

        sectionC "feature-grid" [
            h2 [ text "Capabilities" ]
            featureCards
        ]

        // ── Inline JavaScript ──
        sectionC "demo-section demo-js" [
            h2 [ text "Inline JavaScript" ]
            p [
                text "Click the button below — the handler is embedded via "
                code [ text "js \"\"\"...\"\"\"" ]
                text "."
            ]
            buttonC "btn" [ text "Click me" ]
            js """
                document.querySelector('.demo-js .btn').addEventListener('click', () => {
                    alert('Hello from inline F# DSL JS!')
                })
            """
        ]

        // ── JSON data injection ──
        sectionC "demo-section demo-json" [
            h2 [ text "JSON Data Injection" ]
            p [
                text "The config below is passed from F# to client JS via "
                code [ text "jsonBlock" ]
                text "."
            ]
            preC "code-block" [
                code [ text "// Client-side: console.log(window.__PAGE_DATA__)" ]
            ]
            jsonBlock "__PAGE_DATA__" {|
                theme = "light"
                version = "1.0"
                postCount = page_count ()
            |}
        ]

        // ── New components ──
        sectionC "demo-section demo-components" [
            h2 [ text "New Components" ]

            h3 [ text "Breadcrumb" ]
            breadcrumb [("Home", "/"); ("Features", "/features/")]

            h3 [ text "Tag badges" ]
            tagBadges "/tags/" ["fsharp"; "ssg"; "zcss"; "dsl"]

            h3 [ text "Progress bar" ]
            progressBar 75 "Build progress"

            h3 [ text "Icons (icon-font ready)" ]
            icon "star"
            icon "heart"
            icon "check"
        ]

        // ── Syntax sugar ──
        sectionC "demo-section demo-sugar" [
            h2 [ text "Syntax Sugar" ]
            p [
                text (pluralize (page_count ()) "page")
                text " published."
            ]
            p [
                text "Tags: "
                text (all_tags () |> Array.toList |> intersperse ", ")
            ]
            p [
                text "Titleised slug: "
                text (titleize "my-cool-blog-post")
            ]
        ]

        // ── Inline Markdown with dedent ──
        sectionC "demo-section demo-md" [
            h2 [ text "Inline Markdown (dedent)" ]
            mdDedent """
                The `mdDedent` function strips common indentation so you can
                keep F# source formatting without breaking Markdown headings.

                - Lists work
                - **Bold** and *italic* work
                - `code` works
            """
        ]
    ]
]
