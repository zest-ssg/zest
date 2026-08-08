+++
title = "Writing with Markdown"
description = "Every post is a plain Markdown file with TOML front matter — no build config needed."
date = 2026-08-01
tags = ["writing", "markdown"]
+++

Markdown is the default authoring format for posts. Each file starts
with a `+++` TOML block for metadata, then free-form Markdown below:

```toml
+++
title = "Writing with Markdown"
date = 2026-02-10
tags = ["writing", "markdown"]
+++
```

## Front matter is optional

Title, date, tags, description, author — all of it is optional. Zest
derives sensible fallbacks for anything you leave out, and the
`[[defaults]]` table in `_config.toml` can set shared values for whole
directories (for example, routing every `posts/*` file to the `post`
layout).

## What works out of the box

- **Standard Markdown** — headings, lists, links, blockquotes, code
- **Fenced code blocks** with language tags
- **Raw HTML** passes through untouched when you need it

The rendered HTML lands in `{{ content | safe }}` inside the post
layout, next to reading time, tags and prev/next navigation.
