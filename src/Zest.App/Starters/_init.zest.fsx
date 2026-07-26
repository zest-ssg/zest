// _init.zest.fsx — runs once before every build.
//
// Inject global site data, register custom filters, and schedule
// post-build tasks. All data exposed as `{{ site.<key> }}` in templates.

// ── Site metadata ──────────────────────────────────
// Social links displayed in the footer.
addGlobal "socials" [|
    {| label = "GitHub";  url = "https://github.com/zest-ssg";  icon = "github" |}
    {| label = "Twitter"; url = "https://twitter.com/zest_ssg"; icon = "twitter" |}
    {| label = "RSS";     url = "/rss.xml";                      icon = "rss" |}
|]

// Build timestamp for cache-busting query strings.
addGlobal "build_time" (System.DateTime.UtcNow.ToString("yyyyMMddHHmmss"))

// ── Features list (used by the features demo page) ─
addGlobal "features" [|
    {| title = "F# DSL";       desc = "Type-safe HTML generation with full IDE support" |}
    {| title = "ZCSS";         desc = "SCSS-like preprocessor with variables, mixins, color functions" |}
    {| title = "Multi-engine"; desc = "Nunjucks, Handlebars, HAML, Pug — all auto-converted" |}
    {| title = "Inline JS";    desc = "js \"\"\"...\"\"\" blocks with automatic dedent" |}
    {| title = "JSON inject";  desc = "jsonBlock for type-safe F# to JS data passing" |}
    {| title = "Live reload";  desc = "Dev server with WebSocket hot reload" |}
|]

// ── After-build hooks ──────────────────────────────
// These commands run after the build completes (and after CSS/JS
// minification), so they have access to the final output files.
// Uncomment to generate extra artefacts:
//
//   afterBuild "pandoc" "content/friends.md -s -o _site/friends.html"
//   afterBuild "python" "scripts/stats.py"
