namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text

// ============================================================
// ZCSS Utilities — Built-in utility classes and functions
// ============================================================

module Utilities =

    /// Process @use directives — resolve built-in modules
    let resolveUse (path: string) : string option =
        match path with
        | "zest:utilities" | "utilities" -> Some BuiltinStyles.builtinUtilities
        | "zest:reset" | "reset" -> Some BuiltinStyles.cssReset
        | "zest:palette" | "palette" | "zest:colors" | "colors" -> Some BuiltinStyles.colorPalettes
        | "zest:animations" | "animations" -> Some BuiltinStyles.animationUtilities
        | "zest:gradients" | "gradients" -> Some BuiltinStyles.gradientUtilities
        | "zest:filters" | "filters" -> Some BuiltinStyles.filterUtilities
        | "zest:layout" | "layout" -> Some BuiltinStyles.layoutUtilities
        | "zest:all" | "all" ->
            Some (BuiltinStyles.builtinUtilities + BuiltinStyles.animationUtilities + BuiltinStyles.gradientUtilities + BuiltinStyles.filterUtilities + BuiltinStyles.layoutUtilities)
        | _ -> None
