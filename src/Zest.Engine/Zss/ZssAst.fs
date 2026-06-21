namespace Zest.Engine.Zss

open System

// ============================================================
// ZSS 2.0 AST — Abstract Syntax Tree with source tracking
// ============================================================

/// Source position: line and column (1-based)
type SourcePos = {
    Line: int
    Col:  int
} with
    static member Zero = { Line = 0; Col = 0 }
    override this.ToString() = sprintf "L%d:C%d" this.Line this.Col

/// A declaration with optional source position for error reporting
type Declaration = {
    Property : string
    Value    : string
    Important: bool
    Pos      : SourcePos
} with
    static member Create(prop, value, ?important, ?pos) =
        { Property = prop
          Value = value
          Important = defaultArg important false
          Pos = defaultArg pos SourcePos.Zero }

/// ZSS AST node types — covers all CSS constructs plus ZSS extensions
type ZssNode =
    /// CSS rule: selector + declarations + nested children
    | RuleSet  of selector: string * declarations: Declaration list * children: ZssNode list * pos: SourcePos
    /// Variable: $name: value
    | Variable of name: string * value: string * isDefault: bool * pos: SourcePos
    /// Mixin definition: @mixin name($params) { body }
    | Mixin    of name: string * parameters: (string * string option) list * body: ZssNode list * pos: SourcePos
    /// Mixin call: @include name($args) { content }
    | Include  of name: string * arguments: string list * content: ZssNode list * pos: SourcePos
    /// @extend selector
    | Extend   of selector: string * pos: SourcePos
    /// @apply utility-class-1 utility-class-2
    | Apply    of classes: string list * pos: SourcePos
    /// @import "path"
    | Import   of path: string * pos: SourcePos
    /// @use "module" [as alias]
    | Use      of path: string * alias: string option * pos: SourcePos
    /// Comment text
    | Comment  of text: string * pos: SourcePos
    /// Raw at-rule block (@media, @keyframes, @supports, etc.)
    | AtRule   of name: string * prms: string * body: ZssNode list * pos: SourcePos
    /// @export $var — emit as CSS custom property in :root
    | CssVarExport of name: string * value: string * pos: SourcePos
    /// @each $item in (a,b,c) { body }
    | Each of varName: string * items: string list * body: ZssNode list * pos: SourcePos
    /// @each $key, $val in $map { body }
    | EachMap of keyVar: string * valVar: string * mapName: string * body: ZssNode list * pos: SourcePos
    /// @for $i from N through M { body }
    | For of varName: string * from: int * through: int * body: ZssNode list * pos: SourcePos
    /// @if condition { body } @else { elseBody }
    | If of condition: string * body: ZssNode list * elseBody: ZssNode list option * pos: SourcePos
    /// @content — slot content inside mixin
    | Content of pos: SourcePos
    /// Responsive shorthand: @sm/@md/@lg/@xl/@2xl
    | Responsive of breakpoint: string * body: ZssNode list * pos: SourcePos
    /// @option key: value — compiler options
    | Option of key: string * value: string * pos: SourcePos
    /// @warn / @debug message
    | Warn of message: string * pos: SourcePos
    | Debug of message: string * pos: SourcePos

/// Helper to get position from any node
module NodePos =
    let get (node: ZssNode) : SourcePos =
        match node with
        | RuleSet(_,_,_,p) | Variable(_,_,_,p) | Mixin(_,_,_,p) | Include(_,_,_,p)
        | Extend(_,p) | Apply(_,p) | Import(_,p) | Use(_,_,p) | Comment(_,p)
        | AtRule(_,_,_,p) | CssVarExport(_,_,p) | Each(_,_,_,p) | EachMap(_,_,_,_,p)
        | For(_,_,_,_,p) | If(_,_,_,p) | Content(p) | Responsive(_,_,p)
        | Option(_,_,p) | Warn(_,p) | Debug(_,p) -> p
