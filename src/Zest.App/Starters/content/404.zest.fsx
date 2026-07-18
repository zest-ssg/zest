// @permalink /404.html
// @layout default
// @title 404 — Page Not Found
// @description The page you were looking for could not be found.
//
// Custom 404 page. The dev server and preview server serve this file
// automatically when a route is not found.

open Zest.Dsl

render [
    divC "notfound" [
        h1C "notfound__code" [ text "404" ]
        md """
**Sorry, the page you were looking for doesn’t exist.**

[← Back to home](/)
"""
    ]
]
