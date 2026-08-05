namespace Zest.Engine

open System.Collections.Generic

/// Menu item for navigation.
type MenuItem = {
    Label: string
    Url:   string
    Weight: int
}

/// Taxonomy definition (e.g. tags, categories, series).
type TaxonomyConfig = {
    Name:   string   // singular, e.g. "tag"
    Plural: string   // plural,   e.g. "tags"
}

/// Page defaults: set frontmatter defaults for files matching a glob pattern.
/// Example: path = "posts/*", values = { layout: "post", comments: "true" }
type PageDefaults = {
    /// Glob pattern to match (e.g., "posts/*", "*.md")
    Path: string
    /// Default frontmatter key/value pairs
    Values: Map<string, string>
}

/// Theme configuration from the [theme] table in _config.toml.
/// Controls which theme to load and from where.
type ThemeConfig = {
    /// Theme directory name (e.g. "minima").
    Name: string
    /// Source type: "local" (default), "git", "url", or "path".
    Source: string
    /// Git repository URL (source = "git").
    Git: string
    /// Git branch to checkout (source = "git", default "main").
    Branch: string
    /// Git tag to checkout (source = "git", overrides branch).
    Tag: string
    /// URL to a ZIP archive of the theme (source = "url").
    Url: string
    /// Local filesystem path to the theme (source = "path").
    Path: string
}

/// <summary>
/// Site configuration loaded from _config.toml (or defaults).
/// </summary>
type SiteConfig = {
    Title: string
    BaseUrl: string
    Description: string
    /// Root directory for content discovery.
    /// When set to "." or empty, uses the project root directly.
    /// When not specified, defaults to "content" (implicit content directory).
    /// This allows index.zest.fsx to be placed at the project root.
    RootDir: string
    ContentDir: string
    OutputDir: string
    LayoutsDir: string
    IncludesDir: string
    DataDir: string
    AssetsDir: string
    DefaultLayout: string
    PermalinkFormat: string
    DevServerPort: int
    LiveReloadPort: int
    EnableMinification: bool
    EnableAssetFormatting: bool
    EnableHtmlFormatting: bool
    EnableCacheBusting: bool
    SiteVersion: string
    // Performance
    EnableParallelBuild: bool
    EnableIncrementalBuild: bool
    // Logging
    LogLevel: string        // "Debug" | "Info" | "Warn" | "Error" | "Off"
    LogToFile: bool         // Mirror logs to .zest/logs/zest.log
    LogTimestamps: bool     // Include timestamps in console output
    // Taxonomies & navigation
    Taxonomies: TaxonomyConfig list
    Menus: IDictionary<string, MenuItem list>
    // Author / social (surfaced from _data but can be inlined in _config)
    Author: string
    Language: string
    /// Template engine: "native" (default, {{ }} placeholders) or "nunjucks" (Nunjucks-compatible)
    TemplateEngine: string
    // ── Compatibility flags (enable SSG-specific behaviors) ──
    /// Enable Jekyll-compatible behavior (permalink style, default layout, etc.)
    CompatJekyll: bool
    /// Enable Hexo-compatible behavior.
    CompatHexo: bool
    /// Enable Hugo-compatible behavior.
    CompatHugo: bool
    /// Enable 11ty-compatible behavior (collections API shape, etc.).
    CompatEleventy: bool
    // ── Nunjucks compatibility mode ──
    /// "strict" = match official Nunjucks exactly; "zest" = Zest extensions enabled.
    NunjucksCompatibility: string
    // ── Theme ──
    /// Theme configuration from the [theme] table.
    Theme: ThemeConfig
    // ── File inclusion / exclusion ──
    /// Glob patterns for files to explicitly include (even if excluded by
    /// the default _-prefix / .-prefix rules). Example: [".domains", "tools/*"]
    Include: string list
    /// Glob patterns for files to explicitly exclude from the content pipeline.
    /// Example: ["README.md", "LICENSE", "node_modules/*"]
    Exclude: string list
    // ── Page defaults ──
    /// Default frontmatter overrides applied to files matching glob patterns.
    /// Lower-index entries have higher priority (first match wins).
    PageDefaults: PageDefaults list
    // ── Theme parameters ──
    /// Arbitrary key/value parameters from the `[params]` table in _config.toml.
    /// Surfaced to templates as `site.params.*` (overriding _data/params.toml).
    /// Nested tables (e.g. `[params.colors]`) become nested dictionaries so
    /// `site.params.colors.accent` resolves correctly after context nesting.
    Params: IDictionary<string, obj>
}
with
    /// Create a copy with a different dev server port.
    member this.WithDevServerPort(port: int) =
        { this with DevServerPort = port }

    /// Resolve the effective content directory:
    /// - If RootDir is "." or empty, content is the project root itself
    /// - If RootDir is a specific path, use that as the content root
    /// - Falls back to ContentDir for backward compatibility
    member this.EffectiveContentDir =
        let root = this.RootDir.Trim()
        if System.String.IsNullOrEmpty root || root = "." then
            "."  // Project root
        else
            root

module SiteConfigDefaults =
    let create () =
        { Title = "My Zest Site"
          BaseUrl = "http://localhost:8080"
          Description = "A site built with Zest SSG"
          RootDir = "content"  // Default: implicit content directory
          ContentDir = "./content"
          OutputDir = "./_site"
          LayoutsDir = "./_layouts"
          IncludesDir = "./_includes"
          DataDir = "./_data"
          AssetsDir = "./assets"
          DefaultLayout = "default"
          PermalinkFormat = "/:slug/"
          DevServerPort = 8080
          LiveReloadPort = 35729
          EnableMinification = false
          EnableAssetFormatting = false
          EnableHtmlFormatting = false
          EnableCacheBusting = false
          SiteVersion = "1.0"
          EnableParallelBuild = true
          EnableIncrementalBuild = true
          LogLevel = "Info"
          LogToFile = false
          LogTimestamps = true
          Taxonomies = [ { Name = "tag"; Plural = "tags" }; { Name = "category"; Plural = "categories" } ]
          Menus = dict []
          Author = ""
          Language = "en"
          // Pure annotation for the primary template language (native → .zest.fsx,
          // nunjucks → .njk, liquid → .liquid, ...). No effect on build routing —
          // layouts are routed by file extension in LayoutEngine.
          TemplateEngine = "native"
          // Compat flags default off — users opt in via [compat] table.
          CompatJekyll = false
          CompatHexo = false
          CompatHugo = false
          CompatEleventy = false
          // "zest" mode enables Zest's extended filters/macros on top of Nunjucks.
          NunjucksCompatibility = "zest"
          // Theme defaults to empty — no theme loaded unless explicitly configured.
          Theme = { Name = ""; Source = "local"; Git = ""; Branch = "main"; Tag = ""; Url = ""; Path = "" }
          // Include / exclude — empty by default
          Include = []
          Exclude = []
          // Page defaults — empty by default
          PageDefaults = []
          // Theme parameters — empty until _config.toml [params] is parsed.
          Params = dict [] :> IDictionary<string, obj> }
