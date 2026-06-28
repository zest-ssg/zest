namespace Zest.Engine.Zcss

open System
open System.Collections.Generic

// ============================================================
// ZSS Auto-Completion — LSP-compatible completion data
// ============================================================

/// A single completion item for IDE integration.
type CompletionItem = {
    Label: string
    Kind: CompletionKind
    Detail: string
    InsertText: string
    Documentation: string option
}

and CompletionKind =
    | Property
    | Value
    | Function
    | Color
    | Selector
    | CssAtRule
    | CssVariable
    | CssKeyword

module Completions =

    /// CSS property names with descriptions (for property-name completion).
    let propertyCompletions: CompletionItem list = [
        // Layout
        { Label = "display"; Kind = Property; Detail = "display"; InsertText = "display: "; Documentation = Some "Sets the display type of an element" }
        { Label = "position"; Kind = Property; Detail = "position"; InsertText = "position: "; Documentation = Some "Sets the positioning method" }
        { Label = "visibility"; Kind = Property; Detail = "visibility"; InsertText = "visibility: "; Documentation = Some "Controls element visibility" }
        { Label = "overflow"; Kind = Property; Detail = "overflow"; InsertText = "overflow: "; Documentation = Some "Specifies overflow behavior" }
        { Label = "overflow-x"; Kind = Property; Detail = "overflow-x"; InsertText = "overflow-x: "; Documentation = Some "Specifies horizontal overflow" }
        { Label = "overflow-y"; Kind = Property; Detail = "overflow-y"; InsertText = "overflow-y: "; Documentation = Some "Specifies vertical overflow" }
        { Label = "float"; Kind = Property; Detail = "float"; InsertText = "float: "; Documentation = Some "Specifies floating behavior" }
        { Label = "clear"; Kind = Property; Detail = "clear"; InsertText = "clear: "; Documentation = Some "Specifies cleared sides" }
        { Label = "z-index"; Kind = Property; Detail = "z-index"; InsertText = "z-index: "; Documentation = Some "Sets the stack order" }
        { Label = "object-fit"; Kind = Property; Detail = "object-fit"; InsertText = "object-fit: "; Documentation = Some "Specifies how object fits its box" }

        // Box model
        { Label = "width"; Kind = Property; Detail = "width"; InsertText = "width: "; Documentation = Some "Sets element width" }
        { Label = "height"; Kind = Property; Detail = "height"; InsertText = "height: "; Documentation = Some "Sets element height" }
        { Label = "min-width"; Kind = Property; Detail = "min-width"; InsertText = "min-width: "; Documentation = Some "Sets minimum width" }
        { Label = "min-height"; Kind = Property; Detail = "min-height"; InsertText = "min-height: "; Documentation = Some "Sets minimum height" }
        { Label = "max-width"; Kind = Property; Detail = "max-width"; InsertText = "max-width: "; Documentation = Some "Sets maximum width" }
        { Label = "max-height"; Kind = Property; Detail = "max-height"; InsertText = "max-height: "; Documentation = Some "Sets maximum height" }
        { Label = "margin"; Kind = Property; Detail = "margin"; InsertText = "margin: "; Documentation = Some "Sets all margins" }
        { Label = "margin-top"; Kind = Property; Detail = "margin-top"; InsertText = "margin-top: "; Documentation = Some "Sets top margin" }
        { Label = "margin-right"; Kind = Property; Detail = "margin-right"; InsertText = "margin-right: "; Documentation = Some "Sets right margin" }
        { Label = "margin-bottom"; Kind = Property; Detail = "margin-bottom"; InsertText = "margin-bottom: "; Documentation = Some "Sets bottom margin" }
        { Label = "margin-left"; Kind = Property; Detail = "margin-left"; InsertText = "margin-left: "; Documentation = Some "Sets left margin" }
        { Label = "margin-inline"; Kind = Property; Detail = "margin-inline"; InsertText = "margin-inline: "; Documentation = Some "Sets inline-axis margins" }
        { Label = "margin-block"; Kind = Property; Detail = "margin-block"; InsertText = "margin-block: "; Documentation = Some "Sets block-axis margins" }
        { Label = "padding"; Kind = Property; Detail = "padding"; InsertText = "padding: "; Documentation = Some "Sets all paddings" }
        { Label = "padding-top"; Kind = Property; Detail = "padding-top"; InsertText = "padding-top: "; Documentation = Some "Sets top padding" }
        { Label = "padding-right"; Kind = Property; Detail = "padding-right"; InsertText = "padding-right: "; Documentation = Some "Sets right padding" }
        { Label = "padding-bottom"; Kind = Property; Detail = "padding-bottom"; InsertText = "padding-bottom: "; Documentation = Some "Sets bottom padding" }
        { Label = "padding-left"; Kind = Property; Detail = "padding-left"; InsertText = "padding-left: "; Documentation = Some "Sets left padding" }
        { Label = "padding-inline"; Kind = Property; Detail = "padding-inline"; InsertText = "padding-inline: "; Documentation = Some "Sets inline-axis paddings" }
        { Label = "padding-block"; Kind = Property; Detail = "padding-block"; InsertText = "padding-block: "; Documentation = Some "Sets block-axis paddings" }
        { Label = "box-sizing"; Kind = Property; Detail = "box-sizing"; InsertText = "box-sizing: "; Documentation = Some "Specifies box model" }
        { Label = "box-shadow"; Kind = Property; Detail = "box-shadow"; InsertText = "box-shadow: "; Documentation = Some "Applies shadow to element" }

        // Typography
        { Label = "color"; Kind = Property; Detail = "color"; InsertText = "color: "; Documentation = Some "Sets text color" }
        { Label = "font-size"; Kind = Property; Detail = "font-size"; InsertText = "font-size: "; Documentation = Some "Sets font size" }
        { Label = "font-weight"; Kind = Property; Detail = "font-weight"; InsertText = "font-weight: "; Documentation = Some "Sets font weight" }
        { Label = "font-family"; Kind = Property; Detail = "font-family"; InsertText = "font-family: "; Documentation = Some "Sets font family" }
        { Label = "font-style"; Kind = Property; Detail = "font-style"; InsertText = "font-style: "; Documentation = Some "Sets font style" }
        { Label = "text-align"; Kind = Property; Detail = "text-align"; InsertText = "text-align: "; Documentation = Some "Sets text alignment" }
        { Label = "text-decoration"; Kind = Property; Detail = "text-decoration"; InsertText = "text-decoration: "; Documentation = Some "Sets text decoration" }
        { Label = "text-transform"; Kind = Property; Detail = "text-transform"; InsertText = "text-transform: "; Documentation = Some "Sets text casing" }
        { Label = "line-height"; Kind = Property; Detail = "line-height"; InsertText = "line-height: "; Documentation = Some "Sets line height" }
        { Label = "letter-spacing"; Kind = Property; Detail = "letter-spacing"; InsertText = "letter-spacing: "; Documentation = Some "Sets letter spacing" }
        { Label = "word-spacing"; Kind = Property; Detail = "word-spacing"; InsertText = "word-spacing: "; Documentation = Some "Sets word spacing" }
        { Label = "white-space"; Kind = Property; Detail = "white-space"; InsertText = "white-space: "; Documentation = Some "Sets whitespace handling" }
        { Label = "word-break"; Kind = Property; Detail = "word-break"; InsertText = "word-break: "; Documentation = Some "Sets word breaking rules" }
        { Label = "text-shadow"; Kind = Property; Detail = "text-shadow"; InsertText = "text-shadow: "; Documentation = Some "Applies text shadow" }
        { Label = "user-select"; Kind = Property; Detail = "user-select"; InsertText = "user-select: "; Documentation = Some "Controls text selection" }

        // Background
        { Label = "background"; Kind = Property; Detail = "background"; InsertText = "background: "; Documentation = Some "Background shorthand" }
        { Label = "background-color"; Kind = Property; Detail = "background-color"; InsertText = "background-color: "; Documentation = Some "Sets background color" }
        { Label = "background-image"; Kind = Property; Detail = "background-image"; InsertText = "background-image: "; Documentation = Some "Sets background image" }
        { Label = "background-repeat"; Kind = Property; Detail = "background-repeat"; InsertText = "background-repeat: "; Documentation = Some "Sets background repeat mode" }
        { Label = "background-position"; Kind = Property; Detail = "background-position"; InsertText = "background-position: "; Documentation = Some "Sets background position" }
        { Label = "background-size"; Kind = Property; Detail = "background-size"; InsertText = "background-size: "; Documentation = Some "Sets background size" }

        // Border
        { Label = "border"; Kind = Property; Detail = "border"; InsertText = "border: "; Documentation = Some "Border shorthand" }
        { Label = "border-color"; Kind = Property; Detail = "border-color"; InsertText = "border-color: "; Documentation = Some "Sets border color" }
        { Label = "border-style"; Kind = Property; Detail = "border-style"; InsertText = "border-style: "; Documentation = Some "Sets border style" }
        { Label = "border-width"; Kind = Property; Detail = "border-width"; InsertText = "border-width: "; Documentation = Some "Sets border width" }
        { Label = "border-radius"; Kind = Property; Detail = "border-radius"; InsertText = "border-radius: "; Documentation = Some "Sets border radius" }
        { Label = "border-collapse"; Kind = Property; Detail = "border-collapse"; InsertText = "border-collapse: "; Documentation = Some "Sets table border collapse" }

        // Flex
        { Label = "flex"; Kind = Property; Detail = "flex"; InsertText = "flex: "; Documentation = Some "Flex shorthand" }
        { Label = "flex-direction"; Kind = Property; Detail = "flex-direction"; InsertText = "flex-direction: "; Documentation = Some "Sets flex direction" }
        { Label = "flex-wrap"; Kind = Property; Detail = "flex-wrap"; InsertText = "flex-wrap: "; Documentation = Some "Sets flex wrap mode" }
        { Label = "flex-grow"; Kind = Property; Detail = "flex-grow"; InsertText = "flex-grow: "; Documentation = Some "Sets flex grow factor" }
        { Label = "flex-shrink"; Kind = Property; Detail = "flex-shrink"; InsertText = "flex-shrink: "; Documentation = Some "Sets flex shrink factor" }
        { Label = "flex-basis"; Kind = Property; Detail = "flex-basis"; InsertText = "flex-basis: "; Documentation = Some "Sets flex basis" }
        { Label = "align-items"; Kind = Property; Detail = "align-items"; InsertText = "align-items: "; Documentation = Some "Sets cross-axis alignment" }
        { Label = "align-self"; Kind = Property; Detail = "align-self"; InsertText = "align-self: "; Documentation = Some "Sets individual cross-axis alignment" }
        { Label = "align-content"; Kind = Property; Detail = "align-content"; InsertText = "align-content: "; Documentation = Some "Sets cross-axis distribution" }
        { Label = "justify-content"; Kind = Property; Detail = "justify-content"; InsertText = "justify-content: "; Documentation = Some "Sets main-axis distribution" }
        { Label = "justify-items"; Kind = Property; Detail = "justify-items"; InsertText = "justify-items: "; Documentation = Some "Sets item justification" }
        { Label = "justify-self"; Kind = Property; Detail = "justify-self"; InsertText = "justify-self: "; Documentation = Some "Sets individual item justification" }
        { Label = "gap"; Kind = Property; Detail = "gap"; InsertText = "gap: "; Documentation = Some "Sets gap between items" }
        { Label = "row-gap"; Kind = Property; Detail = "row-gap"; InsertText = "row-gap: "; Documentation = Some "Sets row gap" }
        { Label = "column-gap"; Kind = Property; Detail = "column-gap"; InsertText = "column-gap: "; Documentation = Some "Sets column gap" }

        // Grid
        { Label = "grid-template-columns"; Kind = Property; Detail = "grid-template-columns"; InsertText = "grid-template-columns: "; Documentation = Some "Defines grid columns" }
        { Label = "grid-template-rows"; Kind = Property; Detail = "grid-template-rows"; InsertText = "grid-template-rows: "; Documentation = Some "Defines grid rows" }
        { Label = "grid-auto-flow"; Kind = Property; Detail = "grid-auto-flow"; InsertText = "grid-auto-flow: "; Documentation = Some "Sets auto-placement algorithm" }

        // Effects
        { Label = "opacity"; Kind = Property; Detail = "opacity"; InsertText = "opacity: "; Documentation = Some "Sets element opacity" }
        { Label = "transform"; Kind = Property; Detail = "transform"; InsertText = "transform: "; Documentation = Some "Applies transformation" }
        { Label = "transition"; Kind = Property; Detail = "transition"; InsertText = "transition: "; Documentation = Some "Transition shorthand" }
        { Label = "animation"; Kind = Property; Detail = "animation"; InsertText = "animation: "; Documentation = Some "Animation shorthand" }
        { Label = "cursor"; Kind = Property; Detail = "cursor"; InsertText = "cursor: "; Documentation = Some "Sets cursor type" }
        { Label = "filter"; Kind = Property; Detail = "filter"; InsertText = "filter: "; Documentation = Some "Applies visual filter" }
        { Label = "backdrop-filter"; Kind = Property; Detail = "backdrop-filter"; InsertText = "backdrop-filter: "; Documentation = Some "Applies backdrop filter" }
        { Label = "pointer-events"; Kind = Property; Detail = "pointer-events"; InsertText = "pointer-events: "; Documentation = Some "Controls pointer event handling" }
        { Label = "resize"; Kind = Property; Detail = "resize"; InsertText = "resize: "; Documentation = Some "Controls element resizability" }
        { Label = "appearance"; Kind = Property; Detail = "appearance"; InsertText = "appearance: "; Documentation = Some "Controls native appearance" }
        { Label = "aspect-ratio"; Kind = Property; Detail = "aspect-ratio"; InsertText = "aspect-ratio: "; Documentation = Some "Sets aspect ratio" }
        { Label = "scroll-behavior"; Kind = Property; Detail = "scroll-behavior"; InsertText = "scroll-behavior: "; Documentation = Some "Sets scroll behavior" }
    ]

    /// Common CSS values for auto-completion.
    let valueCompletions: CompletionItem list = [
        { Label = "flex"; Kind = Value; Detail = "display value"; InsertText = "flex"; Documentation = None }
        { Label = "grid"; Kind = Value; Detail = "display value"; InsertText = "grid"; Documentation = None }
        { Label = "block"; Kind = Value; Detail = "display value"; InsertText = "block"; Documentation = None }
        { Label = "inline"; Kind = Value; Detail = "display value"; InsertText = "inline"; Documentation = None }
        { Label = "inline-block"; Kind = Value; Detail = "display value"; InsertText = "inline-block"; Documentation = None }
        { Label = "none"; Kind = Value; Detail = "universal value"; InsertText = "none"; Documentation = None }
        { Label = "auto"; Kind = Value; Detail = "universal value"; InsertText = "auto"; Documentation = None }
        { Label = "relative"; Kind = Value; Detail = "position value"; InsertText = "relative"; Documentation = None }
        { Label = "absolute"; Kind = Value; Detail = "position value"; InsertText = "absolute"; Documentation = None }
        { Label = "fixed"; Kind = Value; Detail = "position value"; InsertText = "fixed"; Documentation = None }
        { Label = "sticky"; Kind = Value; Detail = "position value"; InsertText = "sticky"; Documentation = None }
        { Label = "center"; Kind = Value; Detail = "alignment value"; InsertText = "center"; Documentation = None }
        { Label = "hidden"; Kind = Value; Detail = "visibility/overflow value"; InsertText = "hidden"; Documentation = None }
        { Label = "solid"; Kind = Value; Detail = "border-style value"; InsertText = "solid"; Documentation = None }
        { Label = "dashed"; Kind = Value; Detail = "border-style value"; InsertText = "dashed"; Documentation = None }
        { Label = "dotted"; Kind = Value; Detail = "border-style value"; InsertText = "dotted"; Documentation = None }
        { Label = "transparent"; Kind = Value; Detail = "color value"; InsertText = "transparent"; Documentation = None }
        { Label = "currentColor"; Kind = Value; Detail = "color value"; InsertText = "currentColor"; Documentation = None }
        { Label = "ellipsis"; Kind = Value; Detail = "text-overflow value"; InsertText = "ellipsis"; Documentation = None }
        { Label = "bold"; Kind = Value; Detail = "font-weight value"; InsertText = "bold"; Documentation = None }
        { Label = "cover"; Kind = Value; Detail = "background-size value"; InsertText = "cover"; Documentation = None }
        { Label = "contain"; Kind = Value; Detail = "background-size value"; InsertText = "contain"; Documentation = None }
        { Label = "stretch"; Kind = Value; Detail = "alignment value"; InsertText = "stretch"; Documentation = None }
        { Label = "baseline"; Kind = Value; Detail = "alignment value"; InsertText = "baseline"; Documentation = None }
        { Label = "wrap"; Kind = Value; Detail = "flex-wrap value"; InsertText = "wrap"; Documentation = None }
        { Label = "nowrap"; Kind = Value; Detail = "flex-wrap value"; InsertText = "nowrap"; Documentation = None }
        { Label = "row"; Kind = Value; Detail = "flex-direction value"; InsertText = "row"; Documentation = None }
        { Label = "column"; Kind = Value; Detail = "flex-direction value"; InsertText = "column"; Documentation = None }
        { Label = "border-box"; Kind = Value; Detail = "box-sizing value"; InsertText = "border-box"; Documentation = None }
        { Label = "content-box"; Kind = Value; Detail = "box-sizing value"; InsertText = "content-box"; Documentation = None }
        { Label = "pointer"; Kind = Value; Detail = "cursor value"; InsertText = "pointer"; Documentation = None }
        { Label = "uppercase"; Kind = Value; Detail = "text-transform value"; InsertText = "uppercase"; Documentation = None }
        { Label = "lowercase"; Kind = Value; Detail = "text-transform value"; InsertText = "lowercase"; Documentation = None }
        { Label = "capitalize"; Kind = Value; Detail = "text-transform value"; InsertText = "capitalize"; Documentation = None }
        { Label = "underline"; Kind = Value; Detail = "text-decoration value"; InsertText = "underline"; Documentation = None }
        { Label = "line-through"; Kind = Value; Detail = "text-decoration value"; InsertText = "line-through"; Documentation = None }
        { Label = "italic"; Kind = Value; Detail = "font-style value"; InsertText = "italic"; Documentation = None }
        { Label = "space-between"; Kind = Value; Detail = "justify-content value"; InsertText = "space-between"; Documentation = None }
        { Label = "space-around"; Kind = Value; Detail = "justify-content value"; InsertText = "space-around"; Documentation = None }
        { Label = "space-evenly"; Kind = Value; Detail = "justify-content value"; InsertText = "space-evenly"; Documentation = None }
        { Label = "flex-start"; Kind = Value; Detail = "alignment value"; InsertText = "flex-start"; Documentation = None }
        { Label = "flex-end"; Kind = Value; Detail = "alignment value"; InsertText = "flex-end"; Documentation = None }
    ]

    /// F# style declarations for auto-complete (let bindings, etc).
    let fsharpSnippetCompletions: CompletionItem list = [
        { Label = "let"; Kind = CssKeyword; Detail = "variable binding"; InsertText = "let $0: "; Documentation = Some "F#-style variable binding" }
        { Label = "@media"; Kind = CssAtRule; Detail = "media query"; InsertText = "@media (min-width: $0px)"; Documentation = Some "Media query block" }
        { Label = "@keyframes"; Kind = CssAtRule; Detail = "keyframes definition"; InsertText = "@keyframes $0"; Documentation = Some "Defines animation keyframes" }
        { Label = "@each"; Kind = CssAtRule; Detail = "each loop"; InsertText = "@each $$0 in ($1)"; Documentation = Some "Iterates over a list" }
        { Label = "@for"; Kind = CssAtRule; Detail = "for loop"; InsertText = "@for $$0 from 1 to $1"; Documentation = Some "Counted loop" }
        { Label = "@if"; Kind = CssAtRule; Detail = "conditional"; InsertText = "@if $0"; Documentation = Some "Conditional block" }
        { Label = "@mixin"; Kind = CssAtRule; Detail = "mixin definition"; InsertText = "@mixin $0"; Documentation = Some "Defines a reusable mixin" }
        { Label = "@include"; Kind = CssAtRule; Detail = "include mixin"; InsertText = "@include $0"; Documentation = Some "Includes a mixin" }
        { Label = "@apply"; Kind = CssAtRule; Detail = "apply utility class"; InsertText = "@apply $0"; Documentation = Some "Applies utility class declarations" }
        { Label = "@use"; Kind = CssAtRule; Detail = "use module"; InsertText = "@use \"$0\""; Documentation = Some "Imports a ZSS module" }
        { Label = "@export"; Kind = CssAtRule; Detail = "export CSS variable"; InsertText = "@export $$0"; Documentation = Some "Exports a variable as CSS custom property" }
    ]

    /// Selector snippet completions.
    let selectorSnippetCompletions: CompletionItem list = [
        { Label = "div"; Kind = Selector; Detail = "element selector"; InsertText = "div"; Documentation = None }
        { Label = "a"; Kind = Selector; Detail = "anchor selector"; InsertText = "a"; Documentation = None }
        { Label = "p"; Kind = Selector; Detail = "paragraph selector"; InsertText = "p"; Documentation = None }
        { Label = "h1-6"; Kind = Selector; Detail = "heading selector"; InsertText = "h$0"; Documentation = None }
        { Label = "nav"; Kind = Selector; Detail = "nav element"; InsertText = "nav"; Documentation = None }
        { Label = "header"; Kind = Selector; Detail = "header element"; InsertText = "header"; Documentation = None }
        { Label = "footer"; Kind = Selector; Detail = "footer element"; InsertText = "footer"; Documentation = None }
        { Label = "main"; Kind = Selector; Detail = "main element"; InsertText = "main"; Documentation = None }
        { Label = "section"; Kind = Selector; Detail = "section element"; InsertText = "section"; Documentation = None }
        { Label = "article"; Kind = Selector; Detail = "article element"; InsertText = "article"; Documentation = None }
        { Label = "aside"; Kind = Selector; Detail = "aside element"; InsertText = "aside"; Documentation = None }
        { Label = "ul"; Kind = Selector; Detail = "unordered list"; InsertText = "ul"; Documentation = None }
        { Label = "ol"; Kind = Selector; Detail = "ordered list"; InsertText = "ol"; Documentation = None }
        { Label = "li"; Kind = Selector; Detail = "list item"; InsertText = "li"; Documentation = None }
        { Label = "table"; Kind = Selector; Detail = "table element"; InsertText = "table"; Documentation = None }
        { Label = "form"; Kind = Selector; Detail = "form element"; InsertText = "form"; Documentation = None }
        { Label = "button"; Kind = Selector; Detail = "button element"; InsertText = "button"; Documentation = None }
        { Label = "input"; Kind = Selector; Detail = "input element"; InsertText = "input"; Documentation = None }
        { Label = "img"; Kind = Selector; Detail = "image element"; InsertText = "img"; Documentation = None }
        { Label = "span"; Kind = Selector; Detail = "span element"; InsertText = "span"; Documentation = None }
    ]

    /// Complete list of all completion items (grouped by kind).
    let allCompletions: CompletionItem list =
        propertyCompletions @ valueCompletions @ fsharpSnippetCompletions @ selectorSnippetCompletions

    /// Get completions filtered by a prefix string.
    let filterByPrefix (prefix: string) (items: CompletionItem list) : CompletionItem list =
        if String.IsNullOrEmpty prefix then items
        else
            items |> List.filter (fun item ->
                item.Label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))

    /// Serialize completions to JSON (for LSP/editor integration).
    let toJson (items: CompletionItem list) : string =
        let kindName = function
            | Property -> "property" | Value -> "value" | Function -> "function"
            | Color -> "color" | Selector -> "selector" | CssAtRule -> "atrule"
            | CssVariable -> "variable" | CssKeyword -> "keyword"
        items
        |> List.map (fun item ->
            let doc = item.Documentation |> Option.map (sprintf "\"documentation\": \"%s\"") |> Option.defaultValue ""
            let docJson = if doc <> "" then sprintf ", %s" doc else ""
            sprintf """{"label":"%s","kind":"%s","detail":"%s","insertText":"%s"%s}"""
                item.Label (kindName item.Kind) item.Detail (item.InsertText.Replace("\"", "\\\"")) docJson)
        |> String.concat ",\n"
        |> sprintf "[\n%s\n]"
