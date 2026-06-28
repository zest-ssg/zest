namespace Zest.Engine

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
