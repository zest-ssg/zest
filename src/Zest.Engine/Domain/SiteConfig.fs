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
    /// This allows index.zpage.fsx to be placed at the project root.
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
    /// Template engine: "native" (default, {{ }} placeholders) or "nunjucks"
    TemplateEngine: string
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
          TemplateEngine = "native" }
