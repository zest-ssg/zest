namespace Zest.Dsl

open System
open System.Web

// ============================================================
// DslSeo — SEO meta tags, Open Graph, Twitter Cards, hreflang
// ============================================================

module DslSeo =
    open Dsl

    /// HTML-encode a value for safe insertion into attribute values.
    let private ae (v: string) = htmlEncode v

    /// Generate a complete set of <meta> tags for SEO.
    /// Includes charset, viewport, title, description, keywords, author, and robots.
    let meta_tags (title: string) (description: string) (url: string) (image: string) (siteName: string) =
        [
            yield sprintf """<meta charset="utf-8" />"""
            yield sprintf """<meta name="viewport" content="width=device-width, initial-scale=1.0" />"""
            yield sprintf """<title>%s</title>""" (ae title)
            yield sprintf """<meta name="description" content="%s" />""" (ae description)
            yield sprintf """<link rel="canonical" href="%s" />""" (ae url)
            if not (String.IsNullOrEmpty siteName) then
                yield sprintf """<meta name="application-name" content="%s" />""" (ae siteName)
            if not (String.IsNullOrEmpty image) then
                yield sprintf """<meta name="image" content="%s" />""" (ae image)
        ]

    /// Generate Open Graph (og:) meta tags for social sharing.
    let open_graph_tags (title: string) (description: string) (url: string) (image: string) (ogType: string) =
        [
            yield sprintf """<meta property="og:title" content="%s" />""" (ae title)
            yield sprintf """<meta property="og:description" content="%s" />""" (ae description)
            yield sprintf """<meta property="og:url" content="%s" />""" (ae url)
            yield sprintf """<meta property="og:type" content="%s" />""" (ae ogType)
            if not (String.IsNullOrEmpty image) then
                yield sprintf """<meta property="og:image" content="%s" />""" (ae image)
                yield sprintf """<meta property="og:image:alt" content="%s" />""" (ae title)
        ]

    /// Generate Twitter Card meta tags.
    let twitter_card_tags (cardType: string) (title: string) (description: string) (image: string) (site: string) =
        [
            yield sprintf """<meta name="twitter:card" content="%s" />""" (ae cardType)
            yield sprintf """<meta name="twitter:title" content="%s" />""" (ae title)
            yield sprintf """<meta name="twitter:description" content="%s" />""" (ae description)
            if not (String.IsNullOrEmpty image) then
                yield sprintf """<meta name="twitter:image" content="%s" />""" (ae image)
            if not (String.IsNullOrEmpty site) then
                yield sprintf """<meta name="twitter:site" content="%s" />""" (ae site)
        ]

    /// Generate a canonical URL <link> tag.
    let canonical_url (url: string) =
        sprintf """<link rel="canonical" href="%s" />""" (ae url)

    /// Generate a single hreflang <link> tag for multilingual pages.
    let hreflang_tag (lang: string) (url: string) =
        sprintf """<link rel="alternate" hreflang="%s" href="%s" />""" (ae lang) (ae url)

    // ── Page-object convenience APIs ───────────────────────────────────
    // These accept a page-like record (url, title, description, date, slug,
    // tags) and return a ready-to-paste HTML string block.

    /// A minimal page shape accepted by the SEO helpers below.
    type SeoPage = {
        url: string
        title: string
        description: string
        image: string
        ``type``: string      // og:type, e.g. "article" / "website"
        siteName: string
    }

    /// Generate a complete Open Graph tag block as a single HTML string.
    /// `openGraphHtml(page)` matches the spec's page-object signature.
    let openGraphHtml (page: SeoPage) =
        open_graph_tags page.title page.description page.url page.image page.``type``
        |> String.concat "\n"

    /// Generate a complete Twitter Card tag block as a single HTML string.
    /// `twitterCardHtml(page, cardType)` — cardType is "summary" or
    /// "summary_large_image".
    let twitterCardHtml (page: SeoPage) (cardType: string) =
        twitter_card_tags cardType page.title page.description page.image page.siteName
        |> String.concat "\n"

    /// Generate a canonical URL <link> tag from a page object.
    let canonicalUrl (page: SeoPage) = canonical_url page.url

    /// Generate the full SEO <head> block (meta + OG + Twitter + canonical)
    /// for a page, ready to drop into a <head> element.
    let seoHead (page: SeoPage) (siteName: string) =
        [ yield! meta_tags page.title page.description page.url page.image siteName
          yield openGraphHtml page
          yield twitterCardHtml page "summary_large_image"
          yield canonicalUrl page ]
        |> String.concat "\n"
