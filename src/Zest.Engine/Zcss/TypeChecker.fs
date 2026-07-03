namespace Zest.Engine.Zcss

open System
open System.Collections.Generic
open System.Text.RegularExpressions

// ============================================================
// ZCSS Type Checker — CSS value type safety and validation
// ============================================================

/// Represents the expected CSS type for a property value.
type CssValueType =
    | Color
    | Length
    | Percentage
    | Number
    | Angle
    | Time
    | Resolution
    | String
    | Url
    | Identifier
    | Keyword of allowedValues: string list
    | LengthOrPercentage
    | ColorOrCurrentColor
    | NumberOrPercentage
    | Auto
    | Integer
    | Any

module TypeChecker =

    /// Known CSS properties and their expected value types.
    let private knownProperties = Dictionary<string, CssValueType>(StringComparer.OrdinalIgnoreCase)

    let private init() =
        if knownProperties.Count = 0 then
            // Layout
            knownProperties.["display"] <- Keyword ["block";"inline";"inline-block";"flex";"inline-flex";"grid";"inline-grid";"table";"table-cell";"table-row";"none";"contents";"list-item"]
            knownProperties.["position"] <- Keyword ["static";"relative";"absolute";"fixed";"sticky"]
            knownProperties.["visibility"] <- Keyword ["visible";"hidden";"collapse"]
            knownProperties.["overflow"] <- Keyword ["visible";"hidden";"scroll";"auto";"clip"]
            knownProperties.["overflow-x"] <- Keyword ["visible";"hidden";"scroll";"auto";"clip"]
            knownProperties.["overflow-y"] <- Keyword ["visible";"hidden";"scroll";"auto";"clip"]
            knownProperties.["float"] <- Keyword ["left";"right";"none"]
            knownProperties.["clear"] <- Keyword ["none";"left";"right";"both"]
            knownProperties.["z-index"] <- Integer
            knownProperties.["object-fit"] <- Keyword ["fill";"contain";"cover";"none";"scale-down"]

            // Box model
            knownProperties.["width"] <- LengthOrPercentage
            knownProperties.["height"] <- LengthOrPercentage
            knownProperties.["min-width"] <- LengthOrPercentage
            knownProperties.["min-height"] <- LengthOrPercentage
            knownProperties.["max-width"] <- LengthOrPercentage
            knownProperties.["max-height"] <- LengthOrPercentage
            knownProperties.["margin"] <- LengthOrPercentage
            knownProperties.["margin-top"] <- LengthOrPercentage
            knownProperties.["margin-right"] <- LengthOrPercentage
            knownProperties.["margin-bottom"] <- LengthOrPercentage
            knownProperties.["margin-left"] <- LengthOrPercentage
            knownProperties.["margin-inline"] <- LengthOrPercentage
            knownProperties.["margin-block"] <- LengthOrPercentage
            knownProperties.["padding"] <- LengthOrPercentage
            knownProperties.["padding-top"] <- LengthOrPercentage
            knownProperties.["padding-right"] <- LengthOrPercentage
            knownProperties.["padding-bottom"] <- LengthOrPercentage
            knownProperties.["padding-left"] <- LengthOrPercentage
            knownProperties.["padding-inline"] <- LengthOrPercentage
            knownProperties.["padding-block"] <- LengthOrPercentage
            knownProperties.["box-sizing"] <- Keyword ["content-box";"border-box"]
            knownProperties.["box-shadow"] <- Any  // complex shorthand

            // Typography
            knownProperties.["color"] <- Color
            knownProperties.["font-size"] <- LengthOrPercentage
            knownProperties.["font-weight"] <- NumberOrPercentage
            knownProperties.["font-family"] <- String
            knownProperties.["font-style"] <- Keyword ["normal";"italic";"oblique"]
            knownProperties.["text-align"] <- Keyword ["left";"right";"center";"justify";"start";"end"]
            knownProperties.["text-decoration"] <- Any
            knownProperties.["text-transform"] <- Keyword ["none";"capitalize";"uppercase";"lowercase";"full-width"]
            knownProperties.["line-height"] <- NumberOrPercentage
            knownProperties.["letter-spacing"] <- Length
            knownProperties.["word-spacing"] <- Length
            knownProperties.["word-break"] <- Keyword ["normal";"break-all";"keep-all";"break-word"]
            knownProperties.["word-wrap"] <- Keyword ["normal";"break-word"]
            knownProperties.["white-space"] <- Keyword ["normal";"nowrap";"pre";"pre-wrap";"pre-line";"break-spaces"]
            knownProperties.["vertical-align"] <- Keyword ["baseline";"top";"bottom";"middle";"sub";"super";"text-top";"text-bottom"]
            knownProperties.["direction"] <- Keyword ["ltr";"rtl"]
            knownProperties.["writing-mode"] <- Keyword ["horizontal-tb";"vertical-rl";"vertical-lr"]
            knownProperties.["text-overflow"] <- Keyword ["clip";"ellipsis"]
            knownProperties.["overflow-wrap"] <- Keyword ["normal";"break-word";"anywhere"]
            knownProperties.["hyphens"] <- Keyword ["none";"manual";"auto"]
            knownProperties.["text-shadow"] <- Any
            knownProperties.["user-select"] <- Keyword ["none";"auto";"text";"all";"contain"]

            // Background
            knownProperties.["background"] <- Any
            knownProperties.["background-color"] <- Color
            knownProperties.["background-image"] <- Url
            knownProperties.["background-repeat"] <- Keyword ["repeat";"repeat-x";"repeat-y";"no-repeat";"space";"round"]
            knownProperties.["background-position"] <- LengthOrPercentage
            knownProperties.["background-size"] <- Keyword ["auto";"cover";"contain"]
            knownProperties.["background-attachment"] <- Keyword ["scroll";"fixed";"local"]
            knownProperties.["background-clip"] <- Keyword ["border-box";"padding-box";"content-box";"text"]
            knownProperties.["background-origin"] <- Keyword ["border-box";"padding-box";"content-box"]

            // Border
            knownProperties.["border"] <- Any
            knownProperties.["border-color"] <- Color
            knownProperties.["border-style"] <- Keyword ["none";"hidden";"dotted";"dashed";"solid";"double";"groove";"ridge";"inset";"outset"]
            knownProperties.["border-width"] <- Length
            knownProperties.["border-radius"] <- LengthOrPercentage
            knownProperties.["border-collapse"] <- Keyword ["collapse";"separate"]
            knownProperties.["border-spacing"] <- Length

            // Flex
            knownProperties.["flex"] <- Any
            knownProperties.["flex-direction"] <- Keyword ["row";"row-reverse";"column";"column-reverse"]
            knownProperties.["flex-wrap"] <- Keyword ["nowrap";"wrap";"wrap-reverse"]
            knownProperties.["flex-flow"] <- Any
            knownProperties.["flex-grow"] <- Number
            knownProperties.["flex-shrink"] <- Number
            knownProperties.["flex-basis"] <- LengthOrPercentage
            knownProperties.["align-items"] <- Keyword ["normal";"stretch";"center";"start";"end";"flex-start";"flex-end";"baseline"]
            knownProperties.["align-self"] <- Keyword ["auto";"normal";"stretch";"center";"start";"end";"flex-start";"flex-end";"baseline"]
            knownProperties.["align-content"] <- Keyword ["normal";"stretch";"center";"start";"end";"flex-start";"flex-end";"space-between";"space-around";"space-evenly"]
            knownProperties.["justify-content"] <- Keyword ["center";"start";"end";"flex-start";"flex-end";"space-between";"space-around";"space-evenly";"stretch"]
            knownProperties.["justify-items"] <- Keyword ["auto";"normal";"stretch";"center";"start";"end";"flex-start";"flex-end";"baseline"]
            knownProperties.["justify-self"] <- Keyword ["auto";"normal";"stretch";"center";"start";"end";"flex-start";"flex-end";"baseline"]
            knownProperties.["gap"] <- LengthOrPercentage
            knownProperties.["row-gap"] <- LengthOrPercentage
            knownProperties.["column-gap"] <- LengthOrPercentage

            // Grid
            knownProperties.["grid"] <- Any
            knownProperties.["grid-template"] <- Any
            knownProperties.["grid-template-columns"] <- Any
            knownProperties.["grid-template-rows"] <- Any
            knownProperties.["grid-template-areas"] <- Any
            knownProperties.["grid-column"] <- Any
            knownProperties.["grid-row"] <- Any
            knownProperties.["grid-area"] <- Any
            knownProperties.["grid-auto-flow"] <- Keyword ["row";"column";"dense"]
            knownProperties.["grid-auto-rows"] <- LengthOrPercentage
            knownProperties.["grid-auto-columns"] <- LengthOrPercentage

            // Effects
            knownProperties.["opacity"] <- Number
            knownProperties.["transform"] <- Any
            knownProperties.["transform-origin"] <- Any
            knownProperties.["transition"] <- Any
            knownProperties.["animation"] <- Any
            knownProperties.["animation-duration"] <- Time
            knownProperties.["animation-timing-function"] <- Any
            knownProperties.["animation-delay"] <- Time
            knownProperties.["animation-iteration-count"] <- NumberOrPercentage
            knownProperties.["animation-direction"] <- Keyword ["normal";"reverse";"alternate";"alternate-reverse"]
            knownProperties.["animation-fill-mode"] <- Keyword ["none";"forwards";"backwards";"both"]
            knownProperties.["animation-play-state"] <- Keyword ["running";"paused"]
            knownProperties.["cursor"] <- Keyword ["auto";"default";"pointer";"wait";"text";"move";"not-allowed";"help";"crosshair";"zoom-in";"zoom-out"]
            knownProperties.["filter"] <- Any
            knownProperties.["backdrop-filter"] <- Any
            knownProperties.["clip-path"] <- Any
            knownProperties.["resize"] <- Keyword ["none";"both";"horizontal";"vertical"]
            knownProperties.["pointer-events"] <- Keyword ["auto";"none";"visiblePainted";"visibleFill";"visibleStroke";"visible";"painted";"fill";"stroke";"all"]
            knownProperties.["appearance"] <- Keyword ["none";"auto";"button";"textfield";"menulist-button"]

            // Aspect ratio
            knownProperties.["aspect-ratio"] <- Any

            // Content
            knownProperties.["content"] <- Any
            knownProperties.["quotes"] <- Any
            knownProperties.["counter-increment"] <- Any
            knownProperties.["counter-reset"] <- Any
            knownProperties.["counter-set"] <- Any
            knownProperties.["list-style"] <- Any
            knownProperties.["list-style-type"] <- Keyword ["none";"disc";"circle";"square";"decimal";"decimal-leading-zero";"lower-roman";"upper-roman";"lower-alpha";"upper-alpha"]

            // Table
            knownProperties.["caption-side"] <- Keyword ["top";"bottom"]
            knownProperties.["empty-cells"] <- Keyword ["show";"hide"]
            knownProperties.["table-layout"] <- Keyword ["auto";"fixed"]

            // Columns
            knownProperties.["columns"] <- Any
            knownProperties.["column-count"] <- Integer
            knownProperties.["column-width"] <- Length
            knownProperties.["column-gap"] <- LengthOrPercentage
            knownProperties.["column-rule"] <- Any
            knownProperties.["column-span"] <- Keyword ["none";"all"]
            knownProperties.["column-fill"] <- Keyword ["auto";"balance"]

            // Miscellaneous
            knownProperties.["outline"] <- Any
            knownProperties.["outline-color"] <- Color
            knownProperties.["outline-style"] <- Keyword ["none";"hidden";"dotted";"dashed";"solid";"double";"groove";"ridge";"inset";"outset"]
            knownProperties.["outline-width"] <- Length
            knownProperties.["outline-offset"] <- Length
            knownProperties.["will-change"] <- Any
            knownProperties.["contain"] <- Keyword ["none";"strict";"content";"size";"layout";"style";"paint"]
            knownProperties.["isolation"] <- Keyword ["auto";"isolate"]
            knownProperties.["page-break-before"] <- Keyword ["auto";"always";"avoid";"left";"right"]
            knownProperties.["page-break-after"] <- Keyword ["auto";"always";"avoid";"left";"right"]
            knownProperties.["page-break-inside"] <- Keyword ["auto";"avoid"]
            knownProperties.["caret-color"] <- Color
            knownProperties.["accent-color"] <- Color
            knownProperties.["color-scheme"] <- Any

    do init()

    /// Check if a value looks like a valid CSS color.
    let private isColor (v: string) =
        let v = v.Trim()
        Regex.IsMatch(v, @"^#[0-9a-fA-F]{3,8}$")  // hex
        || Regex.IsMatch(v, @"^rgba?\s*\(")         // rgb/rgba
        || Regex.IsMatch(v, @"^hsla?\s*\(")          // hsl/hsla
        || Regex.IsMatch(v, @"^(transparent|currentColor|inherit|initial|unset)$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(v, @"^[a-zA-Z]+$")          // named color (e.g. red, blue)

    /// Check if a value looks like a CSS length (including calc expressions).
    let private isLength (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?[\d.]+(px|rem|em|ex|ch|cm|mm|in|pt|pc|vw|vh|vmin|vmax|svw|svh|lvw|lvh|dvw|dvh|Q)$", RegexOptions.IgnoreCase)
        || Regex.IsMatch(v, @"^calc\(")

    /// Check if a value looks like a percentage.
    let private isPercentage (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?[\d.]+%$")

    /// Check if a value looks like a number.
    let private isNumber (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?[\d.]+$")

    /// Check if a value looks like a time value.
    let private isTime (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?[\d.]+(s|ms)$")

    /// Check if a value looks like an angle.
    let private isAngle (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?[\d.]+(deg|rad|grad|turn)$")

    /// Check if a value looks like a URL.
    let private isUrl (v: string) =
        Regex.IsMatch(v.Trim(), @"^url\s*\(")
        || Regex.IsMatch(v.Trim(), @"^[""'].*[""']$")

    /// Check if a value looks like an integer.
    let private isInteger (v: string) =
        Regex.IsMatch(v.Trim(), @"^-?\d+$")

    /// Check if a value is a CSS identifier (non-quoted keyword).
    let private isIdentifier (v: string) =
        Regex.IsMatch(v.Trim(), @"^[a-zA-Z_][a-zA-Z0-9_-]*$")

    /// Get the type of a CSS value.
    let private inferValueType (value: string) : CssValueType =
        let v = value.Trim()
        if isColor v then Color
        elif isLength v then Length
        elif isPercentage v then Percentage
        elif isTime v then Time
        elif isAngle v then Angle
        elif isNumber v then Number
        elif isUrl v then Url
        elif isInteger v then Integer
        elif v = "auto" then Auto
        elif isIdentifier v then Identifier
        else Any

    /// Type errors found during validation.
    type TypeError = {
        Property: string
        Value: string
        ExpectedType: string
        ActualType: string
        Line: int
    }

    /// Validate a single declaration against the type system.
    let validateDeclaration (prop: string) (value: string) (line: int) : TypeError option =
        match knownProperties.TryGetValue(prop) with
        | false, _ -> None  // unknown property, skip
        | true, expected ->
            let actual = inferValueType value
            let typeName (t: CssValueType) =
                match t with
                | Color -> "color"
                | Length -> "length"
                | Percentage -> "percentage"
                | Number -> "number"
                | Angle -> "angle"
                | Time -> "time"
                | Resolution -> "resolution"
                | String -> "string"
                | Url -> "url"
                | Identifier -> "identifier"
                | Keyword allowed -> sprintf "keyword (%s)" (String.concat "|" allowed)
                | LengthOrPercentage -> "length-or-percentage"
                | ColorOrCurrentColor -> "color"
                | NumberOrPercentage -> "number-or-percentage"
                | Auto -> "auto"
                | Integer -> "integer"
                | Any -> "any"

            let isMatch =
                match expected, actual with
                | Any, _ -> true
                | Auto, Auto -> true
                | Color, Color -> true
                | ColorOrCurrentColor, (Color | Auto) -> true
                | Length, Length -> true
                | Percentage, Percentage -> true
                | LengthOrPercentage, (Length | Percentage | Auto) -> true
                | Number, Number -> true
                | NumberOrPercentage, (Number | Percentage) -> true
                | Integer, Integer -> true
                | Time, Time -> true
                | Angle, Angle -> true
                | Url, Url -> true
                | String, _ -> true
                | Identifier, Identifier -> true
                | Keyword allowed, Identifier ->
                    allowed |> List.exists (fun k ->
                        String.Equals(k, value.Trim(), StringComparison.OrdinalIgnoreCase))
                | _ -> false

            if isMatch then None
            else Some {
                Property = prop
                Value = value
                ExpectedType = typeName expected
                ActualType = typeName actual
                Line = line
            }

    /// Validate an entire ZCSS AST node list.
    let rec validate (nodes: ZcssNode list) : TypeError list =
        nodes |> List.collect (fun node ->
            match node with
            | RuleSet(_, decls, children, _) ->
                let declErrors =
                    decls |> List.collect (fun d ->
                        match validateDeclaration d.Property d.Value d.Pos.Line with
                        | None -> []
                        | Some err -> [err])
                declErrors @ validate children
            | AtRule(_, _, body, _) -> validate body
            | Responsive(_, body, _) -> validate body
            | Mixin(_, _, body, _) -> validate body
            | Each(_, _, body, _) -> validate body
            | EachMap(_, _, _, body, _) -> validate body
            | For(_, _, _, body, _) -> validate body
            | If(_, body, elseBody, _) ->
                validate body @ (elseBody |> Option.map validate |> Option.defaultValue [])
            | Include(_, _, content, _) -> validate content
            | _ -> [])

    /// Format a type error for display.
    let formatError (err: TypeError) : string =
        sprintf "[ZCSS TYPE] %s: property '%s' expects %s but got '%s' (value: '%s')"
            (if err.Line > 0 then sprintf "line %d" err.Line else "unknown")
            err.Property err.ExpectedType err.ActualType err.Value

    /// Get the expected type description for a property.
    let getExpectedType (prop: string) : string option =
        match knownProperties.TryGetValue(prop) with
        | true, t -> Some (sprintf "%A" t)
        | _ -> None

    /// List all known CSS properties (for auto-completion).
    let listKnownProperties () : (string * string) list =
        knownProperties
        |> Seq.map (fun kv -> kv.Key, sprintf "%A" kv.Value)
        |> Seq.sortBy fst
        |> List.ofSeq

    /// Check if a property is known.
    let isKnownProperty (prop: string) : bool =
        knownProperties.ContainsKey(prop)
