+++
title = "About this template"
layout = "base"
description = "What ships in this blog template and how to make it yours."
+++

This site is the Zest blog template. It is written as a paper: numbered sections, centred document head, theorem environments, booktabs tables and a hanging-indent reference list. Everything you see is plain markup and one stylesheet — there is no build step beyond `zest build`.

## Who writes here

Replace this paragraph with a short biography. The name, tagline and footer year all come from `[site]` in `_config.toml`, so you only edit them once.

## How to publish

Create a Markdown file under `content/posts/`. The directory decides the URL: `content/posts/my-note.md` becomes `/posts/my-note/`. TOML front matter at the top of the file carries the metadata:

| Key | Meaning |
| --- | --- |
| `title` | Heading of the document head |
| `date` | ISO date, drives ordering and archives |
| `tags` | Plain single-word labels, one per tag page |
| `description` | One-line summary for listings and feeds |
| `layout` | Which file in `_layouts/` renders the page |

Pages without a `date` are left out of the archive, the feeds and the home listing, which is what you want for an About page like this one.

## What ships in the box

The template covers the things a blog needs on day one, so nothing has to be added before the first post:

- a paginated home listing, an archive grouped by year, and a tag index with one page per label
- an RSS feed, an Atom feed and a sitemap, all generated from front matter
- a print stylesheet, so a post reads as a PDF when printed
- responsive rules that keep the column honest from phone to wide desktop

Delete any file you do not want. The site still builds without `_config.toml`, without a layout, and without a single stylesheet.
