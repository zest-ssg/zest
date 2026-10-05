namespace Zest.Engine.Template

open System
open System.Collections.Generic
open System.IO

// ============================================================
// ITemplateEngine — Generic template engine abstraction
// ============================================================
// Defines the single surface through which the build pipeline renders any
// template language. Engines are obtained via TemplateManager and must be
// usable concurrently: Render is called from parallel page workers, so an
// engine must not share mutable per-render state between calls.
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

/// <summary>
/// A template filter: transforms an incoming value into an output value.
/// </summary>
/// <param name="value">The value produced by the expression preceding the filter.</param>
/// <param name="args">String forms of any filter arguments, in source order.</param>
/// <returns>The transformed value. Returning the input unchanged is the
/// conventional "unknown filter" fallback so a misnamed filter degrades to a
/// passthrough instead of aborting the whole render.</returns>
type FilterFn = obj -> string list -> obj

/// <summary>
/// A custom block tag or helper registered on an engine.
/// </summary>
/// <remarks>
/// The Zealucks engine dispatches only its built-in tag set and ignores
/// registered tags, so registering a tag is a no-op rather than an error.
/// </remarks>
type TagHandler = {
    /// Name that appears in the template after the opening delimiter.
    TagName: string
    /// Parses the tag's argument text into positional arguments.
    ParseArgs: string -> Result<string list, string>
    /// Executes the tag against the current context and returns emitted output.
    Execute: string list -> IDictionary<string, obj> -> Result<string, string>
}

/// <summary>
/// Unified template engine interface implemented by every engine.
/// </summary>
/// <remarks>
/// Render/RenderFile are pure with respect to the supplied variables: they must
/// not mutate the caller's dictionary. RegisterFilter/RegisterTag mutate only
/// engine-owned registries and are safe to call once during setup.
/// </remarks>
type ITemplateEngine =
    /// <summary>Engine name/identifier. Always "Zealucks".</summary>
    abstract Name: string

    /// <summary>
    /// Renders a template string with the supplied variables.
    /// </summary>
    /// <param name="templateText">Raw template source.</param>
    /// <param name="variables">Flat or nested context bag for interpolation.</param>
    /// <returns>Rendered output, or a typed error with a source location.</returns>
    abstract Render: templateText: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// <summary>
    /// Renders a template file, caching by last-write time where supported.
    /// </summary>
    /// <param name="filePath">Absolute path to the template file.</param>
    /// <param name="variables">Context bag for interpolation.</param>
    /// <returns>Rendered output, or NotFound for a missing file.</returns>
    abstract RenderFile: filePath: string -> variables: IDictionary<string, obj> -> Result<string, TemplateError>

    /// <summary>
    /// Registers a custom filter under a name usable as `| name` in templates.
    /// </summary>
    abstract RegisterFilter: name: string -> fn: FilterFn -> unit

    /// <summary>
    /// Registers a custom tag/helper. Best-effort: engines without tag
    /// extensibility silently ignore the registration.
    /// </summary>
    abstract RegisterTag: handler: TagHandler -> unit

    /// <summary>Clears every engine-owned cache (templates, tokens, parsed ASTs).</summary>
    abstract ClearCache: unit -> unit

// ============================================================
// TemplateUtils — Shared helpers for template loading
// ============================================================

/// Utilities shared by the Zealucks engine and the template manager. Kept
/// here (the first Template/ file) so both can reference it without extra
/// coupling.
module TemplateUtils =

    /// Resolve a template-relative path against the working directory and
    /// reject paths that escape it (path-traversal guard for {% include %}
    /// and {% extends %}). A leading '/' or '\' means "site-root-relative"
    /// in template syntax, so it resolves against the working directory
    /// rather than the drive root.
    let resolveWithinRoot (path: string) : Result<string, string> =
        let cwd = Directory.GetCurrentDirectory()
        let root = Path.GetFullPath(cwd)
        let trimmed = path.Trim().TrimStart('/', '\\')
        let full = Path.GetFullPath(Path.Combine(cwd, trimmed))
        if full.Equals(root, StringComparison.OrdinalIgnoreCase)
           || full.StartsWith(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar.ToString(),
                              StringComparison.OrdinalIgnoreCase) then
            Ok full
        else
            Error(sprintf "Template path escapes the site root: %s" path)
