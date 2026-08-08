+++
title = "Sites That Speak Many Languages"
description = "Locale files in the theme power every visible string — switch the whole site with one config value."
date = 2026-08-04
tags = ["i18n", "multilingual"]
+++

The oxygen theme ships `en` and `zh` locale files under
`_themes/oxygen/_locales/`. Every visible string — navigation labels,
reading time, pagination — goes through the `t` filter:

```html
{{ 'nav.blog' | t }}
```

## Choosing a language

One value in `_config.toml` flips the entire site:

```toml
[site]
language = "zh"
```

## Beyond two locales

Add a file to `_locales/` (say `de.toml`) and reference it with the
two-argument form — for example from an F# DSL page:

```fsharp
t_lang "nav.blog" "de"
```

Strings that are missing from the active locale fall back to any
locale that has them, then to the key itself — so a partially
translated site still renders.
