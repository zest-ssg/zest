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
open System
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

/// Console log verbosity. Parsed from the free-form `log_level` config key so
/// a typo is reported instead of silently falling back to the default.
///
/// Qualified access is required because the `Error` case would otherwise
/// shadow `Result.Error` for every file that opens this namespace.
[<RequireQualifiedAccess>]
type LogLevel =
    | Debug
    | Info
    | Warn
    | Error
    | Off
with
    /// The spelling accepted in `_config.toml` (`log_level = "Info"`).
    member this.Name =
        match this with
        | Debug -> "Debug" | Info -> "Info" | Warn -> "Warn" | Error -> "Error" | Off -> "Off"

module LogLevel =
    /// Recognised spellings, for validation messages.
    let names = [ "Debug"; "Info"; "Warn"; "Error"; "Off" ]

    /// Case-insensitive parse. Returns None for an unrecognised spelling so
    /// the caller can report the typo rather than silently defaulting.
    let tryParse (text: string) =
        match (if isNull text then "" else text.Trim().ToLowerInvariant()) with
        | "debug" -> Some LogLevel.Debug
        | "info"  -> Some LogLevel.Info
        | "warn"  -> Some LogLevel.Warn
        | "error" -> Some LogLevel.Error
        | "off"   -> Some LogLevel.Off
        | _       -> None

/// Zestucks filter-set mode. The engine is the same either way; only the
/// filter/macro surface differs.
[<RequireQualifiedAccess>]
type ZestucksMode =
    /// Match Nunjucks exactly; Zest's extended filters are unavailable.
    | Strict
    /// Nunjucks-compatible core plus Zest's extended filters/macros.
    | Zest

module ZestucksMode =
    let names = [ "strict"; "zest" ]

    let tryParse (text: string) =
        match (if isNull text then "" else text.Trim().ToLowerInvariant()) with
        | "strict" -> Some ZestucksMode.Strict
        | "zest"   -> Some ZestucksMode.Zest
        | _        -> None

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
    // Output shaping (HTML/CSS/JS pretty-printing and minification) is not a
    // configuration concern: it is post-build work the author performs in
    // _finalize.fsx with formatHtml / minifyHtml / rewriteFiles.
    EnableCacheBusting: bool
    SiteVersion: string
    // Performance
    EnableParallelBuild: bool
    EnableIncrementalBuild: bool
    // Build hooks
    /// Run the _finalize.fsx post-build hook even when the main build already
    /// reported errors. Default true, so a "validate what was written" hook
    /// still runs on a failed build.
    FinalizeOnError: bool
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

    /// Create a copy with incremental builds enabled or disabled.
    /// `zest build --no-incremental` uses this to force a full rebuild.
    member this.WithIncrementalBuild(enabled: bool) =
        { this with EnableIncrementalBuild = enabled }

    /// Parsed console log verbosity, or None when `LogLevel` is misspelled.
    member this.ParsedLogLevel : LogLevel option = LogLevel.tryParse this.LogLevel

    /// Parsed Zestucks filter-set mode, or None when misspelled.
    member this.ParsedZestucksMode : ZestucksMode option = ZestucksMode.tryParse this.ZestucksCompatibility

    /// True only when `ZestucksCompatibility` is exactly "strict" (or any
    /// spelling resolving to Strict). Unrecognised values fall back to Zest.
    member this.IsStrictZestucks = this.ParsedZestucksMode = Some ZestucksMode.Strict

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
          EnableCacheBusting = false
          SiteVersion = "1.0"
          EnableParallelBuild = true
          EnableIncrementalBuild = true
          FinalizeOnError = true
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

/// Validation and normalisation applied to a freshly loaded SiteConfig.
///
/// The config file is user input, and a SiteConfig can also be constructed
/// directly by a library caller, so the model validates itself rather than
/// trusting whoever built the record. BuildRunner calls `validate` once at the
/// start of a build and reports every problem at the same time, instead of
/// failing on the first one with an unhelpful exception far from the source.
module SiteConfigValidation =

    let private isPort (n: int) = n >= 1 && n <= 65535

    /// Every reason the configuration cannot be used as written. An empty list
    /// means the config is valid. Messages name the offending key so the user
    /// can find it in `_config.toml`.
    let validate (config: SiteConfig) : string list =
        [ // Free-form enum keys: a typo must be reported, not silently defaulted.
          if config.ParsedLogLevel.IsNone then
              yield sprintf "Config key [site] log_level '%s' is not one of: %s"
                        config.LogLevel (String.concat ", " LogLevel.names)
          if config.ParsedZestucksMode.IsNone then
              yield sprintf "Config key [template.zestucks] compatibility '%s' is not one of: %s"
                        config.ZestucksCompatibility (String.concat ", " ZestucksMode.names)
          // Numeric ranges the record itself cannot express.
          if not (isPort config.DevServerPort) then
              yield sprintf "Config key [site] dev_server_port must be between 1 and 65535 (got %d)" config.DevServerPort
          if not (isPort config.LiveReloadPort) then
              yield sprintf "Config key [site] live_reload_port must be between 1 and 65535 (got %d)" config.LiveReloadPort
          if config.PaginationPerPage < 1 then
              yield sprintf "Config key [pagination] per_page must be at least 1 (got %d)" config.PaginationPerPage
          if String.IsNullOrWhiteSpace config.PermalinkFormat then
              yield "Config key [site] permalink_format must not be empty"
          if String.IsNullOrWhiteSpace config.OutputDir then
              yield "Config key [build] output must not be empty"
          // A taxonomy with an empty name would generate URLs under a blank
          // segment and crash slug generation.
          for tax in config.Taxonomies do
              if String.IsNullOrWhiteSpace tax.Name then
                  yield "Config key [[taxonomies]] name must not be empty"
              if String.IsNullOrWhiteSpace tax.Plural then
                  yield sprintf "Config key [[taxonomies]] plural must not be empty (name = '%s')" tax.Name
          for defaults in config.PageDefaults do
              if String.IsNullOrWhiteSpace defaults.Path then
                  yield "Config key [[defaults]] path must not be empty" ]

    /// Clamp values that have a valid range but are cheap to correct, so a
    /// nonsense-but-harmless number cannot abort an otherwise fine build.
    /// Anything `validate` reports is left untouched.
    let normalize (config: SiteConfig) : SiteConfig =
        { config with
            PaginationPerPage = max 1 config.PaginationPerPage }
