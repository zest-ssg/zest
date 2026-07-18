// @title Building Sites with F#
// @layout post
// @description How the .zest.fsx page model works, with a small example.
// @date 2026-07-10
// @tags fsharp, tutorial

open Zest.Dsl
open Zest.Dsl.DslCollections

let snippet =
    "let recent = recent_pages 5\nfor p in recent do\n    printfn \"%s — %s\" p.date p.title"

let zcssSnippet =
    "$primary: #4f46e5;\n\n.btn {\n  background: $primary;\n  color: #fff;\n  &:hover { background: darken($primary, 10%); }\n}"

render [
    div [
        p [ text "A Zest page is an F# script. At the top you declare metadata as // @key comments; below, you write F# that emits the HTML body. Because it is F#, you can use the full language." ]

        h2 [ text "Listing pages with F#" ]
        p [ text "The DslCollections module exposes helpers like recent_pages and site_pages. Here is a fragment you could drop into any page:" ]
        pre [ code [ raw snippet ] ]

        p [ text "Within a page you can use list comprehensions, pattern matching, and ordinary .NET libraries — there is no separate templating dialect to learn." ]

        h2 [ text "Styling with ZCSS" ]
        p [ text "Styles are authored in .zcss, a CSS superset that supports variables and nesting:" ]
        pre [ code [ raw zcssSnippet ] ]

        p [ text "On build, main.zcss is compiled to main.css and linked automatically by the layout." ]
    ]
]
