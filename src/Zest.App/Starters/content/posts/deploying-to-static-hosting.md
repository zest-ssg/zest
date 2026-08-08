+++
title = "Deploying to Static Hosting"
description = "The build outputs a self-contained _site/ folder — copy it anywhere and you are live."
date = 2026-08-05
tags = ["deploy", "hosting"]
+++

Because Zest produces a purely static `_site/` directory, deployment is
just a file copy. No application server, no database, no cold starts.

## Any static host works

- **GitHub / GitLab Pages** — commit `_site/` and point the branch at it
- **Netlify / Vercel / Cloudflare Pages** — run `zest build` in the build
  step and serve the output folder
- **Any S3-compatible bucket, Nginx, or even a USB stick** — files are
  files

## The `url` matters

Set your production URL in `_config.toml` so the RSS/Atom feeds and
sitemap generate absolute links:

```toml
[site]
url = "https://your-domain.example"
```

The same config drives `sitemap.xml` (auto-generated from every page)
and the canonical `<link>` tags in the document head.
