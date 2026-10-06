# Zest Starter

The site produced by `zest init`.

## Layout

```
.
├── _config.toml              # optional: site metadata and build options
├── _prebuild.fsx             # optional: pre-build script (global data, hooks)
├── _finalize.fsx             # optional: post-build script (validate, index)
├── _layouts/                 # layouts: .zest.fsx (F#) and .ztk (Zestucks)
│   ├── shell.zest.fsx        # the document: <html>, <head>, header, footer
│   ├── default.zest.fsx      # an ordinary page; the engine's fallback layout
│   ├── post.zest.fsx         # a dated entry: author, date, body, tags
│   ├── tag.ztk               # one tag's listing, looked up before taxonomy.ztk
│   └── tags.ztk              # index of every tag, looked up before terms.ztk
├── assets/css/main.zcss      # the stylesheet the pages link
├── assets/css/_*.zcss        # partials, inlined by main.zcss through @use
│   ├── _tokens.zcss          # design tokens: colours, font stacks, measure
│   ├── _typography.zcss      # reset, headings, links, media, quotes, code
│   ├── _site-chrome.zcss     # header, navigation, main column, footer
│   ├── _article.zcss         # the paper__* block every page body is built from
│   ├── _content-blocks.zcss  # theorem, equation, figure, footnotes
│   ├── _listings.zcss        # home listing, pagination, tag index, archive
│   ├── _buttons.zcss         # buttons and controls
│   └── _responsive.zcss      # responsive, print and motion preferences
├── content/                  # pages (.md, .zest.fsx, .ztk)
└── _site/                    # build output
```

`_config.toml`, `_prebuild.fsx` and `_finalize.fsx` are the only three special
files Zest recognises, and all live at the project root. Everything else is
convention.

Every directory is optional. Delete `_config.toml` and the site still builds —
the title falls back to "My Zest Site" and content is read from `content/`
when it exists, otherwise from the project root.

## Three names the engine reads

These are looked up by name rather than chosen by you, so they cannot be
renamed freely:

- `_layouts/default.zest.fsx` — `default` is the layout every page that
  declares none falls back to. Renaming it needs a matching `default_layout`
  entry in `_config.toml`, or an unconfigured page renders unwrapped.
- `_layouts/tag.ztk` and `_layouts/tags.ztk` — the taxonomy generator resolves
  them by taxonomy name: `tag` before `taxonomy`, `tags` before `terms`. A
  `category` taxonomy has no template here and falls back to the engine's
  built-in one; copy these two to `category.ztk` / `categories.ztk` to style it.
- An underscore in front of a `.zcss` file means partial: the build inlines it
  into the sheet that `@use`s it and never publishes one of its own, which is
  why `_site/assets/css/` holds `main.css` and nothing else.

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
