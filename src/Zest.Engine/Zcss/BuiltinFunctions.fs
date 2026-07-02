namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// Built-in Functions — Utility functions (unit, unitless, abs, min, max...)
// ============================================================

module BuiltinFunctions =

    let private fnMapPat = Regex(@"(\w+)\(\s*([^)]*)\s*\)", RegexOptions.Compiled)

    let private unitPat = Regex(@"^(-?[\d.]+)(\w+|%)?$", RegexOptions.Compiled)

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
                        let v = float arg * 100.0
                        sprintf "%g%%" v
                    | "str-length" -> arg.Length.ToString()
                    | "to-upper" -> arg.ToUpper()
                    | "to-lower" -> arg.ToLower()
                    | "quote" -> sprintf "\"%s\"" arg
                    | "unquote" -> arg.Trim('"', '\'')
                    | "list-length" ->
                        arg.Split([|' '|], StringSplitOptions.RemoveEmptyEntries).Length.ToString()
                    | "list-nth" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let list = parts.[0].Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                            let n = int parts.[1]
                            if n > 0 && n <= list.Length then list.[n-1]
                            elif n < 0 && abs n <= list.Length then list.[list.Length + n]
                            else ""
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
                        if mv.Success then
                            let n = float mv.Groups.[1].Value
                            let u = if mv.Groups.[2].Success then mv.Groups.[2].Value else ""
                            sprintf "%g%s" (abs n) u
                        else arg
                    | "min" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let nums = parts |> Array.choose (fun p ->
                                let mv = unitPat.Match(p)
                                if mv.Success then Some(float mv.Groups.[1].Value) else None)
                            if nums.Length > 0 then string (Array.min nums) else arg
                        else arg
                    | "max" ->
                        let parts = arg.Split(',') |> Array.map (fun s -> s.Trim())
                        if parts.Length >= 2 then
                            let nums = parts |> Array.choose (fun p ->
                                let mv = unitPat.Match(p)
                                if mv.Success then Some(float mv.Groups.[1].Value) else None)
                            if nums.Length > 0 then string (Array.max nums) else arg
                        else arg
                    | _ -> null
                if replacement <> null then
                    result <- result.Substring(0, m.Index) + replacement + result.Substring(m.Index + m.Length)
                    changed <- true
        result
