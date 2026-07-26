// _theme.zest.fsx — Duo theme init script
//
// Executed before the site's _init.zest.fsx. Use this to register
// theme-specific filters, globals, and layout data that the site
// script may later override or extend.
//
// Ported from Eleventy Duo (yinkakun/eleventy-duo).
// See: https://github.com/yinkakun/eleventy-duo

// Theme metadata — exposed to templates as {{ site.theme }}.
addGlobal "theme" {|
    name    = "duo"
    version = "1.0.0"
    author  = "Zest SSG"
    desc    = "A clean, minimal blog theme ported from Eleventy Duo."
|}
