namespace Zest.Engine.Template

open System
open System.Collections.Generic

// ============================================================
// ITemplateEngine — Generic template engine abstraction
// ============================================================

/// Error that occurred during template processing.
type TemplateError =
    | ParseError   of message: string * line: int * col: int
    | RuntimeError of message: string * line: int
    | NotFound     of name: string
    | UnknownFilter of name: string
    | UnknownTag    of name: string
    | IncludeLoop   of name: string
with
    override this.ToString() =
        match this with
        | ParseError(msg, line, col)   -> sprintf "[Template Parse] %s at line %d:%d" msg line col
        | RuntimeError(msg, line)      -> sprintf "[Template Runtime] %s at line %d" msg line
        | NotFound(name)               -> sprintf "[Template] Template '%s' not found" name
        | UnknownFilter(name)          -> sprintf "[Template] Unknown filter '%s'" name
        | UnknownTag(name)             -> sprintf "[Template] Unknown tag '%s'" name
        | IncludeLoop(name)            -> sprintf "[Template] Circular include detected: '%s'" name

/// Filter function signature: receives value and string args, returns new value.
type FilterFn = obj -> string list -> obj

/// Tag handler for custom tags.
type TagHandler = {
    TagName: string
    ParseArgs: string -> Result<string list, string>
    Execute: string list -> IDictionary<string, obj> -> Result<string, string>
}

/// Unified template engine interface.
/// Every engine must implement these members.
type ITemplateEngine =
    /// Engine name/identifier.
    abstract Name: string

    /// Render a template string with the given variables.
    abstract Render: templateText: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// Render a template file with the given variables.
    abstract RenderFile: filePath: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// Register a custom filter.
    abstract RegisterFilter: name: string -> fn: FilterFn -> unit

    /// Register a custom tag.
    abstract RegisterTag: handler: TagHandler -> unit

    /// Clear all cached templates.
    abstract ClearCache: unit -> unit
