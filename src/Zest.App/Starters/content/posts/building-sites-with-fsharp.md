+++
title = "Building Sites with F#"
layout = "post"
description = "How the .zest.fsx page model works, with a small example."
date = 2026-07-10
tags = ["fsharp", "tutorial"]
+++

A Zest page is an F# script. At the top you declare metadata as
`// @key` comments; below, you write F# that emits the HTML body.
Because it is F#, you can use the full language.

## Listing pages with F#

The `DslCollections` module exposes helpers like `recent_pages`
and `site_pages`. Here is a fragment you could drop into any page:

```fsharp
let recent = recent_pages 5
for p in recent do
    printfn "%s — %s" p.date p.title
```

Within a page you can use list comprehensions, pattern matching,
and ordinary .NET libraries — there is no separate templating
dialect to learn.

## Styling with ZCSS

Styles are authored in `.zcss`, a CSS superset that supports
variables, nesting, and color functions:

```zcss
$primary: #4f46e5;

.btn {
  background: $primary;
  color: #fff;
  &:hover { background: darken($primary, 10%); }
}
```

On build, `main.zcss` is compiled to `main.css` and linked
automatically by the layout.
