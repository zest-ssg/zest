namespace Zest.Engine

open System.Collections.Generic

/// <summary>
/// A page produced by a .zpage.fsx template, ready for layout wrapping and output.
/// </summary>
type ContentPage = {
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
    Date: System.DateTime option

    /// Slug derived from filename
    Slug: string

    /// Source file path
    SourcePath: string
}

/// <summary>
/// Default page constructor.
/// </summary>
module ContentPage =
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
