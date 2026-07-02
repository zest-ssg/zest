namespace Zest.Dsl

// ============================================================
// CompoundBuilder — High-level component builders
// ============================================================

module CompoundBuilder =
    open Dsl
    open DslSugar

    /// Build a media object (image + text side by side).
    let media_object (imgSrc: string) (imgAlt: string) (title: string) (desc: string) =
        divC "media" [
            imgC "media-img" imgSrc imgAlt
            divC "media-body" [
                h_text 4 title
                p_text desc
            ]
        ]

    /// Build a simple card component.
    let card_component (title: string) (body: string) (linkUrl: string) (linkText: string) =
        divC "card" [
            divC "card-body" [
                h_text 4 title
                p_text body
                a_text_c "btn btn-primary" linkUrl linkText
            ]
        ]

    /// Build a hero section.
    let hero_section (title: string) (subtitle: string) (ctaUrl: string) (ctaText: string) =
        sectionC "hero" [
            divC "hero-content" [
                h_text 1 title
                p_text subtitle
                a_text_c "btn btn-lg" ctaUrl ctaText
            ]
        ]

    /// Build a grid of cards from data items.
    let card_grid (items: 'a list) (cardFn: 'a -> string) =
        divC "grid" (items |> List.map cardFn)
