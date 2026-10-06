namespace Zest.Markup

// ============================================================
// Compound — High-level component builders
// ============================================================

[<AutoOpen>]
module Compound =
    open Dsl
    open DslSugar

    /// Build a media object (image + text side by side).
    let mediaObject (imgSrc: string) (imgAlt: string) (title: string) (desc: string) =
        divClass "media" [
            imgClass "media-img" imgSrc imgAlt
            divClass "media-body" [
                hText 4 title
                pText desc
            ]
        ]

    /// Build a simple card component.
    let cardComponent (title: string) (body: string) (linkUrl: string) (linkText: string) =
        divClass "card" [
            divClass "card-body" [
                hText 4 title
                pText body
                aTextClass "btn btn-primary" linkUrl linkText
            ]
        ]

    /// Build a hero section.
    let heroSection (title: string) (subtitle: string) (ctaUrl: string) (ctaText: string) =
        sectionClass "hero" [
            divClass "hero-content" [
                hText 1 title
                pText subtitle
                aTextClass "btn btn-lg" ctaUrl ctaText
            ]
        ]

    /// Build a grid of cards from data items.
    let cardGrid (items: 'a list) (cardFn: 'a -> string) =
        divClass "grid" (items |> List.map cardFn)
