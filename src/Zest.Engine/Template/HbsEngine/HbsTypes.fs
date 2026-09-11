namespace Zest.Engine.Template

open System.Collections.Generic

// HbsTypes.fs
//
// Foundation types shared by every Handlebars engine module: the token and
// AST node shapes, the per-render environment record, and the public helper
// function type used for both built-in and user-registered helpers.
//
// Invariant: tokens and nodes are immutable value records carrying no render
// state. RenderEnv is rebuilt per render; its Helpers map is fixed for the
// lifetime of a single render.

/// <summary>
/// A Handlebars helper: receives already-resolved positional arguments and
/// named (hash) arguments, and returns a value that the renderer HTML-escapes.
/// Return the input value unchanged to act as a passthrough.
/// </summary>
type HbsHelper = obj list -> Map<string, obj> -> obj

module internal HbsTypes =

    /// <summary>
    /// Per-render environment. Data is a stack of `@`-variable frames; the
    /// head is the innermost scope, so `@../index` reads the parent frame.
    /// </summary>
    type RenderEnv = {
        /// Context stack; the head is the current context object.
        Stack: obj list
        /// Root context (the value of `@root`).
        Root: obj
        /// The top-level variable dictionary backing the root stack frame.
        Vars: IDictionary<string, obj>
        /// `@`-variable frames (index/key/first/last per loop scope).
        Data: Map<string, obj> list
        /// Block-parameter frames (`as |item index|`); head is the innermost block.
        Params: Map<string, obj> list
        /// Registered helpers, keyed by helper name.
        Helpers: Map<string, HbsHelper>
        /// Partial loader: name → source text.
        LoadPartial: string -> string option
        /// Body of the innermost `{{#> partial}}` block, renderable as `@partial-block`.
        PartialBlockBody: HbsNode list option
    }

    // ── Tokens ─────────────────────────────────────────────────────────
    type HbsToken =
        | TText of string
        | TExpr of expr: string * triple: bool
        | TBlockOpen of name: string * args: string * blockParams: string list
        | TBlockClose of name: string
        | TElseIf of args: string
        | TElse
        | TInverted of name: string * args: string * blockParams: string list
        | TPartial of name: string * args: string
        | TPartialBlock of name: string * args: string
        | TComment

    // ── AST nodes ──────────────────────────────────────────────────────
    type HbsNode =
        | NText of string
        | NExpr of expr: string * triple: bool
        | NBlock of name: string * args: string * blockParams: string list * body: HbsNode list * elseBody: HbsNode list option
        | NInverted of name: string * args: string * blockParams: string list * body: HbsNode list
        | NPartial of name: string * args: string
        | NPartialBlock of name: string * args: string * body: HbsNode list
