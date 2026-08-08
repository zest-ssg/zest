+++
title = "A Performance Checklist"
description = "Incremental builds, parallel evaluation, smart caching — the things that keep Zest fast as the site grows."
date = 2026-08-08
tags = ["performance", "optimization"]
+++

Zest is designed around fast iteration. A few things work in your
favour automatically:

## Incremental builds

Unchanged files are not re-evaluated. `_config.toml` keeps this on:

```toml
[build]
incremental = true
```

## Parallel evaluation

Content files are processed across the thread pool; FSI page scripts
are batched into a single long-lived session instead of one process
per file.

## What you can do

- **Minify** — enable `[build] minify = true` to compress CSS/JS output
- **Cache-bust** — append a version query to assets with
  `[build] cache_busting = true`
- **Keep assets small** — ZCSS compiles with no runtime, so what you
  ship is the CSS you wrote, plus variables and nesting

The preview server pairs all of this with WebSocket live reload, so
edits appear in the browser without a manual rebuild.
