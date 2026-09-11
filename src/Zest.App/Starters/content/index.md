+++
title = "Welcome to Zest"
layout = "post"
description = "A short introduction to the Zest static site generator."
date = 2026-08-02
+++

Zest is a static site generator where templates are real code. This site is the minimal starter produced by `zest init`.

## Writing

Articles are Markdown files with TOML front matter. Layouts are HTML processed by the Nunjucks engine, and styles are written in ZCSS, a CSS superset with variables and nesting.

## Building

Run `zest serve` for a live-reloading development server, or `zest build` to write the static site into `_site/`.
