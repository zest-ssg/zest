# Zest Starter

The site produced by `zest init`.

## Layout

```
.
├── _config.toml              # optional: site metadata and build options
├── _prebuild.fsx             # optional: pre-build script (global data, hooks)
├── _finalize.fsx             # optional: post-build script (validate, index)
├── _layouts/                 # layouts: .zest.fsx (F#) and .ztk (Zestucks)
├── assets/css/main.zcss      # the stylesheet the pages link
├── assets/css/_*.zcss        # partials, inlined by main.zcss through @use
├── content/                  # pages (.md, .zest.fsx, .ztk)
└── _site/                    # build output
```

`_config.toml`, `_prebuild.fsx` and `_finalize.fsx` are the only three special
files Zest recognises, and all live at the project root. Everything else is
convention.

Every directory is optional. Delete `_config.toml` and the site still builds —
the title falls back to "My Zest Site" and content is read from `content/`
when it exists, otherwise from the project root.

## How it works

- Posts are Markdown with `+++` TOML front matter; `content/index.zest.fsx`
  renders the paginated home page, eight posts per window. `// @paginate 8`
  hands the page to the pagination generator, which re-runs it once per window
  with `pagination` in scope — currentPage, totalPages, totalItems, perPage,
  prevUrl, nextUrl and items.
- Layouts are `.zest.fsx` templates — real F# evaluated by FSI with `content`,
  `page` and `site` in scope — and are composed through the same DSL as the
  rest of the engine. `default.zest.fsx` renders an ordinary page,
  `post.zest.fsx` adds the metadata only a dated entry has, and
  `shell.zest.fsx` wraps either one in the `<html>` document. A layout chooses
  its parent with a `// @layout name` first line.
- `main.zcss` is ZCSS, a CSS superset compiled to plain CSS at build time. It
  is an index: each `@use` inlines a partial, in cascade order. Partials are
  the underscore-prefixed `.zcss` files beside it, and the build never publishes
  one on its own — `_tokens.css` is not written, only `main.css` is.
- The feeds and sitemap are `.zest.fsx` pages built with `Zest.Markup.Feeds`.
- `_prebuild.fsx` runs before every build to inject dynamic data. It ships
  commented out; uncomment an example to see it take effect.

## Workflow

```bash
zest init my-site     # scaffold this starter
zest init --empty     # scaffold only the directory layout
cd my-site
zest serve            # dev server with live reload
zest build            # static output into _site/
zest clean            # remove build output
```
