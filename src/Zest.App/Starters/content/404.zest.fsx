// @permalink /404.html
// @layout default
// @title 404 — Page Not Found
// @description The page you were looking for could not be found.
//
// Custom 404 page. The dev server and preview server serve this file
// automatically when a route is not found.

open Zest.Dsl

render [
    divC "not-found" [
        h1 [ text "404" ]
        p [
            text (t "notfound.message")
            text " "
            aHref "/" (t "notfound.back_home")
            text "."
        ]
    ]
]
