+++
title = "Everything Markdown can do here"
layout = "post"
date = 2026-07-21
tags = ["markdown", "writing"]
description = "Headings, tables, code and quotations — the full palette of a post body."
+++

The body of a post is ordinary Markdown. This page is a reference for what the renderer understands, so you can copy the pieces you need instead of guessing.

## Headings are numbered for you

Write `##` and `###` as usual and the stylesheet numbers them, the way a paper numbers its sections. Nested headings continue the parent numbering, so a `###` under the second `##` reads as 2.1, 2.2, and so on. Never type the number yourself.

### A nested heading

This paragraph sits under a level-three heading, which is where long arguments usually live.

## Tables use the three-line rule

A table is a table, but it is styled as a booktabs three-line table: heavy rules top and bottom, one thin rule under the header, and no vertical lines anywhere. Keep the header row short and the columns narrow.

| Element | Written as | Rendered as |
| --- | --- | --- |
| Emphasis | `*text*` | italic serif |
| Strong | `**text**` | bold ink |
| Code | backticks | monospace on cream |
| Link | `[label](url)` | hyperref blue |

## Code stays monospaced

Fenced blocks keep their whitespace and scroll sideways on a narrow screen rather than wrapping, which is what you want when a line is already too long:

```fsharp
let posts =
    sitePages ()
    |> Array.filter (fun p -> p.date <> "")
    |> Array.sortByDescending (fun p -> p.date)
```

Inline code such as `zest build` sits on the same cream fill, so it never competes with the body text.

## Quotations and lists

A quotation keeps its attribution in the same paragraph, indented like a paper block quote:

> Everything should be made as simple as possible, but not simpler.

Lists work as you expect. Use them for enumerations that are genuinely ordered:

1. create a file under `content/posts/`
2. give it a `title` and a `date`
3. run `zest build`

And for the unordered remainder:

- front matter is TOML
- layouts are Zestucks
- styles are ZCSS

## What is deliberately missing

There is no syntax highlighting and no client-side renderer. Code is set in monospace and left alone, because colour belongs to links and cross-references in this design. Mathematical notation is simulated with italics rather than loaded from a script; the next post shows how to lay out a numbered equation by hand.
