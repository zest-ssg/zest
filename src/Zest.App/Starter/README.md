# Zest Starter

The site produced by `zest init`.

## Layout

```
.
├── _config.toml          # optional: site metadata and build options
├── _layouts/             # Zestucks layouts (.ztk)
├── _includes/            # partials pulled in with {{ include }}
├── assets/css/main.zcss  # ZCSS stylesheet, compiled to CSS at build time
├── content/              # pages (.md, .zest.fsx, .ztk)
└── _site/                # build output
```

Every directory is optional. Delete `_config.toml` and the site still builds —
the title falls back to "My Zest Site" and content is read from `content/`
when it exists, otherwise from the project root.

## How it works

- The article is Markdown with `+++` TOML front matter. `index.md` maps to `/`.
- `post.ztk` is the Zestucks layout: it renders title, author, date and body.
- `main.zcss` is ZCSS, a CSS superset compiled to plain CSS at build time.
- The feeds and sitemap are `.zest.fsx` pages built with `Zest.Markup.Feeds`.

## Workflow

```bash
zest init my-site     # scaffold this starter
zest init --empty     # scaffold only the directory layout
cd my-site
zest serve            # dev server with live reload
zest build            # static output into _site/
zest clean            # remove build output
```
