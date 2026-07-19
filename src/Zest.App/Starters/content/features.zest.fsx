// @title Features
// @layout default
// @description Zest SSG features — inline JS, JSON injection, new components, and syntax sugar.

open Zest.Dsl
open Zest.Dsl.DslCollections
open Zest.Dsl.DslComponents
open Zest.Dsl.DslSugar

// Feature cards defined inline (in a real site you might load these from
// _data/*.toml, which is now preserved as native arrays after the §1.2/1.3 fix).
let features = [
    ("F# DSL",       "Type-safe HTML generation with full IDE support")
    ("ZCSS",         "SCSS-like preprocessor with variables, mixins, color functions")
    ("Multi-engine", "Nunjucks, Handlebars, HAML, Pug — all auto-converted")
    ("Inline JS",    "js \"\"\"...\"\"\" blocks with automatic dedent")
    ("JSON inject",  "jsonBlock for type-safe F# to JS data passing")
    ("Live reload",  "Dev server with WebSocket hot reload")
]

let featureGrid =
    card_grid features (fun (title, desc) ->
        card [
            h3 [ text title ]
            p [ text desc ]
        ])

render [
    divC "features-page" [
        sectionC "hero" [
            h1 [ text "Zest Features" ]
            pC "lead" [
                text "A tour of the DSL capabilities: inline JavaScript, type-safe "
                text "data injection, semantic components, and syntax sugar."
            ]
        ]

        sectionC "feature-grid" [
            h2 [ text "Capabilities" ]
            featureGrid
        ]

        // ── Inline JavaScript (L2) ──
        // The js """...""" block mirrors md: raw JS wrapped in <script>,
        // with automatic dedent so the body can follow F# indentation.
        sectionC "demo-js" [
            h2 [ text "Inline JavaScript" ]
            p [ text "Click the button below — the handler is embedded via "
                code [ text "js \"\"\"...\"\"\"" ]
                text "." ]
            buttonC "btn btn-primary" [ text "Click me" ]
            js """
                document.querySelector('.demo-js .btn').addEventListener('click', () => {
                    alert('Hello from inline F# DSL JS!')
                })
            """
        ]

        // ── JSON data injection (L3) ──
        // jsonBlock serialises F# data to JSON and injects it as
        // window.__PAGE_DATA__. No string concatenation, no XSS risk.
        sectionC "demo-json" [
            h2 [ text "JSON Data Injection" ]
            p [ text "The config below is passed from F# to client JS via "
                code [ text "jsonBlock" ]
                text "." ]
            preC "code-block" [ text "// Client-side: console.log(window.__PAGE_DATA__)" ]
            jsonBlock "__PAGE_DATA__" {|
                theme = "light"
                version = "1.0"
                postCount = page_count ()
            |}
        ]

        // ── New components: breadcrumb, tagBadges, progressBar ──
        sectionC "demo-components" [
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

        // ── Syntax sugar: intersperse, pluralize, titleize ──
        sectionC "demo-sugar" [
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

        // ── Inline Markdown with dedent (3.2 fix) ──
        sectionC "demo-md" [
            h2 [ text "Inline Markdown (dedent)" ]
            mdDedent """
                ## Why dedent?

                The `mdDedent` function strips common indentation so you can
                keep F# source formatting without breaking Markdown headings.

                - Lists work
                - **Bold** and *italic* work
                - `code` works
            """
        ]
    ]
]
