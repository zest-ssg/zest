namespace Zest.Markup

// ============================================================
// Components — Forms, media, layout components
// ============================================================

[<AutoOpen>]
module Components =
    open Dsl

    // ---- Form elements ----
    let form action ch = elem "form" [attr "action" action] ch
    let input t n v = voidElem "input" [attr "type" t; attr "name" n; attr "value" v]
    let button ch = elem "button" [] ch
    let textarea name ch = elem "textarea" [attr "name" name] ch
    let select name ch = elem "select" [attr "name" name] ch
    let option value ch = elem "option" [attr "value" value] ch
    let label forVal ch = elem "label" [attr "for" forVal] ch
    /// `<optgroup label="…">…</optgroup>` — groups its `<option>` children.
    let optgroup label ch = elem "optgroup" [attr "label" label] ch
    /// `<datalist id="…">…</datalist>` — shared by an input's `list` attribute.
    let datalist id ch = elem "datalist" [attr "id" id] ch

    // `boolAttr` yields "" for a false flag; dropping empty entries keeps a
    // stray separator out of the attribute list.
    let private dropEmptyAttrs (attrs: string list) =
        attrs |> List.filter (fun a -> a <> "")

    // ---- Typed form controls ----
    // Thin wrappers over `input`/`voidElem` that pin `type`, so authors do not
    // have to spell out the type string. `n` is the control name, `v` its value.
    let inputText n v = input "text" n v
    let inputEmail n v = input "email" n v
    let inputPassword n v = input "password" n v
    let inputHidden n v = input "hidden" n v
    let inputNumber n v = input "number" n v
    let inputDate n v = input "date" n v
    let inputRange n v = input "range" n v
    let inputColor n v = input "color" n v
    let inputSearch n v = input "search" n v
    let inputTel n v = input "tel" n v
    let inputUrl n v = input "url" n v
    /// `<input type="file" name="…" />` — no `value`: the browser sets it.
    let inputFile n = voidElem "input" [attr "type" "file"; attr "name" n]
    /// `<input type="submit" value="…" />` — no `name`: the value is the label.
    let inputSubmit v = voidElem "input" [attr "type" "submit"; attr "value" v]
    let inputCheckbox n v isChecked =
        voidElem "input" (dropEmptyAttrs
            [ attr "type" "checkbox"; attr "name" n; attr "value" v; boolAttr "checked" isChecked ])
    let inputRadio n v isChecked =
        voidElem "input" (dropEmptyAttrs
            [ attr "type" "radio"; attr "name" n; attr "value" v; boolAttr "checked" isChecked ])

    let formClass cls action ch = elem "form" [attr "action" action; attr "class" cls] ch
    let buttonClass cls ch = elem "button" [attr "class" cls] ch
    let labelClass cls forVal ch = elem "label" [attr "for" forVal; attr "class" cls] ch

    // `C` ≡ `Class` (see Dsl.fs): `formC` ≡ `formClass`, etc.
    let formC = formClass
    let buttonC = buttonClass
    let labelC = labelClass

    // ---- Layout components ----
    let container ch = divClass "container" ch
    let row ch = divClass "row" ch
    let col ch = divClass "col" ch
    let card ch = divClass "card" ch
    let badge t = spanClass "badge" [text t]

    // ---- Alert components ----
    let alert level ch = divClass ("alert alert-" + level) ch
    let alertInfo ch = divClass "alert alert-info" ch
    let alertSuccess ch = divClass "alert alert-success" ch
    let alertWarning ch = divClass "alert alert-warning" ch
    let alertDanger ch = divClass "alert alert-danger" ch

    // ---- Button variants ----
    let btnPrimary ch = buttonClass "btn btn-primary" ch
    let btnSecondary ch = buttonClass "btn btn-secondary" ch
    let btnSuccess ch = buttonClass "btn btn-success" ch
    let btnDanger ch = buttonClass "btn btn-danger" ch

    // ---- Figure / details ----
    // The composed `figure src alt cap` / `details summary ch` variants used to
    // live here and shadowed the `Dsl` element builders, so the same call site
    // meant different things depending on whether the script added `open Dsl`.
    // The `Dsl` builders are the single source now:
    //   figure [ img src alt; figcaption [ text cap ] ]
    //   details (summary [ text heading ] :: ch)

    // ---- Utility helpers ----
    let each items f = items |> List.map f |> String.concat ""
    // `joinWith` lives in `DslSugar` alone — same signature, same output.
    let opt v = match v with Some x -> x | None -> ""
    let renderIf cond node fallback = if cond then node else fallback
    let renderOpt v f = match v with Some x -> f x | None -> ""

    // ---- Navigation components ─────────────────────────────────

    /// A navigation link with optional active state.
    /// `navLink "/about" "About" true` → `<a href="/about" class="active">About</a>`.
    let navLink (url: string) (label: string) (isActive: bool) =
        let cls = if isActive then "active" else ""
        if cls = "" then aHref url label
        else aClass cls url [text label]

    /// A navigation list (`<nav><ul>…</ul></nav>`) from (url, label, isActive)
    /// triples. The active item gets `class="active"`.
    let navList (items: (string * string * bool) list) =
        navClass "nav-list" [
            ul (items |> List.map (fun (url, label, active) -> li [navLink url label active]))
        ]

    /// A breadcrumb trail: `breadcrumb [("Home","/"); ("Posts","/posts")]`.
    /// Renders `<nav class="breadcrumb"><ol>…</ol></nav>`.
    let breadcrumb (items: (string * string) list) =
        navClass "breadcrumb" [
            ol (items |> List.mapi (fun i (label, url) ->
                if i = items.Length - 1 then
                    liClass "active" [text label]   // last item is current page
                else
                    li [ aHref url label; text " › " ]))
        ]

    // ---- Tag / badge components ────────────────────────────────

    /// Render a list of tag strings as clickable badge links.
    /// `tagBadges "/tags/" ["fsharp"; "ssg"]` → spans/links per tag.
    let tagBadges (baseUrl: string) (tags: string list) =
        tags |> List.map (fun t -> aClass "tag" (baseUrl + t) [text t])
        |> ulClass "tag-list"

    /// A single badge span with a variant class.
    let badgeClass (variant: string) (t: string) = spanClass ("badge badge-" + variant) [text t]

    /// Alias for `badgeClass` (`C` ≡ `Class`).
    let badgeC = badgeClass

    // ---- Icon / media components ───────────────────────────────

    /// An inline SVG-less icon span (for icon-font / emoji usage).
    /// `icon "star"` → `<span class="icon icon-star" aria-hidden="true"></span>`.
    let icon (name: string) =
        elem "span" [attr "class" ("icon icon-" + name); aria "hidden" "true"] []

    /// A responsive `<figure>` with srcset for art-directed images.
    let figureResponsive (src: string) (alt: string) (caption: string) (widths: int list) =
        let srcset =
            widths
            |> List.map (fun w -> sprintf "%s?w=%d %dw" src w w)
            |> String.concat ", "
        elem "figure" [] [
            voidElem "img" [attr "src" src; attr "alt" alt; attr "srcset" srcset]
            elem "figcaption" [] [text caption]
        ]

    /// A responsive 16:9 video embed wrapper (YouTube/Vimeo etc.).
    /// `videoEmbed "https://youtube.com/embed/XYZ"` → padded container + iframe.
    let videoEmbed (url: string) =
        divClass "video-embed" [
            elem "iframe" [attr "src" url; attr "frameborder" "0"
                           attr "allowfullscreen" "allowfullscreen"] []
        ]

    // ---- Progress / status components ──────────────────────────

    /// A labelled progress bar. `progressBar 60 "Uploading…"` →
    /// `<progress value="60" max="100"></progress>` with a label span.
    let progressBar (percent: int) (label: string) =
        divClass "progress-wrapper" [
            spanClass "progress-label" [text label]
            elem "progress" [attr "value" (string percent); attr "max" "100"] []
        ]

    /// A simple meter bar (for ratings/gauges).
    let meterBar (value: float) (optimum: float) =
        elem "meter" [attr "value" (string value); attr "min" "0"
                      attr "max" "100"; attr "optimum" (string optimum)] []

    // ---- Social / contact ──────────────────────────────────────

    /// A social media link with an icon class and label.
    let socialLink (platform: string) (url: string) =
        aClass ("social social-" + platform) url [
            spanClass ("icon icon-" + platform) [text ""]
            spanClass "sr-only" [text platform]
        ]

    /// A contact list (dl) of label → value pairs.
    let contactList (items: (string * string) list) =
        dlClass "contact-list" (
            items |> List.collect (fun (k, v) -> [ dt [text k]; dd [text v] ])
        )
