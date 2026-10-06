namespace Zest.Compiler.Zestucks
open System
open System.IO

// Tokens.fs
//
// Foundation types shared by every Zestucks engine module. Must stay first in
// the compilation order: everything else in Template/ builds on these.
//
// There is no generic-template-engine layer any more. Zest ships one template
// language — Zestucks — so these types describe Zestucks directly instead of
// being an abstraction other engines could implement.
//
// Invariant: all types here are immutable and carry no render state.

/// <summary>
/// An error raised while tokenizing, evaluating, or rendering a Zestucks
/// template.
/// </summary>
type ZestucksError =
    /// Evaluation or rendering failed. Carries the source line when known.
    | RuntimeError of message: string * line: int
    /// A template referenced by an include/extends tag was not found.
    | NotFound of name: string
with
    override this.ToString() =
        match this with
        | RuntimeError(msg, line) -> sprintf "[Zestucks] %s (line %d)" msg line
        | NotFound(name)          -> sprintf "[Zestucks] Template not found: %s" name

/// <summary>
/// A Zestucks filter: transforms an incoming value into an output value.
/// </summary>
/// <param name="value">The value produced by the expression preceding the filter.</param>
/// <param name="args">String forms of any filter arguments, in source order.</param>
/// <returns>The transformed value. Returning the input unchanged is the
/// conventional "unknown filter" fallback so a misnamed filter degrades to a
/// passthrough instead of aborting the whole render.</returns>
type ZestucksFilter = obj -> string list -> obj

/// Path helpers for resolving include/extends references.
module ZestucksPaths =

    /// Resolve a template-relative path against the working directory and
    /// reject paths that escape it (path-traversal guard for include/extends).
    /// A leading '/' or '\' means "site-root-relative" in template syntax, so
    /// it resolves against the working directory rather than the drive root.
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

module internal Tokens =

    // ── Safe string wrapper (bypasses auto-escaping) ──
    type SafeString(s: string) =
        member _.Value = s
        override _.ToString() = s

    // ── Token types ────────────────────────────────────────
    // Each token carries its 1-based source line so runtime/syntax errors can
    // be reported with a meaningful location instead of line 0.
    type Token =
        | TextToken of string * int      // literal text, source line
        | VarToken  of string * int      // expression, source line
        | TagToken  of string * string list * int  // tag + args, source line
        | CmtToken  of string * int      // comment, source line

    /// A zero-copy window into a shared Token[]. The renderer recurses into
    /// block bodies by narrowing a range instead of slicing/copying tokens, so
    /// a deeply nested template stays O(n) in allocations instead of building a
    /// fresh list (and then array) at every nesting level.
    [<Struct>]
    type TokenRange =
        { Source: Token[]
          Start: int
          Count: int }
        /// Index one past the last token in the window (exclusive).
        member this.Stop = this.Start + this.Count
        member this.IsEmpty = this.Count <= 0
        member this.Item
            with get (i: int) = this.Source.[this.Start + i]

    module TokenRange =
        let empty : TokenRange = { Source = [||]; Start = 0; Count = 0 }
        let ofArray (a: Token[]) : TokenRange = { Source = a; Start = 0; Count = a.Length }
        let ofList (l: Token list) : TokenRange =
            let a = List.toArray l
            { Source = a; Start = 0; Count = a.Length }
