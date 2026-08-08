+++
title = "Styling with ZCSS"
description = "A SCSS-like preprocessor built into the engine: variables, nesting and color functions, with zero tooling."
date = 2026-08-03
tags = ["zcss", "styling"]
+++

ZCSS is the theme's stylesheet language — a superset of CSS that
compiles to plain `.css` at build time. No npm, no PostCSS, no build
step to babysit.

## Variables

```zcss
let accent     = #0074d9
let text-base  = 1rem
```

## Nesting and color functions

```zcss
.button [
  background: accent;

  &:hover [
    background: darken(accent, 10%);
  ]
]
```

## Theme colors from `_config.toml`

The oxygen theme exposes its palette as CSS custom properties with
fallbacks, so you can restyle the whole site from config:

```toml
[params.colors]
accent = "#0074d9"
background = "#fffffe"
color = "#292929"
```

Every override is optional — unset values fall back to the theme's
built-in defaults.
