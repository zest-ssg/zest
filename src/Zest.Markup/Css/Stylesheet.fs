namespace Zest.Markup

open System
open System.Text

// ============================================================
// ZCSS F#-style Stylesheet DSL
// ============================================================
// Provides an F# computation expression for writing CSS
// using F#-native syntax with:
//   - stylesheet { ... } block structure
//   - Selector application: selector [ prop value ]
//   - Pseudo-class/element combinators: hover "a" [ color "#0ff" ]
//   - Property-value syntax without colons: bg "#000"
//   - Multiple properties in a single selector block
//
// Selector helpers live in `Stylesheet.Selectors` and are *functions*
// of `CssDecl list -> CssRule`, so `body [ bg "#000" ]` is a plain F#
// application. F# has no `Invoke`-application sugar, so a selector
// cannot be both a value with members and something you can apply —
// the combinators below replace that (removed) dot-notation form.
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

// A selector is represented as a plain function `CssDecl list -> CssRule`.
// That is the only shape F# can apply with bracket syntax, so the former
// `Sel` class (which needed `Invoke`-application sugar that F# does not
// have) was removed in favour of the functions in `Stylesheet.Selectors`.

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

    // `Delay` must return the monadic value itself (like `AsyncBuilder`), not a
    // thunk: F# threads the delayed continuation straight into `Combine`, which
    // takes `CssRule list`. `Yield` must stay single — CE member resolution does
    // not pick between `Yield` overloads, and a second overload silently broke
    // every usage form.
    type StylesheetBuilder() =
        member _.Yield(rule: CssRule) : CssRule list = [rule]
        member _.Combine(a: CssRule list, b: CssRule list) : CssRule list = a @ b
        member _.Zero() : CssRule list = []
        member _.Delay(f: unit -> CssRule list) : CssRule list = f ()
        /// `for tag in [ "h1"; "h2" ] -> rawSel tag [ color "#000" ]`
        member _.For(xs: 'a seq, f: 'a -> CssRule list) : CssRule list =
            xs |> Seq.collect f |> Seq.toList
        member _.Run(rules: CssRule list) : string =
            compileStylesheet rules

    /// The primary stylesheet computation expression builder.
    /// Usage (with `open Zest.Markup.Stylesheet.Selectors`):
    ///   let myCss = stylesheet {
    ///       body [ bg "#000"; color "#0f0"; fontFamily "monospace" ]
    ///       hover "a" [ color "#0ff" ]
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
    /// Custom-property emitter (`--name: value`). Named `cssVar`, not `var`,
    /// because `Stylesheet` is auto-opened and `var` is the `<var>` element
    /// builder in the equally auto-opened `Dsl` module (see R5).
    let cssVar name value = { Property = sprintf "--%s" name; Value = value }

    /// Create a declaration with an explicit CSS property name.
    let prop (name: string) (value: string) = { Property = name; Value = value }

    // ── Pre-defined Selectors ───────────────────────────────
    // A separate *explicit* module, because these names deliberately shadow
    // the HTML DSL functions in Dsl.fs (e.g., `a`, `div`, `h1`).
    // Open `Stylesheet.Selectors` explicitly when writing stylesheets.

    /// Pre-defined HTML element CSS selectors, plus class/ID and
    /// pseudo-class helpers. Every entry is a `CssDecl list -> CssRule`
    /// function, so a rule reads `body [ bg "#000" ]`.
    /// Open this module explicitly: `open Stylesheet.Selectors`
    module Selectors =

        // ── Core constructors ───────────────────────────────────

        /// Build a rule from a raw selector string and declarations:
        ///   rule ".card" [ padding "1rem" ]
        let rule (selector: string) (decls: CssDecl list) : CssRule =
            { Selector = selector; Declarations = decls }

        /// Build a rule from a raw selector string — alias of `rule`.
        let rawSel (selector: string) : CssDecl list -> CssRule = rule selector

        /// Every element selector below is `CssDecl list -> CssRule`.
        let allElements : CssDecl list -> CssRule = rule "*"
        let html        = rule "html"
        let body        = rule "body"
        let head        = rule "head"
        let a           = rule "a"
        let abbr        = rule "abbr"
        let address     = rule "address"
        let area        = rule "area"
        let article     = rule "article"
        let aside       = rule "aside"
        let audio       = rule "audio"
        let b           = rule "b"
        let ``base``    = rule "base"
        let bdi         = rule "bdi"
        let bdo         = rule "bdo"
        let blockquote  = rule "blockquote"
        let br          = rule "br"
        let button      = rule "button"
        let canvas      = rule "canvas"
        let caption     = rule "caption"
        let cite        = rule "cite"
        let code        = rule "code"
        let col         = rule "col"
        let colgroup    = rule "colgroup"
        let data        = rule "data"
        let datalist    = rule "datalist"
        let dd          = rule "dd"
        let del         = rule "del"
        let details     = rule "details"
        let dfn         = rule "dfn"
        let dialog      = rule "dialog"
        let div         = rule "div"
        let dl          = rule "dl"
        let dt          = rule "dt"
        let em          = rule "em"
        let embed       = rule "embed"
        let fieldset    = rule "fieldset"
        let figcaption  = rule "figcaption"
        let figure      = rule "figure"
        let footer      = rule "footer"
        let form        = rule "form"
        let h1          = rule "h1"
        let h2          = rule "h2"
        let h3          = rule "h3"
        let h4          = rule "h4"
        let h5          = rule "h5"
        let h6          = rule "h6"
        let header      = rule "header"
        let hgroup      = rule "hgroup"
        let hr          = rule "hr"
        let i           = rule "i"
        let iframe      = rule "iframe"
        let img         = rule "img"
        let input       = rule "input"
        let ins         = rule "ins"
        let kbd         = rule "kbd"
        let label       = rule "label"
        let legend      = rule "legend"
        let li          = rule "li"
        let link        = rule "link"
        let main        = rule "main"
        let map         = rule "map"
        let mark        = rule "mark"
        let menu        = rule "menu"
        let meta        = rule "meta"
        let meter       = rule "meter"
        let nav         = rule "nav"
        let noscript    = rule "noscript"
        let ``object``  = rule "object"
        let ol          = rule "ol"
        let optgroup    = rule "optgroup"
        let option      = rule "option"
        let output      = rule "output"
        let p           = rule "p"
        let picture     = rule "picture"
        let pre         = rule "pre"
        let progress    = rule "progress"
        let q           = rule "q"
        let rp          = rule "rp"
        let rt          = rule "rt"
        let ruby        = rule "ruby"
        let s           = rule "s"
        let samp        = rule "samp"
        let script      = rule "script"
        let section     = rule "section"
        let ``select``  = rule "select"
        let small       = rule "small"
        let source      = rule "source"
        let span        = rule "span"
        let strong      = rule "strong"
        let style       = rule "style"
        let sub         = rule "sub"
        let summary     = rule "summary"
        let sup         = rule "sup"
        let table       = rule "table"
        let tbody       = rule "tbody"
        let td          = rule "td"
        let template    = rule "template"
        let textarea    = rule "textarea"
        let tfoot       = rule "tfoot"
        let th          = rule "th"
        let thead       = rule "thead"
        let time        = rule "time"
        let title       = rule "title"
        let tr          = rule "tr"
        let track       = rule "track"
        let u           = rule "u"
        let ul          = rule "ul"
        let varEl       = rule "var"
        let video       = rule "video"
        let wbr         = rule "wbr"

        // ── ID and Class selector helpers ───────────────────────

        /// Create a class selector: `cls "card" [ padding "1rem" ]`.
        let cls (name: string) : CssDecl list -> CssRule = rule ("." + name)

        /// Create an ID selector: `id "main" [ maxWidth "60rem" ]`.
        let id (name: string) : CssDecl list -> CssRule = rule ("#" + name)

        /// Create an attribute selector: `attrSel "open" [ display "block" ]`.
        let attrSel (name: string) : CssDecl list -> CssRule = rule ("[" + name + "]")

        /// Combine multiple selectors with a comma:
        ///   selectors [ "h1"; "h2"; "h3" ] [ color "#000" ]
        let selectors (sels: string list) : CssDecl list -> CssRule =
            rule (String.concat ", " sels)

        // ── Pseudo-class / pseudo-element combinators ───────────
        // The former dot-notation (`a.hover [ ... ]`) needed a value that is
        // both a member carrier and applicable, which F# cannot express.
        // These combinators take the base selector as a string instead:
        //   hover "a" [ color "#0ff" ]

        /// Append an arbitrary pseudo suffix to a selector:
        ///   pseudo ":not(.legacy)" "a" [ color "#0ff" ]
        let pseudo (suffix: string) (selector: string) (decls: CssDecl list) : CssRule =
            rule (selector + suffix) decls

        let hover        : string -> CssDecl list -> CssRule = pseudo ":hover"
        let active       : string -> CssDecl list -> CssRule = pseudo ":active"
        let focus        : string -> CssDecl list -> CssRule = pseudo ":focus"
        let visited      : string -> CssDecl list -> CssRule = pseudo ":visited"
        let focusVisible : string -> CssDecl list -> CssRule = pseudo ":focus-visible"
        let focusWithin  : string -> CssDecl list -> CssRule = pseudo ":focus-within"
        let firstChild   : string -> CssDecl list -> CssRule = pseudo ":first-child"
        let lastChild    : string -> CssDecl list -> CssRule = pseudo ":last-child"
        let before       : string -> CssDecl list -> CssRule = pseudo "::before"
        let after        : string -> CssDecl list -> CssRule = pseudo "::after"

        /// Functional pseudo-class: `nthChild 2 "li" [ color "#0ff" ]`.
        let nthChild (n: int) (selector: string) (decls: CssDecl list) : CssRule =
            rule (sprintf "%s:nth-child(%d)" selector n) decls

        // ── Responsive breakpoint media queries ─────────────────

        /// Tailwind-style responsive breakpoints, for use with `media`:
        ///   media md [ cls "card" [ width "100%" ] ]
        let sm  = "(min-width: 640px)"
        let md  = "(min-width: 768px)"
        let lg  = "(min-width: 1024px)"
        let xl  = "(min-width: 1280px)"
        let xxl = "(min-width: 1536px)"

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
