namespace Zest.Engine.Template

open System.Collections.Generic

// HbsTypes.fs
//
// Foundation types shared by every Handlebars engine module: the token and
// AST node shapes plus the per-render environment record.
//
// Invariant: all three types are immutable value records carrying no render state.

module internal HbsTypes =

    // ── Tokens ─────────────────────────────────────────────────────────
    type HbsToken =
        | TText of string
        | TExpr of expr: string * triple: bool
        | TBlockOpen of name: string * args: string
        | TBlockClose of name: string
        | TElseIf of args: string
        | TElse
        | TInverted of name: string * args: string
        | TPartial of name: string * args: string
        | TComment

    // ── AST nodes ──────────────────────────────────────────────────────
    type HbsNode =
        | NText of string
        | NExpr of expr: string * triple: bool
        | NBlock of name: string * args: string * body: HbsNode list * elseBody: HbsNode list option
        | NInverted of name: string * args: string * body: HbsNode list
        | NPartial of name: string * args: string

    /// Runtime environment for a single render.
    type RenderEnv = {
        /// Context stack; head is current context.
        Stack: obj list
        Root: obj
        Vars: IDictionary<string, obj>
        /// Loop metadata: @index / @key / @first / @last
        Meta: Map<string, obj>
        /// Partial loader: name → source text
        LoadPartial: string -> string option
    }
