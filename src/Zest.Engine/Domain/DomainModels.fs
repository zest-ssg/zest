namespace Zest.Engine

open System
open System.Collections.Generic

/// <summary>
/// Represents an HTML content tree node.
/// Can be a plain text node, a tagged element, or a fragment (list of nodes).
/// </summary>
type HtmlNode =
    | Text of string
    | Element of tag: string * attributes: (string * string) list * children: HtmlNode list
    | Fragment of HtmlNode list
    | Raw of string  // Raw HTML that won't be escaped
    | Conditional of condition: bool * node: HtmlNode
    | Repeat of items: HtmlNode list

/// <summary>
/// A page produced by a .zest.fsx template, ready for layout wrapping and output.
/// </summary>
type Page = {
    /// URL path, e.g. "/" or "/posts/hello-world/"
    Url: string

    /// Relative output path from output root, e.g. "index.html" or "posts/hello-world/index.html"
    OutputPath: string

    /// Layout name (without extension), e.g. "default"
    Layout: string option

    /// Page title
    Title: string

    /// The rendered HTML content (inner body, not including layout)
    Content: string

    /// Raw content nodes before rendering (for DSL use)
    ContentNodes: HtmlNode list

    /// Front-matter-style metadata
    Data: IDictionary<string, obj>

    /// Custom permalink override
    Permalink: string option

    /// Tags for collection classification
    Tags: string list

    /// Publish date
    Date: DateTime option

    /// Slug derived from filename
    Slug: string

    /// Source file path
    SourcePath: string
}

/// <summary>
/// Default page constructor.
/// </summary>
module Page =
    let empty =
        { Url = ""
          OutputPath = ""
          Layout = None
          Title = ""
          Content = ""
          ContentNodes = []
          Data = dict []
          Permalink = None
          Tags = []
          Date = None
          Slug = ""
          SourcePath = "" }

/// <summary>
/// A named collection of pages (like 11ty collections).
/// </summary>
type Collection = {
    Name: string
    Pages: Page list
    Type: CollectionType
}
and CollectionType =
    | Directory
    | Tag
    | Category

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
    // Taxonomies & navigation
    Taxonomies: TaxonomyConfig list
    Menus: IDictionary<string, MenuItem list>
    // Author / social (surfaced from _data but can be inlined in _config)
    Author: string
    Language: string
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
          Taxonomies = [ { Name = "tag"; Plural = "tags" }; { Name = "category"; Plural = "categories" } ]
          Menus = dict []
          Author = ""
          Language = "en" }

/// <summary>
/// Result of a build operation.
/// </summary>
type BuildResult = {
    TotalPages: int
    ProcessedPages: int
    CachedPages: int
    AssetsCopied: int
    AssetsMinified: int
    DurationMs: int64
    Errors: string list
}
with
    member this.Success = this.Errors.IsEmpty

/// <summary>
/// Globals injected into the F# script evaluation context.
/// </summary>
type ScriptGlobals = {
    Site: IDictionary<string, obj>
    Collections: IDictionary<string, obj>
    Data: IDictionary<string, obj>
    Page: Page option
    Content: string option
}
