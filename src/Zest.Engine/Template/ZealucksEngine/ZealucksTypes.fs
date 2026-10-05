namespace Zest.Engine.Template

// ZealucksTypes.fs
//
// Foundation types shared by every Zealucks engine module.
// Token is the tokenizer output consumed by the block collector and renderer;
// SafeString marks a value that must bypass HTML auto-escaping.
//
// Invariant: both types are immutable and carry no render state.

module internal ZealucksTypes =

    // ── Safe string wrapper (bypasses auto-escaping) ──
    type SafeString(s: string) =
        member _.Value = s
        override _.ToString() = s

    // ── Token types ────────────────────────────────────────
    // Each token carries its 1-based source line so runtime/syntax errors can
    // be reported with a meaningful location instead of line 0.
    type Token =
        | TextToken of string * int      // literal text, source line
        | VarToken  of string * int      // {{ expr }}, source line
        | TagToken  of string * string list * int  // {% tag args %}, source line
        | CmtToken  of string * int      // {# comment #}, source line
