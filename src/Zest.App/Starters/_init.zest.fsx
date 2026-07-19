// _init.zest.fsx — runs once before every build.
// Inject global site data available to layouts and templates as
// `{{ site.<key> }}`.
//
// After the §1.2/1.3 engine fix, TOML arrays/tables from _data/*.toml are
// preserved as native .NET types, so Nunjucks can iterate them directly.
// The _data/nav.toml file is auto-loaded as `site.nav.items`.

// Social links — exposed as a structured array, iterable in Nunjucks:
//   {% for s in site.socials %}<a href="{{ s.url }}">{{ s.label }}</a>{% endfor %}
addGlobal "socials" [|
    {| label = "GitHub";  url = "https://github.com/zest-ssg";  icon = "github" |}
    {| label = "Twitter"; url = "https://twitter.com/zest_ssg"; icon = "twitter" |}
    {| label = "RSS";     url = "/rss.xml";                      icon = "rss" |}
|]

// Build timestamp for cache-busting query strings on static assets.
addGlobal "build_time" (System.DateTime.UtcNow.ToString("yyyyMMddHHmmss"))

// Site features list — used by the features demo page.
addGlobal "features" [|
    {| title = "F# DSL";       desc = "Type-safe HTML generation with full IDE support" |}
    {| title = "ZCSS";         desc = "SCSS-like preprocessor with variables, mixins, color functions" |}
    {| title = "Multi-engine"; desc = "Nunjucks, Handlebars, HAML, Pug — all auto-converted" |}
    {| title = "Inline JS";    desc = "js \"\"\"...\"\"\" blocks with automatic dedent" |}
    {| title = "JSON inject";  desc = "jsonBlock for type-safe F# → JS data passing" |}
    {| title = "Live reload";  desc = "Dev server with WebSocket hot reload" |}
|]
