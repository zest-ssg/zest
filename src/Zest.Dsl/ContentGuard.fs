namespace Zest.Dsl

open System

// ============================================================
// ContentGuard — Content validation, guard, and debug helpers
// ============================================================

module ContentGuard =
    open Dsl

    /// Guard: if value is null or empty, return fallback; otherwise apply render.
    let guard (value: string) (render: string -> string) (fallback: string) =
        if String.IsNullOrEmpty value then fallback
        else render value

    /// GuardOption: if value is Some, apply render; otherwise fallback.
    let guard_opt (value: string option) (render: string -> string) (fallback: string) =
        match value with
        | Some v when not (String.IsNullOrEmpty v) -> render v
        | _ -> fallback

    /// Guard a list: only render if the list is non-empty.
    let guard_list (items: 'a list) (render: 'a list -> string) (fallback: string) =
        if List.isEmpty items then fallback
        else render items

    /// Wrap content in an HTML comment with a validation error if condition is not met.
    let validate (condition: bool) (message: string) (content: string) =
        if condition then content
        else sprintf "<!-- VALIDATION ERROR: %s -->%s" (htmlEncode message) content

    /// Assert that a required value is present; emit error comment if not.
    let require (fieldName: string) (value: string) =
        if String.IsNullOrEmpty value then
            sprintf "<!-- REQUIRED FIELD MISSING: %s -->" (htmlEncode fieldName)
        else ""

    /// Warn when a value is suspiciously short/long.
    let warn_if (condition: bool) (message: string) (content: string) =
        if condition then
            sprintf "<!-- WARNING: %s -->%s" (htmlEncode message) content
        else content

    /// Print a debug message to stderr during script evaluation.
    let debug (format: string) ([<ParamArray>] args: obj[]) =
        let msg = String.Format(format, args)
        eprintfn "[Zest.DSL] %s" msg

    /// Trace the value of an expression (returns the value unchanged).
    let trace (label: string) (value: 'a) =
        eprintfn "[Zest.DSL] %s = %A" label value
        value
