// SiteConfig.fs
//
// The single configuration record for a Zest site. Every field has a usable
// default, so a site builds correctly with no _config.toml at all.
//
// Directory layout is convention, not configuration: content lives in
// content/ (or the project root when content/ is absent), layouts in
// _layouts/, includes in _includes/, data in _data/, assets in assets/ and
// output in _site/. SitePaths owns those names.
//
// Dependencies: System.Collections.Generic

namespace Zest.Compiler.Model
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

/// <summary>
/// Site configuration loaded from _config.toml, or defaults when the file is
/// absent. Two tables are recognised: [site] and [build].
/// </summary>
type SiteConfig = {
    Title: string
    BaseUrl: string
    Description: string
    /// Content directory, relative to the project root. Empty means "auto":
    /// content/ when it exists, otherwise the project root itself.
    ContentDir: string
    OutputDir: string
    DefaultLayout: string
    PermalinkFormat: string
    DevServerPort: int
    LiveReloadPort: int
    EnableMinification: bool
    EnableAssetFormatting: bool
    EnableHtmlFormatting: bool
    EnableHtmlMinification: bool
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
    // ── Zestucks compatibility mode ──
    /// "strict" = match Nunjucks exactly; "zest" = Zest extensions enabled.
    /// Zest renders with Zestucks either way; only the filter set differs.
    ZestucksCompatibility: string
    // ── File inclusion / exclusion ──
    /// Glob patterns for files to explicitly include (even if excluded by
    /// the default _-prefix / .-prefix rules). Example: [".domains", "tools/*"]
    Include: string list
    /// Glob patterns for files to explicitly exclude from the content pipeline.
    /// Example: ["README.md", "LICENSE", "node_modules/*"]
    Exclude: string list
    // ── Pagination ──
    /// Default number of items per page for paginated listings (e.g. posts
    /// index). A content file overrides it per page via `@paginate` frontmatter.
    PaginationPerPage: int
    // ── Page defaults ──
    /// Default frontmatter overrides applied to files matching glob patterns.
    /// Lower-index entries have higher priority (first match wins).
    PageDefaults: PageDefaults list
    // ── Site parameters ──
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

module SiteConfigDefaults =
    let create () =
        { Title = "My Zest Site"
          BaseUrl = "http://localhost:8080"
          Description = "A site built with Zest SSG"
          // Empty means auto-detect: content/ when present, else project root.
          ContentDir = ""
          OutputDir = "_site"
          DefaultLayout = "default"
          PermalinkFormat = "/:slug/"
          DevServerPort = 8080
          LiveReloadPort = 35729
          EnableMinification = false
          EnableAssetFormatting = false
          EnableHtmlFormatting = false
          EnableHtmlMinification = false
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
          // "zest" mode enables Zest's extended filters/macros on top of the
          // Nunjucks-compatible core.
          ZestucksCompatibility = "zest"
          // Include / exclude — empty by default
          Include = []
          Exclude = []
          // Pagination — 10 items per page by default (configurable).
          PaginationPerPage = 10
          // Page defaults — empty by default
          PageDefaults = []
          // Site parameters — empty until _config.toml [params] is parsed.
          Params = dict [] :> IDictionary<string, obj> }
