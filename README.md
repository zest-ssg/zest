# Zest

**A quiet, fast static site generator for F#.**

Zest turns Markdown and F# source into a clean static site. No runtime, no
build toolchain, no npm dependency tree — just `dotnet`, your content, and a
template engine. It is built for people who like their tools to stay out of
the way: pages are plain files, styles are plain ZCSS, and the output is
plain HTML.

---

## Why Zest?

- **Fast.** Compiles pages in parallel, caches aggressively, and ships a dev
  server with live reload. Big sites stay snappy because the pipeline is
  embarrassingly parallel by design.
- **F# everywhere.** Pages can be written as real F# programs using a
  type-safe HTML DSL — loops, conditionals, functions and data, with no
  template-language hacks. Markdown is there when you just want to write.
- **No lock-in.** The engine is template-engine agnostic: Nunjucks is the
  default, Handlebars is available as an alternative, and output is static
  HTML you can host anywhere.
- **Quiet by default.** The bundled starter theme has no animation, no
  shadows, no hover theatrics — typography and whitespace carry the page.
- **A single binary.** One dotnet CLI tool does init, build, serve and clean.

---

## Quick start

```bash
dotnet tool install --global Zest
zest init my-blog
cd my-blog
zest serve          # http://localhost:8080 with live reload
zest build          # static output in _site/
```

## Writing content

### Markdown posts

```markdown
+++
title = "Hello, world"
date = 2026-01-15
tags = ["zest", "fsharp"]
layout = "post"
+++

This is a blog post.
```

### Pages as F# (`.zest.fsx`)

```fsharp
// @title About
// @layout default

render [
    h1 [ text "About this site" ]
    p [ text "Written in F#, rendered as HTML." ]
]
```

### Styles as ZCSS (`.zcss`)

```zcss
let accent = #3c6a5a
let measure = 44r

.post__title, .post__meta [
  font-family: font-display;
  color: ink;
  a [
    color: accent
  ]
]
```

ZCSS is a small SCSS-like preprocessor: variables, nesting, comma-grouped
selectors, color functions (`darken()`, `lighten()`), and `r` as a shorthand
for `rem` — compiled to plain CSS by the engine itself. Rule bodies use
F#-style `[ ]` blocks.

### Data

`_init.zest.fsx` runs before every build and injects global data:

```fsharp
addGlobal "socials" [
    {| label = "GitHub"; url = "https://github.com/zest"; icon = "github" |}
]
```

Templates read it as `{{ site.socials }}`.

---

## Templates

Layouts and partials are plain HTML processed by a template engine. Nunjucks
is the default; Handlebars is available for projects that prefer it.

```html
<!DOCTYPE html>
<html lang="{{ site.language }}">
<head>
  <meta charset="utf-8">
  <title>{{ site.title }}</title>
  <link rel="stylesheet" href="/assets/css/main.css">
</head>
<body>
  {{ include header.html }}
  <main>
    {{ content | safe }}
  </main>
  {{ include footer.html }}
</body>
</html>
```

Supported Nunjucks constructs include `{{ include }}`, `{{ content }}`,
`{% if %}` / `{% for %}`, `{% assign %}`, filters (`| t`, `| date`,
`| readingTime`) and i18n strings from `_locales/*.toml`.

> Note: the `template_engine` field in `_config.toml` is declarative only —
> it documents which engine the templates were written for. Layout routing is
> decided by file extension, so a project may mix Nunjucks and Handlebars
> templates freely.

---

## Project layout

```
.
├── zest.toml            # CLI configuration
├── _config.toml         # site metadata and build options
├── _init.zest.fsx       # pre-build script (global data, hooks)
├── _data/               # global data (nav.toml, …)
├── _themes/<name>/      # self-contained themes
│   ├── _theme.toml      # theme manifest
│   ├── _layouts/        # Nunjucks/Handlebars layouts
│   ├── _includes/       # partials
│   ├── _locales/        # i18n string tables
│   └── assets/          # styles (ZCSS), images, fonts
├── content/             # pages (.zest.fsx) and posts (.md)
└── _site/               # build output
```

## Commands

| Command      | Description                           |
|--------------|---------------------------------------|
| `zest init`  | Scaffold a new site from a starter    |
| `zest build` | Build the site into `_site/`          |
| `zest serve` | Dev server with live reload           |
| `zest clean` | Remove build output                   |

---

## Architecture

| Project      | Role                                                        |
|--------------|-------------------------------------------------------------|
| `Zest.App`   | CLI, scaffolding, dev server, embedded starter templates    |
| `Zest.Engine`| Build pipeline: content, layouts, ZCSS, Zcss, data, feeds   |
| `Zest.Dsl`   | Type-safe HTML DSL for `.zest.fsx` pages                    |
| `Zest.Infra` | Shared infrastructure (files, logging, hashing)             |

## Design philosophy

1. **Content is code, code is content.** The F# DSL and the template engine
   share one data model, so nothing is lost at the boundary.
2. **No magic.** Every transformation is a plain pipeline stage you can read
   in the source. No hidden runtime, no implied dependencies.
3. **Speed is a feature.** Parallel compilation, minimal allocations, and
   caching are part of the core design, not an afterthought.
4. **The output is the deliverable.** Static HTML, no JavaScript required,
   host it anywhere.

---

## License

Apache License 2.0. See [LICENSE](LICENSE).
