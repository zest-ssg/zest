namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.IO

// ============================================================
// ZcssModules — Module system with namespace aliases
// ============================================================
// Resolves `@use "zest:palette" as p;` so that namespaced
// references like `p.primary` (or `$p.primary`) resolve to the
// imported module's variable. Also provides `getModuleSource`
// as the single source of truth for built-in + user-file imports,
// used by ZcssProcessor.
//
// Dependencies: BuiltinStyles (module text), ParserCore (var
// extraction), Utilities (built-in resolution).
// ============================================================

module Modules =

    /// A parsed `@use` directive: the module path and an optional alias.
    type UseDirective =
        { Path: string; Alias: string option }

    /// Get the source text for a module path.
    /// Built-in modules (zest:utilities, etc.) are resolved first;
    /// otherwise the path is treated as a relative file path.
    let getModuleSource (baseDir: string option) (path: string) : string option =
        match Utilities.resolveUse path with
        | Some _ as result -> result
        | None ->
            match baseDir with
            | Some dir ->
                let fullPath = Path.GetFullPath(Path.Combine(dir, path))
                if File.Exists fullPath then Some(File.ReadAllText(fullPath))
                else
                    eprintfn "[ZCSS WARN] @use import not found: '%s' (resolved: %s)" path fullPath
                    None
            | None ->
                eprintfn "[ZCSS WARN] @use import '%s' skipped — no source file context" path
                None

    /// Extract `$name: value` variable declarations from ZCSS source text.
    /// Returns a dictionary of variable name (without `$`) → value.
    let extractVars (source: string) : IDictionary<string, string> =
        let d = Dictionary<string, string>()
        let cleaned = ParserCore.stripComments source
        let lines = cleaned.Split('\n') |> Array.map (fun l -> l.TrimEnd('\r'))
        for kv in ParserCore.extractVars lines do
            d.[kv.Key] <- kv.Value
        d :> IDictionary<string, string>

    /// Given a list of `@use` directives with aliases, build a variable
    /// dictionary where each namespaced import's variables are registered
    /// under `alias.varname` (in addition to their original names).
    /// Non-aliased imports contribute their variables un-prefixed.
    ///
    /// Example: `@use "zest:palette" as p;` with palette defining
    /// `$primary: #3b82f6` registers `p.primary` → `#3b82f6`.
    let buildNamespacedVars (baseDir: string option) (directives: UseDirective list) : IDictionary<string, string> =
        let result = Dictionary<string, string>()
        for d in directives do
            match getModuleSource baseDir d.Path with
            | Some src ->
                let vars = extractVars src
                for kv in vars do
                    // Always register under original name (backward compat)
                    if not (result.ContainsKey kv.Key) then result.[kv.Key] <- kv.Value
                    // When aliased, also register under alias.name
                    match d.Alias with
                    | Some alias ->
                        let nsKey = alias + "." + kv.Key
                        result.[nsKey] <- kv.Value
                    | None -> ()
            | None -> ()
        result :> IDictionary<string, string>

    /// Resolve a namespaced reference `alias.name` or `$alias.name` against
    /// a variable dictionary. Returns Some(value) when found.
    let tryResolveNamespaced (ref: string) (vars: IDictionary<string, string>) : string option =
        let t = ref.TrimStart('$').Trim()
        if t.Contains(".") then
            match vars.TryGetValue t with
            | true, v -> Some v
            | _ -> None
        else None
