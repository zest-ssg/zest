// ContentPage.fs
//
// The page record produced by the content pipeline, plus the HTML node tree a
// page body is built from. The two sit in one file because the node tree is
// only ever read as part of a page.
//
// Dependencies: System, System.Collections.Generic

namespace Zest.Compiler.Model
open System
open System.Collections.Generic

/// <summary>
/// Represents an HTML content tree node.
/// </summary>
/// <remarks>
/// The tree is deliberately small. A node is either text, an element, a list of
/// children (<c>Fragment</c>), pre-escaped markup (<c>Raw</c>), or a node that
/// may be dropped at render time (<c>Conditional</c>).
///
/// There is no separate "repeat" case: repeating is <c>Fragment</c> over a
/// mapped list, which is what every helper already produced.
/// </remarks>
type HtmlNode =
    | Text of string
    | Element of tag: string * attributes: (string * string) list * children: HtmlNode list
    | Fragment of HtmlNode list
    /// Pre-escaped markup. Only reachable through <c>RawNode</c>-style helpers
    /// in the DSL, and only for markup the DSL itself produced.
    | Raw of string
    /// Rendered only when the condition is true. The condition is evaluated
    /// where the node is constructed (the DSL is eager), which keeps the
    /// renderer free of closures.
    | Conditional of condition: bool * node: HtmlNode

/// <summary>
/// A page produced by a .zest.fsx template, ready for layout wrapping and output.
/// </summary>
type ContentPage = {
    /// URL path, e.g. "/" or "/posts/hello-world/"
    Url: string

    /// Relative output path from output root, e.g. "index.html" or "posts/hello-world/index.html".
    /// Always uses forward slashes, on every platform — see SitePaths.normalizeOutputRel.
    OutputPath: string

    /// Layout name (without extension), e.g. "default"
    Layout: string option

    /// Page title
    Title: string

    /// The rendered HTML content (inner body, not including layout)
    Content: string

    /// Raw content nodes before rendering (for DSL use)
    ContentNodes: HtmlNode list

    /// Front-matter-style metadata.
    ///
    /// Read-only on purpose: pages are handed to parallel renderers, and a
    /// writable dictionary shared between them is a cross-page state leak.
    /// Producers build a private Dictionary and publish it here.
    Data: IReadOnlyDictionary<string, obj>

    /// Custom permalink override
    Permalink: string option

    /// Tags for collection classification
    Tags: string list

    /// Categories — a coarser, separate classification from Tags.
    Categories: string list

    /// Publish date
    Date: System.DateTime option

    /// Last modification date from the "updated" front matter key.
    Updated: System.DateTime option

    /// Draft status — when true, the page is excluded from production builds
    Draft: bool

    /// Slug derived from filename
    Slug: string

    /// Source file path
    SourcePath: string
}

/// <summary>
/// Default page constructor.
/// </summary>
module ContentPage =
    /// A page with every field at its zero value.
    ///
    /// Safe to share: `Data` is an immutable read-only dictionary, so the value
    /// can be used as the base of `{ ContentPage.empty with ... }` from any
    /// number of threads without them seeing each other's writes.
    let empty =
        { Url = ""
          OutputPath = ""
          Layout = None
          Title = ""
          Content = ""
          ContentNodes = []
          Data = readOnlyDict []
          Permalink = None
          Tags = []
          Categories = []
          Date = None
          Updated = None
          Draft = false
          Slug = ""
          SourcePath = "" }
