// _init.zest.fsx — runs once before every build.
// Inject global site data available to layouts and templates as
// `{{ site.<key> }}`. The bundled footer include reads these links.

// addGlobal "key" value  → available as {{ site.key }}
addGlobal "social_github"  "https://github.com/zest-ssg"
addGlobal "social_twitter" "@zest_ssg"
