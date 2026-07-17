namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text

// ============================================================
// ZCSS Utilities — Built-in utility classes and functions
// ============================================================

module Utilities =

    /// Process @use directives — resolve built-in modules.
    /// Composition utilities are bundled into `zest:utilities` so the
    /// Tailwind-style shortcuts (`.flex-center`, etc.) are available by
    /// default; they can also be imported on their own via `zest:composition`.
    let resolveUse (path: string) : string option =
        match path with
        | "zest:utilities" | "utilities" ->
            Some (BuiltinStyles.builtinUtilities + BuiltinStyles.compositionUtilities)
        | "zest:reset" | "reset" -> Some BuiltinStyles.cssReset
        | "zest:palette" | "palette" | "zest:colors" | "colors" -> Some BuiltinStyles.colorPalettes
        | "zest:animations" | "animations" -> Some BuiltinStyles.animationUtilities
        | "zest:gradients" | "gradients" -> Some BuiltinStyles.gradientUtilities
        | "zest:filters" | "filters" -> Some BuiltinStyles.filterUtilities
        | "zest:layout" | "layout" -> Some (BuiltinStyles.layoutUtilities + BuiltinStyles.layoutMixins)
        | "zest:layout-mixins" | "layout-mixins" -> Some BuiltinStyles.layoutMixins
        | "zest:composition" | "composition" -> Some BuiltinStyles.compositionUtilities
        | "zest:all" | "all" ->
            Some (BuiltinStyles.builtinUtilities + BuiltinStyles.compositionUtilities
                  + BuiltinStyles.animationUtilities + BuiltinStyles.gradientUtilities
                  + BuiltinStyles.filterUtilities + BuiltinStyles.layoutUtilities + BuiltinStyles.layoutMixins)
        | _ -> None
