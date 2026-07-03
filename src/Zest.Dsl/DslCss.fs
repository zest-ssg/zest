namespace Zest.Dsl

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
    member _.checked_     = Sel(name + ":checked")
    member _.disabled     = Sel(name + ":disabled")
    member _.enabled      = Sel(name + ":enabled")
    member _.required     = Sel(name + ":required")
    member _.optional     = Sel(name + ":optional")
    member _.read_only    = Sel(name + ":read-only")
    member _.read_write   = Sel(name + ":read-write")
    member _.valid        = Sel(name + ":valid")
    member _.invalid      = Sel(name + ":invalid")
    member _.default_     = Sel(name + ":default")
    member _.in_range     = Sel(name + ":in-range")
    member _.out_of_range = Sel(name + ":out-of-range")
    member _.placeholder_shown = Sel(name + ":placeholder-shown")
    member _.autofill     = Sel(name + ":autofill")
    member _.target       = Sel(name + ":target")
    member _.root_        = Sel(name + ":root")
    member _.empty        = Sel(name + ":empty")
    member _.blank        = Sel(name + ":blank")
    member _.first_child  = Sel(name + ":first-child")
    member _.last_child   = Sel(name + ":last-child")
    member _.only_child   = Sel(name + ":only-child")
    member _.first_of_type = Sel(name + ":first-of-type")
    member _.last_of_type  = Sel(name + ":last-of-type")
    member _.only_of_type  = Sel(name + ":only-of-type")
    member _.nth_child(n: int)    = Sel(name + sprintf ":nth-child(%d)" n)
    member _.nth_last_child(n: int) = Sel(name + sprintf ":nth-last-child(%d)" n)
    member _.nth_of_type(n: int)  = Sel(name + sprintf ":nth-of-type(%d)" n)
    member _.nth_last_of_type(n: int) = Sel(name + sprintf ":nth-last-of-type(%d)" n)
    member _.not_(sel: string)    = Sel(name + sprintf ":not(%s)" sel)
    member _.lang(code: string)   = Sel(name + sprintf ":lang(%s)" code)
    member _.is_(sel: string)     = Sel(name + sprintf ":is(%s)" sel)
    member _.where_(sel: string)  = Sel(name + sprintf ":where(%s)" sel)
    member _.has_(sel: string)    = Sel(name + sprintf ":has(%s)" sel)

    // ── Pseudo-elements ─────────────────────────────────

    member _.before  = Sel(name + "::before")
    member _.after   = Sel(name + "::after")
    member _.first_letter = Sel(name + "::first-letter")
    member _.first_line   = Sel(name + "::first-line")
    member _.selection    = Sel(name + "::selection")
    member _.placeholder  = Sel(name + "::placeholder")
    member _.backdrop     = Sel(name + "::backdrop")
    member _.marker       = Sel(name + "::marker")
    member _.spelling_error = Sel(name + "::spelling-error")
    member _.grammar_error  = Sel(name + "::grammar-error")

    // ── Attribute selectors ─────────────────────────────

    member this.attr(a: string) = Sel(name + sprintf "[%s]" a)
    member this.attr_eq(a: string, v: string) = Sel(name + sprintf """[%s="%s"]""" a v)
    member this.attr_contains(a: string, v: string) = Sel(name + sprintf """[%s~="%s"]""" a v)
    member this.attr_dash(a: string, v: string) = Sel(name + sprintf """[%s|="%s"]""" a v)
    member this.attr_starts(a: string, v: string) = Sel(name + sprintf """[%s^="%s"]""" a v)
    member this.attr_ends(a: string, v: string) = Sel(name + sprintf """[%s$="%s"]""" a v)
    member this.attr_substr(a: string, v: string) = Sel(name + sprintf """[%s*="%s"]""" a v)

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
module DslCss =

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
    ///       body [ bg "#000"; color "#0f0"; font_family "monospace" ]
    ///       a.hover [ color "#0ff" ]
    ///       cls "container" [ max_width "1200px"; margin "0 auto" ]
    ///   }
    let stylesheet = StylesheetBuilder()

    // ── CSS Property Functions ──────────────────────────────

    // Background
    let bg                   v = { Property = "background";           Value = v }
    let bg_color             v = { Property = "background-color";    Value = v }
    let bg_image             v = { Property = "background-image";    Value = v }
    let bg_repeat            v = { Property = "background-repeat";   Value = v }
    let bg_position          v = { Property = "background-position"; Value = v }
    let bg_size              v = { Property = "background-size";     Value = v }
    let bg_attachment        v = { Property = "background-attachment"; Value = v }
    let bg_clip              v = { Property = "background-clip";     Value = v }
    let bg_origin            v = { Property = "background-origin";   Value = v }
    let bg_blend_mode        v = { Property = "background-blend-mode"; Value = v }

    // Color & Text
    let color                v = { Property = "color";          Value = v }
    let opacity              v = { Property = "opacity";        Value = v }

    // Typography
    let font_family          v = { Property = "font-family";     Value = v }
    let font_size            v = { Property = "font-size";       Value = v }
    let font_weight          v = { Property = "font-weight";     Value = v }
    let font_style           v = { Property = "font-style";      Value = v }
    let font_variant         v = { Property = "font-variant";    Value = v }
    let font_stretch         v = { Property = "font-stretch";    Value = v }
    let line_height          v = { Property = "line-height";     Value = v }
    let letter_spacing       v = { Property = "letter-spacing";  Value = v }
    let word_spacing         v = { Property = "word-spacing";    Value = v }
    let text_align           v = { Property = "text-align";      Value = v }
    let text_decoration      v = { Property = "text-decoration"; Value = v }
    let text_transform       v = { Property = "text-transform";  Value = v }
    let text_indent          v = { Property = "text-indent";     Value = v }
    let text_overflow        v = { Property = "text-overflow";   Value = v }
    let text_shadow          v = { Property = "text-shadow";     Value = v }
    let text_wrap            v = { Property = "text-wrap";       Value = v }
    let white_space          v = { Property = "white-space";     Value = v }
    let word_break           v = { Property = "word-break";      Value = v }
    let overflow_wrap        v = { Property = "overflow-wrap";   Value = v }
    let hyphens              v = { Property = "hyphens";         Value = v }
    let vertical_align       v = { Property = "vertical-align";  Value = v }

    // Box Model
    let width                v = { Property = "width";           Value = v }
    let height               v = { Property = "height";          Value = v }
    let min_width            v = { Property = "min-width";       Value = v }
    let max_width            v = { Property = "max-width";       Value = v }
    let min_height           v = { Property = "min-height";      Value = v }
    let max_height           v = { Property = "max-height";      Value = v }
    let margin               v = { Property = "margin";          Value = v }
    let margin_top           v = { Property = "margin-top";      Value = v }
    let margin_right         v = { Property = "margin-right";    Value = v }
    let margin_bottom        v = { Property = "margin-bottom";   Value = v }
    let margin_left          v = { Property = "margin-left";     Value = v }
    let padding              v = { Property = "padding";         Value = v }
    let padding_top          v = { Property = "padding-top";     Value = v }
    let padding_right        v = { Property = "padding-right";   Value = v }
    let padding_bottom       v = { Property = "padding-bottom";  Value = v }
    let padding_left         v = { Property = "padding-left";    Value = v }
    let box_sizing           v = { Property = "box-sizing";      Value = v }
    let box_shadow           v = { Property = "box-shadow";      Value = v }

    // Border
    let border               v = { Property = "border";          Value = v }
    let border_top           v = { Property = "border-top";      Value = v }
    let border_right         v = { Property = "border-right";    Value = v }
    let border_bottom        v = { Property = "border-bottom";   Value = v }
    let border_left          v = { Property = "border-left";     Value = v }
    let border_color         v = { Property = "border-color";    Value = v }
    let border_width         v = { Property = "border-width";    Value = v }
    let border_style         v = { Property = "border-style";    Value = v }
    let border_radius        v = { Property = "border-radius";   Value = v }
    let border_top_left_radius     v = { Property = "border-top-left-radius";     Value = v }
    let border_top_right_radius    v = { Property = "border-top-right-radius";    Value = v }
    let border_bottom_left_radius  v = { Property = "border-bottom-left-radius";  Value = v }
    let border_bottom_right_radius v = { Property = "border-bottom-right-radius"; Value = v }
    let outline              v = { Property = "outline";         Value = v }
    let outline_color        v = { Property = "outline-color";   Value = v }
    let outline_width        v = { Property = "outline-width";   Value = v }
    let outline_style        v = { Property = "outline-style";   Value = v }
    let outline_offset       v = { Property = "outline-offset";  Value = v }

    // Display & Positioning
    let display              v = { Property = "display";         Value = v }
    let position             v = { Property = "position";        Value = v }
    let top                  v = { Property = "top";             Value = v }
    let right                v = { Property = "right";           Value = v }
    let bottom               v = { Property = "bottom";          Value = v }
    let left                 v = { Property = "left";            Value = v }
    let z_index              v = { Property = "z-index";         Value = v }
    let float_               v = { Property = "float";           Value = v }
    let clear                v = { Property = "clear";           Value = v }
    let overflow             v = { Property = "overflow";        Value = v }
    let overflow_x           v = { Property = "overflow-x";      Value = v }
    let overflow_y           v = { Property = "overflow-y";      Value = v }
    let visibility           v = { Property = "visibility";      Value = v }
    let object_fit           v = { Property = "object-fit";      Value = v }
    let object_position      v = { Property = "object-position"; Value = v }
    let aspect_ratio         v = { Property = "aspect-ratio";    Value = v }

    // Flexbox
    let flex                 v = { Property = "flex";            Value = v }
    let flex_direction       v = { Property = "flex-direction";  Value = v }
    let flex_wrap            v = { Property = "flex-wrap";       Value = v }
    let flex_flow            v = { Property = "flex-flow";       Value = v }
    let flex_grow            v = { Property = "flex-grow";       Value = v }
    let flex_shrink          v = { Property = "flex-shrink";     Value = v }
    let flex_basis           v = { Property = "flex-basis";      Value = v }
    let justify_content      v = { Property = "justify-content"; Value = v }
    let align_items          v = { Property = "align-items";     Value = v }
    let align_content        v = { Property = "align-content";   Value = v }
    let align_self           v = { Property = "align-self";      Value = v }
    let justify_items        v = { Property = "justify-items";   Value = v }
    let justify_self         v = { Property = "justify-self";    Value = v }
    let order_               v = { Property = "order";           Value = v }
    let gap                  v = { Property = "gap";             Value = v }
    let row_gap              v = { Property = "row-gap";         Value = v }
    let column_gap           v = { Property = "column-gap";      Value = v }
    let place_items          v = { Property = "place-items";     Value = v }
    let place_content        v = { Property = "place-content";   Value = v }
    let place_self           v = { Property = "place-self";      Value = v }

    // Grid
    let grid                 v = { Property = "grid";            Value = v }
    let grid_template_columns  v = { Property = "grid-template-columns";  Value = v }
    let grid_template_rows     v = { Property = "grid-template-rows";     Value = v }
    let grid_template_areas    v = { Property = "grid-template-areas";    Value = v }
    let grid_template          v = { Property = "grid-template";          Value = v }
    let grid_auto_columns      v = { Property = "grid-auto-columns";      Value = v }
    let grid_auto_rows         v = { Property = "grid-auto-rows";         Value = v }
    let grid_auto_flow         v = { Property = "grid-auto-flow";         Value = v }
    let grid_column            v = { Property = "grid-column";            Value = v }
    let grid_row               v = { Property = "grid-row";               Value = v }
    let grid_column_start      v = { Property = "grid-column-start";      Value = v }
    let grid_column_end        v = { Property = "grid-column-end";        Value = v }
    let grid_row_start         v = { Property = "grid-row-start";         Value = v }
    let grid_row_end           v = { Property = "grid-row-end";           Value = v }
    let grid_area              v = { Property = "grid-area";              Value = v }

    // Transform & Transition
    let transform            v = { Property = "transform";       Value = v }
    let transform_origin     v = { Property = "transform-origin"; Value = v }
    let transition           v = { Property = "transition";      Value = v }
    let transition_duration  v = { Property = "transition-duration";  Value = v }
    let transition_property  v = { Property = "transition-property";  Value = v }
    let transition_timing    v = { Property = "transition-timing-function"; Value = v }
    let transition_delay     v = { Property = "transition-delay";      Value = v }

    // Animation
    let animation            v = { Property = "animation";        Value = v }
    let animation_name       v = { Property = "animation-name";   Value = v }
    let animation_duration   v = { Property = "animation-duration"; Value = v }
    let animation_timing     v = { Property = "animation-timing-function"; Value = v }
    let animation_delay      v = { Property = "animation-delay";  Value = v }
    let animation_iteration  v = { Property = "animation-iteration-count"; Value = v }
    let animation_direction  v = { Property = "animation-direction"; Value = v }
    let animation_fill_mode  v = { Property = "animation-fill-mode";  Value = v }
    let animation_play_state v = { Property = "animation-play-state"; Value = v }

    // Filter & Effects
    let filter               v = { Property = "filter";          Value = v }
    let backdrop_filter      v = { Property = "backdrop-filter"; Value = v }
    let clip_path            v = { Property = "clip-path";       Value = v }
    let mix_blend_mode       v = { Property = "mix-blend-mode";  Value = v }
    let isolation_           v = { Property = "isolation";       Value = v }

    // Cursor & Interaction
    let cursor               v = { Property = "cursor";          Value = v }
    let pointer_events       v = { Property = "pointer-events";  Value = v }
    let user_select          v = { Property = "user-select";     Value = v }
    let resize               v = { Property = "resize";          Value = v }
    let caret_color          v = { Property = "caret-color";     Value = v }
    let scroll_behavior      v = { Property = "scroll-behavior"; Value = v }
    let scrollbar_width      v = { Property = "scrollbar-width"; Value = v }
    let scrollbar_color      v = { Property = "scrollbar-color"; Value = v }

    // Lists & Counters
    let list_style           v = { Property = "list-style";      Value = v }
    let list_style_type      v = { Property = "list-style-type"; Value = v }
    let list_style_position  v = { Property = "list-style-position"; Value = v }
    let list_style_image     v = { Property = "list-style-image";    Value = v }
    let counter_reset        v = { Property = "counter-reset";   Value = v }
    let counter_increment    v = { Property = "counter-increment"; Value = v }
    let counter_set          v = { Property = "counter-set";     Value = v }

    // Tables
    let table_layout         v = { Property = "table-layout";    Value = v }
    let border_collapse      v = { Property = "border-collapse"; Value = v }
    let border_spacing       v = { Property = "border-spacing";  Value = v }
    let caption_side         v = { Property = "caption-side";    Value = v }
    let empty_cells          v = { Property = "empty-cells";     Value = v }

    // Content
    let content_             v = { Property = "content";         Value = v }
    let quotes               v = { Property = "quotes";          Value = v }

    // Print
    let page_break_before    v = { Property = "page-break-before"; Value = v }
    let page_break_after     v = { Property = "page-break-after";  Value = v }
    let page_break_inside    v = { Property = "page-break-inside"; Value = v }

    // Modern
    let will_change          v = { Property = "will-change";     Value = v }
    let contain              v = { Property = "contain";         Value = v }
    let contain_intrinsic_size v = { Property = "contain-intrinsic-size"; Value = v }
    let content_visibility   v = { Property = "content-visibility"; Value = v }

    // Custom property / variable
    let var_ name value = { Property = sprintf "--%s" name; Value = value }

    /// Create a declaration with an explicit CSS property name.
    let prop (name: string) (value: string) = { Property = name; Value = value }

    // ── Pre-defined Selectors ───────────────────────────────

    let allElements = Sel("*")
    let html_   = Sel("html")
    let body    = Sel("body")
    let head_   = Sel("head")
    let a       = Sel("a")
    let abbr    = Sel("abbr")
    let address = Sel("address")
    let area    = Sel("area")
    let article = Sel("article")
    let aside   = Sel("aside")
    let audio   = Sel("audio")
    let b       = Sel("b")
    let base_   = Sel("base")
    let bdi     = Sel("bdi")
    let bdo     = Sel("bdo")
    let blockquote = Sel("blockquote")
    let br      = Sel("br")
    let button  = Sel("button")
    let canvas  = Sel("canvas")
    let caption = Sel("caption")
    let cite_   = Sel("cite")
    let code_   = Sel("code")
    let col     = Sel("col")
    let colgroup = Sel("colgroup")
    let data_   = Sel("data")
    let datalist = Sel("datalist")
    let dd      = Sel("dd")
    let del_    = Sel("del")
    let details = Sel("details")
    let dfn     = Sel("dfn")
    let dialog  = Sel("dialog")
    let div     = Sel("div")
    let dl      = Sel("dl")
    let dt      = Sel("dt")
    let em      = Sel("em")
    let embed   = Sel("embed")
    let fieldset = Sel("fieldset")
    let figcaption = Sel("figcaption")
    let figure  = Sel("figure")
    let footer  = Sel("footer")
    let form    = Sel("form")
    let h1      = Sel("h1")
    let h2      = Sel("h2")
    let h3      = Sel("h3")
    let h4      = Sel("h4")
    let h5      = Sel("h5")
    let h6      = Sel("h6")
    let header  = Sel("header")
    let hgroup  = Sel("hgroup")
    let hr      = Sel("hr")
    let i       = Sel("i")
    let iframe  = Sel("iframe")
    let img     = Sel("img")
    let input   = Sel("input")
    let ins_    = Sel("ins")
    let kbd     = Sel("kbd")
    let label   = Sel("label")
    let legend  = Sel("legend")
    let li      = Sel("li")
    let link_   = Sel("link")
    let main    = Sel("main")
    let map_    = Sel("map")
    let mark_   = Sel("mark")
    let menu_   = Sel("menu")
    let meta_   = Sel("meta")
    let meter   = Sel("meter")
    let nav     = Sel("nav")
    let noscript = Sel("noscript")
    let object_ = Sel("object")
    let ol      = Sel("ol")
    let optgroup = Sel("optgroup")
    let option_ = Sel("option")
    let output  = Sel("output")
    let p       = Sel("p")
    let picture = Sel("picture")
    let pre     = Sel("pre")
    let progress = Sel("progress")
    let q       = Sel("q")
    let rp      = Sel("rp")
    let rt      = Sel("rt")
    let ruby    = Sel("ruby")
    let s       = Sel("s")
    let samp    = Sel("samp")
    let script_ = Sel("script")
    let section = Sel("section")
    let select_ = Sel("select")
    let small   = Sel("small")
    let source  = Sel("source")
    let span    = Sel("span")
    let strong  = Sel("strong")
    let style_  = Sel("style")
    let sub     = Sel("sub")
    let summary = Sel("summary")
    let sup     = Sel("sup")
    let table   = Sel("table")
    let tbody   = Sel("tbody")
    let td      = Sel("td")
    let template = Sel("template")
    let textarea = Sel("textarea")
    let tfoot   = Sel("tfoot")
    let th      = Sel("th")
    let thead   = Sel("thead")
    let time_   = Sel("time")
    let title_  = Sel("title")
    let tr      = Sel("tr")
    let track   = Sel("track")
    let u       = Sel("u")
    let ul      = Sel("ul")
    let varEl   = Sel("var")
    let video   = Sel("video")
    let wbr     = Sel("wbr")

    // ── ID and Class selector helpers ───────────────────────

    /// Create a class selector (.className).
    let cls (name: string) = Sel(sprintf ".%s" name)

    /// Create an ID selector (#idName).
    let id (name: string) = Sel(sprintf "#%s" name)

    /// Create a selector with attribute [attr].
    let attr_sel (name: string) = Sel(sprintf "[%s]" name)

    /// Combine multiple selectors with comma (e.g., "h1, h2, h3").
    let selectors (sels: Sel list) =
        let combined = sels |> List.map (fun s -> s.Name) |> String.concat ", "
        Sel(combined)

    /// Create a raw selector from a string.
    let raw_sel (selector: string) = Sel(selector)

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
