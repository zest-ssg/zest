namespace Zest.Compiler.Zcss
open System
open System.Collections.Generic
open System.Globalization
open System.Text.RegularExpressions

// ============================================================
// Built-in Functions — Utility functions (unit, unitless, abs, min, max...)
// ============================================================

module BuiltinFunctions =

    let private fnMapPat = Regex(@"(\w+)\(\s*([^)]*)\s*\)", RegexOptions.Compiled)

    let private unitPat = Regex(@"^(-?[\d.]+)(\w+|%)?$", RegexOptions.Compiled)

    /// Culture-invariant numeric parse; returns None instead of throwing so a
    /// stray non-numeric argument falls through to the original value.
    let private tryNum (s: string) : float option =
        match Double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true, v -> Some v
        | _ -> None

    /// Escape a value for inclusion inside a double-quoted CSS string.
    let private escapeCssString (s: string) =
        s.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let resolve (value: string) (vars: IDictionary<string, string>) : string =
        let mutable result = value
        let mutable changed = true
        let mutable iterations = 0
        let maxIterations = 20  // safety limit to prevent infinite loops
        while changed && iterations < maxIterations do
            changed <- false
            iterations <- iterations + 1
            let m = fnMapPat.Match(result)
            if m.Success then
                let fn = m.Groups.[1].Value
                let arg = m.Groups.[2].Value.Trim()
                let replacement =
                    match fn with
                    | "unit" ->
                        let mv = unitPat.Match(arg)
                        if mv.Success && mv.Groups.[2].Success then mv.Groups.[2].Value
                        else ""
                    | "unitless" ->
                        let mv = unitPat.Match(arg)
                        if mv.Success && not mv.Groups.[2].Success then "true" else "false"
                    | "percentage" ->
                        match tryNum arg with
                        | Some v -> sprintf "%g%%" (v * 100.0)
                        | None -> arg
                    | "str-length" -> arg.Length.ToString()
                    | "to-upper" -> arg.ToUpper()
                    | "to-lower" -> arg.ToLower()
                    | "quote" -> sprintf "\"%s\"" (escapeCssString arg)
                    | "unquote" -> arg.Trim('"', '\'')
                    | "list-length" ->
                        arg.Split([|' '|], StringSplitOptions.RemoveEmptyEntries).Length.ToString()
                    | "list-nth" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let list = parts.[0].Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                            match Int32.TryParse parts.[1] with
                            | true, n when n > 0 && n <= list.Length -> list.[n-1]
                            | true, n when n < 0 && abs n <= list.Length -> list.[list.Length + n]
                            | _ -> ""
                        else ""
                    | "type-of" ->
                        let mv = unitPat.Match(arg)
                        if mv.Success then
                            if mv.Groups.[2].Success then "number"
                            else "number"  // unitless number
                        elif arg.StartsWith("$") then "variable"
                        elif arg.StartsWith("\"") || arg.StartsWith("'") then "string"
                        else "string"
                    | "abs" ->
                        let mv = unitPat.Match(arg)
                        match (if mv.Success then tryNum mv.Groups.[1].Value else None) with
                        | Some n ->
                            let u = if mv.Groups.[2].Success then mv.Groups.[2].Value else ""
                            sprintf "%g%s" (abs n) u
                        | None -> arg
                    | "min" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let nums = parts |> Array.choose (fun p ->
                                let mv = unitPat.Match(p)
                                if mv.Success then tryNum mv.Groups.[1].Value else None)
                            if nums.Length > 0 then string (Array.min nums) else arg
                        else arg
                    | "max" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let nums = parts |> Array.choose (fun p ->
                                let mv = unitPat.Match(p)
                                if mv.Success then tryNum mv.Groups.[1].Value else None)
                            if nums.Length > 0 then string (Array.max nums) else arg
                        else arg
                    // env-color("primary") → var(--primary)
                    // Generates a CSS custom-property reference, useful for
                    // theming and runtime variable overrides.
                    | "env-color" ->
                        let name = arg.Trim('"', '\'')
                        sprintf "var(--%s)" name
                    // env("name") — alias for env-color, mirroring CSS env()
                    | "env" ->
                        let name = arg.Trim('"', '\'')
                        sprintf "var(--%s)" name
                    // cssvar("name", fallback) → var(--name, fallback)
                    | "cssvar" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            sprintf "var(--%s, %s)" (parts.[0].Trim('"','\'')) parts.[1]
                        else
                            sprintf "var(--%s)" (parts.[0].Trim('"','\''))
                    | _ -> null
                if replacement <> null then
                    result <- result.Substring(0, m.Index) + replacement + result.Substring(m.Index + m.Length)
                    changed <- true
        result
