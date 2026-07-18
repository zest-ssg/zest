// @title Welcome to Zest
// @layout post
// @description A short introduction to the Zest static site generator.
// @date 2026-01-18
// @tags fsharp, ssg

open Zest.Dsl

render [
    div [
        p [ text "Zest is a hybrid F# + C# static site generator where templates are real code. This starter blog was created by running zest init and demonstrates the native template mode." ]
        h2 [ text "What makes Zest different" ]
        p [ text "Pages are .zest.fsx scripts — ordinary F# files evaluated by the build. You can use the full language: list comprehensions, pattern matching, .NET libraries, and your own helpers." ]
        p [ text "Layouts are written in HTML and processed by the Nunjucks engine, so you get includes, variables, and filters with no extra setup." ]
        h2 [ text "Next steps" ]
        ul [
            li [ aHref "/posts/building-sites-with-fsharp/" "Build sites with F#" ]
            li [ aHref "/about/" "Read the About page" ]
        ]
    ]
]
