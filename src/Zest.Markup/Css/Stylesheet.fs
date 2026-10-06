namespace Zest.Markup

open System
open System.Text

// ============================================================
// ZCSS F#-style Stylesheet DSL
// ============================================================
// Provides an F# computation expression for writing CSS
// using F#-native syntax with:
//   - stylesheet { ... } block structure
//   - Bracket syntax for property blocks: selector [ prop value ]
//   - Dot notation for pseudo-classes: a.hover
//   - Property-value syntax without colons: bg "#000"
//   - Multiple properties in a single selector block
// ============================================================

/// Represents a single CSS property-value declaration.
type CssDecl =
    { /// CSS property name (e.g., "background", "color", "font-family").
      Property : string
      /// CSS property value (e.g., "#000", "16px", "monospace").
      Value   : string }

/// Represents a CSS rule: a selector with a list of declarations.
type CssRule =
    { /// CSS selector string (e.g., "body", "a:hover", ".container").
      Selector     : string
      /// List of CSS declarations for this rule.
      Declarations : CssDecl list }

/// Selector builder that supports both function-call syntax
/// (selector [ prop value; ... ]) and pseudo-class dot notation
/// (selector.hover, selector.active, etc.).
type Sel(name: string) =

    /// Apply declarations to this selector, producing a CssRule.
    member _.Invoke(decls: CssDecl list) : CssRule =
        { Selector = name; Declarations = decls }

    // ── Pseudo-classes ──────────────────────────────────

    member _.hover        = Sel(name + ":hover")
    member _.active       = Sel(name + ":active")
    member _.focus        = Sel(name + ":focus")
    member _.visited      = Sel(name + ":visited")
    member _.``checked``  = Sel(name + ":checked")
    member _.disabled     = Sel(name + ":disabled")
    member _.enabled      = Sel(name + ":enabled")
    member _.required     = Sel(name + ":required")
    member _.optional     = Sel(name + ":optional")
    member _.readOnly     = Sel(name + ":read-only")
    member _.readWrite    = Sel(name + ":read-write")
    member _.valid        = Sel(name + ":valid")
    member _.invalid      = Sel(name + ":invalid")
    member _.``default``  = Sel(name + ":default")
    member _.inRange      = Sel(name + ":in-range")
    member _.outOfRange   = Sel(name + ":out-of-range")
    member _.placeholderShown = Sel(name + ":placeholder-shown")
    member _.autofill     = Sel(name + ":autofill")
    member _.target       = Sel(name + ":target")
    member _.root         = Sel(name + ":root")
    member _.empty        = Sel(name + ":empty")
    member _.blank        = Sel(name + ":blank")
    member _.firstChild   = Sel(name + ":first-child")
    member _.lastChild    = Sel(name + ":last-child")
    member _.onlyChild    = Sel(name + ":only-child")
    member _.firstOfType  = Sel(name + ":first-of-type")
    member _.lastOfType   = Sel(name + ":last-of-type")
    member _.onlyOfType   = Sel(name + ":only-of-type")
    member _.nthChild(n: int)         = Sel(name + sprintf ":nth-child(%d)" n)
    member _.nthLastChild(n: int)     = Sel(name + sprintf ":nth-last-child(%d)" n)
    member _.nthOfType(n: int)        = Sel(name + sprintf ":nth-of-type(%d)" n)
    member _.nthLastOfType(n: int)    = Sel(name + sprintf ":nth-last-of-type(%d)" n)
    member _.``not``(sel: string)     = Sel(name + sprintf ":not(%s)" sel)
    member _.lang(code: string)       = Sel(name + sprintf ":lang(%s)" code)
    member _.is(sel: string)          = Sel(name + sprintf ":is(%s)" sel)
    member _.where(sel: string)       = Sel(name + sprintf ":where(%s)" sel)
    member _.has(sel: string)         = Sel(name + sprintf ":has(%s)" sel)

    // ── Pseudo-elements ─────────────────────────────────

    member _.before  = Sel(name + "::before")
    member _.after   = Sel(name + "::after")
    member _.firstLetter  = Sel(name + "::first-letter")
    member _.firstLine    = Sel(name + "::first-line")
    member _.selection    = Sel(name + "::selection")
    member _.placeholder  = Sel(name + "::placeholder")
    member _.backdrop     = Sel(name + "::backdrop")
    member _.marker       = Sel(name + "::marker")
    member _.spellingError = Sel(name + "::spelling-error")
    member _.grammarError  = Sel(name + "::grammar-error")

    // ── Attribute selectors ─────────────────────────────

    member this.attr(a: string) = Sel(name + sprintf "[%s]" a)
    member this.attrEq(a: string) (v: string) = Sel(name + sprintf """[%s="%s"]""" a v)
    member this.attrContains(a: string) (v: string) = Sel(name + sprintf """[%s~="%s"]""" a v)
    member this.attrDash(a: string) (v: string) = Sel(name + sprintf """[%s|="%s"]""" a v)
    member this.attrStarts(a: string) (v: string) = Sel(name + sprintf """[%s^="%s"]""" a v)
    member this.attrEnds(a: string) (v: string) = Sel(name + sprintf """[%s$="%s"]""" a v)
    member this.attrSubstr(a: string) (v: string) = Sel(name + sprintf """[%s*="%s"]""" a v)

    // ── Child / descendant combinator (space) ───────────

    member this.descendant(child: Sel) = Sel(name + " " + child.Name)
    member this.child(child: Sel)     = Sel(name + " > " + child.Name)
    member this.adjacent(sib: Sel)    = Sel(name + " + " + sib.Name)
    member this.sibling(sib: Sel)     = Sel(name + " ~ " + sib.Name)

    /// Get the raw selector string.
    member _.Name = name

    override _.ToString() = name

// ============================================================
// ZCSS DSL Module — Property Functions, Selectors, & Builders
// ============================================================

[<AutoOpen>]
module Stylesheet =

    // ── CSS Compilation helpers ─────────────────────────────

    /// Compile a list of CssRules into a CSS string.
    let compileStylesheet (rules: CssRule list) : string =
        let sb = StringBuilder()
        for rule in rules do
            if not (List.isEmpty rule.Declarations) then
                sb.AppendLine(sprintf "%s {" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.AppendLine(sprintf "  %s: %s;" decl.Property decl.Value) |> ignore
                sb.AppendLine("}") |> ignore
        sb.ToString().TrimEnd()

    /// Compile a list of CssRules into a minified CSS string (single line).
    let compileStylesheetMinified (rules: CssRule list) : string =
        let sb = StringBuilder()
        for rule in rules do
            if not (List.isEmpty rule.Declarations) then
                sb.Append(sprintf "%s{" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.Append(sprintf "%s:%s;" decl.Property decl.Value) |> ignore
                sb.Remove(sb.Length - 1, 1) |> ignore
                sb.Append("}") |> ignore
        sb.ToString()

    // ── Compilation diagnostics ───────────────────────────

    /// Result type carrying compiled CSS with optional warnings.
    type CssCompileResult =
        { /// The compiled CSS string.
          Css: string
          /// Non-fatal warnings (e.g. empty rules skipped).
          Warnings: string list }

    /// Compile with diagnostics — identical to compileStylesheet
    /// but also reports skipped empty rules as warnings.
    let compileStylesheetWithDiagnostics (rules: CssRule list) : CssCompileResult =
        let sb = StringBuilder()
        let warned = ResizeArray<string>()
        for rule in rules do
            if List.isEmpty rule.Declarations then
                warned.Add(sprintf "Skipped empty rule: '%s' (no declarations)" rule.Selector)
            else
                sb.AppendLine(sprintf "%s {" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.AppendLine(sprintf "  %s: %s;" decl.Property decl.Value) |> ignore
                sb.AppendLine("}") |> ignore
        { Css = sb.ToString().TrimEnd(); Warnings = warned |> List.ofSeq }

    // ── Value validation helper ───────────────────────────

    /// Lightweight value-format helper for common CSS data types.
    /// Returns None if the value looks valid, Some(msg) with a warning otherwise.
    /// Covers length units (px, em, rem, %, vw, vh), colours, and times (s, ms).
    let validateValue (propName: string) (value: string) : string option =
        let trimmed = value.Trim()
        if String.IsNullOrWhiteSpace trimmed then
            Some(sprintf "Empty value for property '%s'" propName)
        else
            let isValidLength (v: string) =
                let lower = v.ToLowerInvariant()
                lower.EndsWith("px") || lower.EndsWith("em") || lower.EndsWith("rem")
                || lower.EndsWith("%") || lower.EndsWith("vw") || lower.EndsWith("vh")
                || lower.EndsWith("vmin") || lower.EndsWith("vmax") || lower.EndsWith("ch")
                || lower.EndsWith("ex") || lower.EndsWith("cm") || lower.EndsWith("mm")
                || lower.EndsWith("in") || lower.EndsWith("pt") || lower.EndsWith("pc")
                || lower = "0" || lower = "auto" || lower = "inherit" || lower = "initial"
                || lower = "unset" || lower = "revert"
            let isKnownLengthProperty (p: string) =
                let lower = p.ToLowerInvariant()
                lower.Contains("width") || lower.Contains("height") || lower.Contains("size")
                || lower.Contains("margin") || lower.Contains("padding") || lower.Contains("gap")
                || lower.Contains("radius") || lower.Contains("spacing")
                || lower = "top" || lower = "right" || lower = "bottom" || lower = "left"
                || lower = "font-size" || lower = "line-height" || lower = "letter-spacing"
                || lower = "word-spacing" || lower = "text-indent"
                || lower.Contains("offset")
            if isKnownLengthProperty propName && not (isValidLength trimmed) then
                Some(sprintf "Suspicious length value '%s' for property '%s' — expected unit like px, em, rem, %%" trimmed propName)
            else None

    // ── Stylesheet Computation Expression Builder ───────────

    type StylesheetBuilder() =
        member _.Yield(rule: CssRule) : CssRule list = [rule]
        member _.Yield(rules: CssRule list) : CssRule list = rules
        member _.Combine(a: CssRule list, b: CssRule list) : CssRule list = a @ b
        member _.Delay(f: unit -> CssRule list) = f
        member _.Zero() : CssRule list = []
        member _.For(xs: 'a seq, f: 'a -> CssRule list) : CssRule list =
            xs |> Seq.collect f |> Seq.toList
        member _.Run(rules: CssRule list) : string =
            compileStylesheet rules

    /// The primary stylesheet computation expression builder.
    /// Usage:
    ///   let myCss = stylesheet {
    ///       body [ bg "#000"; color "#0f0"; fontFamily "monospace" ]
    ///       a.hover [ color "#0ff" ]
    ///       cls "container" [ maxWidth "1200px"; margin "0 auto" ]
    ///   }
    let stylesheet = StylesheetBuilder()

    // ── CSS Property Functions ──────────────────────────────

    // Background
    let bg           v = { Property = "background";            Value = v }
    let bgColor      v = { Property = "background-color";      Value = v }
    let bgImage      v = { Property = "background-image";      Value = v }
    let bgRepeat     v = { Property = "background-repeat";     Value = v }
    let bgPosition   v = { Property = "background-position";   Value = v }
    let bgSize       v = { Property = "background-size";       Value = v }
    let bgAttachment v = { Property = "background-attachment"; Value = v }
    let bgClip       v = { Property = "background-clip";       Value = v }
    let bgOrigin     v = { Property = "background-origin";     Value = v }
    let bgBlendMode  v = { Property = "background-blend-mode"; Value = v }

    // Color & Text
    let color                v = { Property = "color";          Value = v }
    let opacity              v = { Property = "opacity";        Value = v }

    // Typography
    let fontFamily     v = { Property = "font-family";     Value = v }
    let fontSize       v = { Property = "font-size";       Value = v }
    let fontWeight     v = { Property = "font-weight";     Value = v }
    let fontStyle      v = { Property = "font-style";      Value = v }
    let fontVariant    v = { Property = "font-variant";    Value = v }
    let fontStretch    v = { Property = "font-stretch";    Value = v }
    let lineHeight     v = { Property = "line-height";     Value = v }
    let letterSpacing  v = { Property = "letter-spacing";  Value = v }
    let wordSpacing    v = { Property = "word-spacing";    Value = v }
    let textAlign      v = { Property = "text-align";      Value = v }
    let textDecoration v = { Property = "text-decoration"; Value = v }
    let textTransform  v = { Property = "text-transform";  Value = v }
    let textIndent     v = { Property = "text-indent";     Value = v }
    let textOverflow   v = { Property = "text-overflow";   Value = v }
    let textShadow     v = { Property = "text-shadow";     Value = v }
    let textWrap       v = { Property = "text-wrap";       Value = v }
    let whiteSpace     v = { Property = "white-space";     Value = v }
    let wordBreak      v = { Property = "word-break";      Value = v }
    let overflowWrap   v = { Property = "overflow-wrap";   Value = v }
    let hyphens        v = { Property = "hyphens";         Value = v }
    let verticalAlign  v = { Property = "vertical-align";  Value = v }

    // Box Model
    let width         v = { Property = "width";           Value = v }
    let height        v = { Property = "height";          Value = v }
    let minWidth      v = { Property = "min-width";       Value = v }
    let maxWidth      v = { Property = "max-width";       Value = v }
    let minHeight     v = { Property = "min-height";      Value = v }
    let maxHeight     v = { Property = "max-height";      Value = v }
    let margin        v = { Property = "margin";          Value = v }
    let marginTop     v = { Property = "margin-top";      Value = v }
    let marginRight   v = { Property = "margin-right";    Value = v }
    let marginBottom  v = { Property = "margin-bottom";   Value = v }
    let marginLeft    v = { Property = "margin-left";     Value = v }
    let padding       v = { Property = "padding";         Value = v }
    let paddingTop    v = { Property = "padding-top";     Value = v }
    let paddingRight  v = { Property = "padding-right";   Value = v }
    let paddingBottom v = { Property = "padding-bottom";  Value = v }
    let paddingLeft   v = { Property = "padding-left";    Value = v }
    let boxSizing     v = { Property = "box-sizing";      Value = v }
    let boxShadow     v = { Property = "box-shadow";      Value = v }

    // Border
    let border               v = { Property = "border";          Value = v }
    let borderTop           v = { Property = "border-top";      Value = v }
    let borderRight         v = { Property = "border-right";    Value = v }
    let borderBottom        v = { Property = "border-bottom";   Value = v }
    let borderLeft          v = { Property = "border-left";     Value = v }
    let borderColor         v = { Property = "border-color";    Value = v }
    let borderWidth         v = { Property = "border-width";    Value = v }
    let borderStyle         v = { Property = "border-style";    Value = v }
    let borderRadius        v = { Property = "border-radius";   Value = v }
    let borderTopLeftRadius     v = { Property = "border-top-left-radius";     Value = v }
    let borderTopRightRadius    v = { Property = "border-top-right-radius";    Value = v }
    let borderBottomLeftRadius  v = { Property = "border-bottom-left-radius";  Value = v }
    let borderBottomRightRadius v = { Property = "border-bottom-right-radius"; Value = v }
    let outline             v = { Property = "outline";         Value = v }
    let outlineColor        v = { Property = "outline-color";   Value = v }
    let outlineWidth        v = { Property = "outline-width";   Value = v }
    let outlineStyle        v = { Property = "outline-style";   Value = v }
    let outlineOffset       v = { Property = "outline-offset";  Value = v }

    // Display & Positioning
    let display        v = { Property = "display";         Value = v }
    let position       v = { Property = "position";        Value = v }
    let top            v = { Property = "top";             Value = v }
    let right          v = { Property = "right";           Value = v }
    let bottom         v = { Property = "bottom";          Value = v }
    let left           v = { Property = "left";            Value = v }
    let zIndex         v = { Property = "z-index";         Value = v }
    // Named `cssFloat` (not `float`) because this module is [<AutoOpen>] in the
    // `Zest.Markup` namespace and `float` would shadow F#'s built-in conversion.
    let cssFloat       v = { Property = "float";           Value = v }
    let clear          v = { Property = "clear";           Value = v }
    let overflow       v = { Property = "overflow";        Value = v }
    let overflowX      v = { Property = "overflow-x";      Value = v }
    let overflowY      v = { Property = "overflow-y";      Value = v }
    let visibility     v = { Property = "visibility";      Value = v }
    let objectFit      v = { Property = "object-fit";      Value = v }
    let objectPosition v = { Property = "object-position"; Value = v }
    let aspectRatio    v = { Property = "aspect-ratio";    Value = v }

    // Flexbox
    let flex           v = { Property = "flex";            Value = v }
    let flexDirection  v = { Property = "flex-direction";  Value = v }
    let flexWrap       v = { Property = "flex-wrap";       Value = v }
    let flexFlow       v = { Property = "flex-flow";       Value = v }
    let flexGrow       v = { Property = "flex-grow";       Value = v }
    let flexShrink     v = { Property = "flex-shrink";     Value = v }
    let flexBasis      v = { Property = "flex-basis";      Value = v }
    let justifyContent v = { Property = "justify-content"; Value = v }
    let alignItems     v = { Property = "align-items";     Value = v }
    let alignContent   v = { Property = "align-content";   Value = v }
    let alignSelf      v = { Property = "align-self";      Value = v }
    let justifyItems   v = { Property = "justify-items";   Value = v }
    let justifySelf    v = { Property = "justify-self";    Value = v }
    let order          v = { Property = "order";           Value = v }
    let gap            v = { Property = "gap";             Value = v }
    let rowGap         v = { Property = "row-gap";         Value = v }
    let columnGap      v = { Property = "column-gap";      Value = v }
    let placeItems     v = { Property = "place-items";     Value = v }
    let placeContent   v = { Property = "place-content";   Value = v }
    let placeSelf      v = { Property = "place-self";      Value = v }

    // Grid
    let grid                v = { Property = "grid";                  Value = v }
    let gridTemplateColumns v = { Property = "grid-template-columns"; Value = v }
    let gridTemplateRows    v = { Property = "grid-template-rows";    Value = v }
    let gridTemplateAreas   v = { Property = "grid-template-areas";   Value = v }
    let gridTemplate        v = { Property = "grid-template";         Value = v }
    let gridAutoColumns     v = { Property = "grid-auto-columns";     Value = v }
    let gridAutoRows        v = { Property = "grid-auto-rows";        Value = v }
    let gridAutoFlow        v = { Property = "grid-auto-flow";        Value = v }
    let gridColumn          v = { Property = "grid-column";           Value = v }
    let gridRow             v = { Property = "grid-row";              Value = v }
    let gridColumnStart     v = { Property = "grid-column-start";     Value = v }
    let gridColumnEnd       v = { Property = "grid-column-end";       Value = v }
    let gridRowStart        v = { Property = "grid-row-start";        Value = v }
    let gridRowEnd          v = { Property = "grid-row-end";          Value = v }
    let gridArea            v = { Property = "grid-area";             Value = v }

    // Transform & Transition
    let transform          v = { Property = "transform";       Value = v }
    let transformOrigin    v = { Property = "transform-origin"; Value = v }
    let transition         v = { Property = "transition";      Value = v }
    let transitionDuration v = { Property = "transition-duration";  Value = v }
    let transitionProperty v = { Property = "transition-property";  Value = v }
    let transitionTiming   v = { Property = "transition-timing-function"; Value = v }
    let transitionDelay    v = { Property = "transition-delay";      Value = v }

    // Animation
    let animation            v = { Property = "animation";        Value = v }
    let animationName        v = { Property = "animation-name";   Value = v }
    let animationDuration    v = { Property = "animation-duration"; Value = v }
    let animationTiming      v = { Property = "animation-timing-function"; Value = v }
    let animationDelay       v = { Property = "animation-delay";  Value = v }
    let animationIteration   v = { Property = "animation-iteration-count"; Value = v }
    let animationDirection   v = { Property = "animation-direction"; Value = v }
    let animationFillMode    v = { Property = "animation-fill-mode";  Value = v }
    let animationPlayState   v = { Property = "animation-play-state"; Value = v }

    // Filter & Effects
    let filter          v = { Property = "filter";          Value = v }
    let backdropFilter  v = { Property = "backdrop-filter"; Value = v }
    let clipPath        v = { Property = "clip-path";       Value = v }
    let mixBlendMode    v = { Property = "mix-blend-mode";  Value = v }
    let isolation       v = { Property = "isolation";       Value = v }

    // Cursor & Interaction
    let cursor          v = { Property = "cursor";          Value = v }
    let pointerEvents   v = { Property = "pointer-events";  Value = v }
    let userSelect      v = { Property = "user-select";     Value = v }
    let resize          v = { Property = "resize";          Value = v }
    let caretColor      v = { Property = "caret-color";     Value = v }
    let scrollBehavior  v = { Property = "scroll-behavior"; Value = v }
    let scrollbarWidth  v = { Property = "scrollbar-width"; Value = v }
    let scrollbarColor  v = { Property = "scrollbar-color"; Value = v }

    // Lists & Counters
    let listStyle          v = { Property = "list-style";            Value = v }
    let listStyleType      v = { Property = "list-style-type";       Value = v }
    let listStylePosition  v = { Property = "list-style-position";   Value = v }
    let listStyleImage     v = { Property = "list-style-image";      Value = v }
    let counterReset       v = { Property = "counter-reset";         Value = v }
    let counterIncrement   v = { Property = "counter-increment";     Value = v }
    let counterSet         v = { Property = "counter-set";           Value = v }

    // Tables
    let tableLayout        v = { Property = "table-layout";    Value = v }
    let borderCollapse     v = { Property = "border-collapse"; Value = v }
    let borderSpacing      v = { Property = "border-spacing";  Value = v }
    let captionSide        v = { Property = "caption-side";    Value = v }
    let emptyCells         v = { Property = "empty-cells";     Value = v }

    // Content
    let content            v = { Property = "content";         Value = v }
    let quotes             v = { Property = "quotes";          Value = v }

    // Print
    let pageBreakBefore    v = { Property = "page-break-before"; Value = v }
    let pageBreakAfter     v = { Property = "page-break-after";  Value = v }
    let pageBreakInside    v = { Property = "page-break-inside"; Value = v }

    // Modern
    let willChange           v = { Property = "will-change";     Value = v }
    let contain              v = { Property = "contain";         Value = v }
    let containIntrinsicSize v = { Property = "contain-intrinsic-size"; Value = v }
    let contentVisibility    v = { Property = "content-visibility"; Value = v }
    
    // Custom property / variable
    let var name value = { Property = sprintf "--%s" name; Value = value }

    /// Create a declaration with an explicit CSS property name.
    let prop (name: string) (value: string) = { Property = name; Value = value }

    // ── Pre-defined Selectors ───────────────────────────────
    // Moved to a separate explicit module to avoid shadowing
    // the HTML DSL functions in Dsl.fs (e.g., `a`, `div`, `h1`).
    // Open `Stylesheet.Selectors` explicitly when writing stylesheets.

    /// Pre-defined HTML element CSS selectors.
    /// Open this module explicitly: `open Stylesheet.Selectors`
    module Selectors =
        let allElements = Sel("*")
        let html        = Sel("html")
        let body        = Sel("body")
        let head        = Sel("head")
        let a           = Sel("a")
        let abbr        = Sel("abbr")
        let address     = Sel("address")
        let area        = Sel("area")
        let article     = Sel("article")
        let aside       = Sel("aside")
        let audio       = Sel("audio")
        let b           = Sel("b")
        let ``base``    = Sel("base")
        let bdi         = Sel("bdi")
        let bdo         = Sel("bdo")
        let blockquote  = Sel("blockquote")
        let br          = Sel("br")
        let button      = Sel("button")
        let canvas      = Sel("canvas")
        let caption     = Sel("caption")
        let cite        = Sel("cite")
        let code        = Sel("code")
        let col         = Sel("col")
        let colgroup    = Sel("colgroup")
        let data        = Sel("data")
        let datalist    = Sel("datalist")
        let dd          = Sel("dd")
        let del         = Sel("del")
        let details     = Sel("details")
        let dfn         = Sel("dfn")
        let dialog      = Sel("dialog")
        let div         = Sel("div")
        let dl      = Sel("dl")
        let dt          = Sel("dt")
        let em          = Sel("em")
        let embed       = Sel("embed")
        let fieldset    = Sel("fieldset")
        let figcaption  = Sel("figcaption")
        let figure      = Sel("figure")
        let footer      = Sel("footer")
        let form        = Sel("form")
        let h1          = Sel("h1")
        let h2          = Sel("h2")
        let h3          = Sel("h3")
        let h4          = Sel("h4")
        let h5          = Sel("h5")
        let h6          = Sel("h6")
        let header      = Sel("header")
        let hgroup      = Sel("hgroup")
        let hr          = Sel("hr")
        let i           = Sel("i")
        let iframe      = Sel("iframe")
        let img         = Sel("img")
        let input       = Sel("input")
        let ins         = Sel("ins")
        let kbd         = Sel("kbd")
        let label       = Sel("label")
        let legend      = Sel("legend")
        let li          = Sel("li")
        let link        = Sel("link")
        let main        = Sel("main")
        let map         = Sel("map")
        let mark        = Sel("mark")
        let menu        = Sel("menu")
        let meta        = Sel("meta")
        let meter       = Sel("meter")
        let nav         = Sel("nav")
        let noscript    = Sel("noscript")
        let ``object``  = Sel("object")
        let ol          = Sel("ol")
        let optgroup    = Sel("optgroup")
        let option      = Sel("option")
        let output      = Sel("output")
        let p           = Sel("p")
        let picture     = Sel("picture")
        let pre         = Sel("pre")
        let progress    = Sel("progress")
        let q           = Sel("q")
        let rp          = Sel("rp")
        let rt          = Sel("rt")
        let ruby        = Sel("ruby")
        let s           = Sel("s")
        let samp        = Sel("samp")
        let script      = Sel("script")
        let section     = Sel("section")
        let ``select``  = Sel("select")
        let small       = Sel("small")
        let source      = Sel("source")
        let span        = Sel("span")
        let strong      = Sel("strong")
        let style       = Sel("style")
        let sub         = Sel("sub")
        let summary     = Sel("summary")
        let sup         = Sel("sup")
        let table       = Sel("table")
        let tbody       = Sel("tbody")
        let td          = Sel("td")
        let template    = Sel("template")
        let textarea    = Sel("textarea")
        let tfoot       = Sel("tfoot")
        let th          = Sel("th")
        let thead       = Sel("thead")
        let time        = Sel("time")
        let title       = Sel("title")
        let tr          = Sel("tr")
        let track       = Sel("track")
        let u           = Sel("u")
        let ul          = Sel("ul")
        let varEl       = Sel("var")
        let video       = Sel("video")
        let wbr         = Sel("wbr")

        // ── ID and Class selector helpers ───────────────────────

        /// Create a class selector (.className).
        let cls (name: string) = Sel(sprintf ".%s" name)

        /// Create an ID selector (#idName).
        let id (name: string) = Sel(sprintf "#%s" name)

        /// Create a selector with attribute [attr].
        let attrSel (name: string) = Sel(sprintf "[%s]" name)

        /// Combine multiple selectors with comma (e.g., "h1, h2, h3").
        let selectors (sels: Sel list) =
            let combined = sels |> List.map (fun s -> s.Name) |> String.concat ", "
            Sel(combined)

        /// Create a raw selector from a string.
        let rawSel (selector: string) = Sel(selector)
        
        // ── Responsive breakpoint selectors ────────────────────────
        
        /// Tailwind-style responsive breakpoints.
        let sm  = Sel("(min-width: 640px)")
        let md  = Sel("(min-width: 768px)")
        let lg  = Sel("(min-width: 1024px)")
        let xl  = Sel("(min-width: 1280px)")
        let xxl = Sel("(min-width: 1536px)")
        
        // ── State selectors ─────────────────────────────────────
        
        /// Focus-visible state selector.
        let focusVisible = Sel(":focus-visible")
        /// Focus-within state selector.
        let focusWithin = Sel(":focus-within")
        /// Hover state for touch devices.
        let hoverHover = Sel(":hover:hover")

    // ── At-Rule Functions ───────────────────────────────────

    /// Wrap a list of rules in a @media query.
    let media (query: string) (rules: CssRule list) : string =
        let sb = StringBuilder()
        sb.AppendLine(sprintf "@media %s {" query) |> ignore
        for rule in rules do
            if not (List.isEmpty rule.Declarations) then
                sb.AppendLine(sprintf "  %s {" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.AppendLine(sprintf "    %s: %s;" decl.Property decl.Value) |> ignore
                sb.AppendLine("  }") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString().TrimEnd()

    /// Wrap a list of rules in a @keyframes block.
    let keyframes (name: string) (frames: (string * CssDecl list) list) : string =
        let sb = StringBuilder()
        sb.AppendLine(sprintf "@keyframes %s {" name) |> ignore
        for (stop, decls) in frames do
            sb.AppendLine(sprintf "  %s {" stop) |> ignore
            for decl in decls do
                sb.AppendLine(sprintf "    %s: %s;" decl.Property decl.Value) |> ignore
            sb.AppendLine("  }") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString().TrimEnd()

    /// Wrap a list of rules in a @supports block.
    let supports (condition: string) (rules: CssRule list) : string =
        let sb = StringBuilder()
        sb.AppendLine(sprintf "@supports %s {" condition) |> ignore
        for rule in rules do
            if not (List.isEmpty rule.Declarations) then
                sb.AppendLine(sprintf "  %s {" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.AppendLine(sprintf "    %s: %s;" decl.Property decl.Value) |> ignore
                sb.AppendLine("  }") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString().TrimEnd()

    /// Generate a @font-face rule from descriptor declarations.
    /// The declarations should use CSS font descriptor properties
    /// (font-family, src, font-weight, font-style, font-display, etc.).
    ///
    /// Example:
    ///   fontFace [
    ///       prop "font-family" "\"My Font\""
    ///       prop "src" "url('/fonts/myfont.woff2') format('woff2')"
    ///       prop "font-display" "swap"
    ///   ]
    let fontFace (decls: CssDecl list) : string =
        if List.isEmpty decls then ""
        else
            let sb = StringBuilder()
            sb.AppendLine("@font-face {") |> ignore
            for decl in decls do
                sb.AppendLine(sprintf "  %s: %s;" decl.Property decl.Value) |> ignore
            sb.AppendLine("}") |> ignore
            sb.ToString().TrimEnd()

    /// Generate a @import at-rule for an external stylesheet.
    ///
    /// Example:
    ///   cssImport "url('/css/reset.css')"
    ///   cssImport (sprintf "\"%s\"" "https://fonts.googleapis.com/css2?family=Inter")
    let cssImport (url: string) : string =
        sprintf "@import %s;" url

    /// Generate a @import with a media query condition.
    ///
    /// Example:
    ///   cssImportMedia "url('/css/print.css')" "print"
    let cssImportMedia (url: string) (mediaQuery: string) : string =
        sprintf "@import %s %s;" url mediaQuery

    // ── Phase 5: responsive & environment-aware styles ─────────────

    /// Print-only styles wrapped in `@media print`.
    ///
    ///   printStyles [ cls "no-print" [ display "none" ] ]
    let printStyles (rules: CssRule list) : string =
        media "print" rules

    /// Dark colour-scheme styles wrapped in
    /// `@media (prefers-color-scheme: dark)`.
    let darkModeStyles (rules: CssRule list) : string =
        media "(prefers-color-scheme: dark)" rules

    /// Accessibility styles for users who request reduced motion, wrapped in
    /// `@media (prefers-reduced-motion: reduce)`.
    ///
    ///   prefersReducedMotion [ cls "anim" [ animation "none" ] ]
    let prefersReducedMotion (rules: CssRule list) : string =
        media "(prefers-reduced-motion: reduce)" rules

    /// Element-based media queries via `@container`. `query` may include a
    /// container name, e.g. "card (min-width: 400px)".
    ///
    ///   containerQueries "(min-width: 400px)" [ cls "item" [ width "100%" ] ]
    let containerQueries (query: string) (rules: CssRule list) : string =
        let sb = StringBuilder()
        sb.AppendLine(sprintf "@container %s {" query) |> ignore
        for rule in rules do
            if not (List.isEmpty rule.Declarations) then
                sb.AppendLine(sprintf "  %s {" rule.Selector) |> ignore
                for decl in rule.Declarations do
                    sb.AppendLine(sprintf "    %s: %s;" decl.Property decl.Value) |> ignore
                sb.AppendLine("  }") |> ignore
        sb.AppendLine("}") |> ignore
        sb.ToString().TrimEnd()

    /// Declare the supported colour schemes on `:root` via the standard
    /// `color-scheme` property, so native form controls and scrollbars follow
    /// the OS preference. `scheme` is one of "light", "dark", or "light dark".
    ///
    ///   colorSchemeStyles "light dark"   →  :root { color-scheme: light dark; }
    let colorSchemeStyles (scheme: string) : string =
        if String.IsNullOrWhiteSpace scheme then ""
        else ":root { color-scheme: " + scheme.Trim() + "; }"
