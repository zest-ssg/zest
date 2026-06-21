namespace Zest.Engine.Html

open System
open System.Collections.Generic
open System.Net
open System.Text
open System.Text.RegularExpressions
open Zest.Engine
open Zest.Engine.Zss

// ============================================================
// HTML DSL — Attribute helpers
// ============================================================

[<AutoOpen>]
module HtmlAttributes =

    // ---- Attribute helpers ----
    let attr   k v           = (k, v)
    let id     v             = attr "id"    v
    let cls    v             = attr "class" v
    let styleA v             = attr "style" v
    let href   v             = attr "href"  v
    let src    v             = attr "src"   v
    let alt    v             = attr "alt"   v
    let rel    v             = attr "rel"  v
    let target v             = attr "target" v
    let data   k v           = attr ("data-" + k) v
    let ariaLabel v          = attr "aria-label" v
    let role   v             = attr "role"  v

    // ---- Extended attribute helpers ----
    let name    v            = attr "name" v
    let value   v            = attr "value" v
    let placeholder v        = attr "placeholder" v
    let ``type`` v          = attr "type" v
    let width   v            = attr "width" (string v)
    let height  v            = attr "height" (string v)
    let colspan v            = attr "colspan" (string v)
    let rowspan v            = attr "rowspan" (string v)
    let lang    v            = attr "lang" v
    let dir    v             = attr "dir" v
    let charset v            = attr "charset" v
    let content v           = attr "content" v
    let titleAttr v         = attr "title" v
    let disabled            = attr "disabled" "disabled"
    let readonly            = attr "readonly" "readonly"
    let required            = attr "required" "required"
    let autofocus           = attr "autofocus" "autofocus"
    let autocomplete v      = attr "autocomplete" v
    let min    v            = attr "min" (string v)
    let max    v            = attr "max" (string v)
    let step   v            = attr "step" (string v)
    let pattern v           = attr "pattern" v
    let minlength v         = attr "minlength" (string v)
    let maxlength v         = attr "maxlength" (string v)
    let multiple            = attr "multiple" "multiple"
    let selected           = attr "selected" "selected"
    let checked            = attr "checked" "checked"
    let hidden             = attr "hidden" "hidden"
    let draggable v        = attr "draggable" v
    let contenteditable v  = attr "contenteditable" v
    let spellcheck v       = attr "spellcheck" v
    let tabindex v         = attr "tabindex" (string v)
    let accesskey v         = attr "accesskey" v
    let crossorigin v       = attr "crossorigin" v
    let loading v          = attr "loading" v
    let decoding v         = attr "decoding" v
    let srcset v           = attr "srcset" v
    let sizes  v           = attr "sizes" v
    let poster v           = attr "poster" v
    let controls           = attr "controls" "controls"
    let autoplay           = attr "autoplay" "autoplay"
    let loop               = attr "loop" "loop"
    let muted              = attr "muted" "muted"
    let preload v          = attr "preload" v
    let playsinline        = attr "playsinline" "playsinline"

    // ---- ARIA attribute helpers ----
    let ariaHidden v       = attr "aria-hidden" v
    let ariaDescribedBy v  = attr "aria-describedby" v
    let ariaLabelledBy v   = attr "aria-labelledby" v
    let ariaExpanded v     = attr "aria-expanded" ((string v).ToLower())
    let ariaControls v     = attr "aria-controls" v
    let ariaSelected v     = attr "aria-selected" ((string v).ToLower())
    let ariaPressed v      = attr "aria-pressed" ((string v).ToLower())
    let ariaChecked v      = attr "aria-checked" ((string v).ToLower())
    let ariaDisabled v     = attr "aria-disabled" ((string v).ToLower())
    let ariaCurrent v      = attr "aria-current" v
    let ariaLive v         = attr "aria-live" v
    let ariaAtomic v       = attr "aria-atomic" ((string v).ToLower())
    let ariaRelevant v     = attr "aria-relevant" v
    let ariaHasPopup v     = attr "aria-haspopup" v
    let ariaRoleDescription v = attr "aria-roledescription" v

    // ---- Data attribute helpers ----
    let dataId v           = data "id" v
    let dataUrl v          = data "url" v
    let dataIndex v        = data "index" v
    let dataToggle v       = data "toggle" v
    let dataTarget v       = data "target" v
    let dataSrc v          = data "src" v
