+++
title = "Welcome to Zest"
layout = "post"
date = 2026-08-02
tags = ["zest", "writing"]
description = "A short introduction to the Zest static site generator, and to the way this template reads."
+++

Zest is a static site generator where templates are real code. Posts are Markdown, layouts are Zestucks templates, and styles are written in ZCSS — a CSS superset with variables and nesting. This site is the blog template produced by `zest init`.

## The shape of a post

A post is a file under `content/posts/` with TOML front matter between `+++` fences. The directory becomes the URL, so this file is served at `/posts/why-zest/`. Front matter is the only place metadata lives:

- `title` becomes the document head
- `date` orders the listing, the archive and the feeds
- `tags` creates one page per label under `/tags/`
- `description` is quoted in listings and syndication

## Building the site

Run `zest serve` for a development server with live reload, or `zest build` to write the static site into `_site/`. Neither command needs configuration: the engine discovers `content/`, compiles every `.zcss` file under `assets/`, and renders each page through its layout.

The generated output is plain HTML and one stylesheet. There is no client-side JavaScript, no webfont request and no tracking, so the page you read is the page a printer would give you.

> A template should disappear. What remains should be the argument, the table and the citation.

## Where to go next

Read the archive for the full list of posts, or the tag index for the individual labels. The body text is Markdown, but nothing stops you from dropping a `.ztk` or a `.zest.fsx` page next to it when a page needs real logic — the archive on this site is generated that way.
