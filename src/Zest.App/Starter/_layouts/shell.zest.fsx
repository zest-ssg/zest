// ---------------------------------------------------------------------------
// shell.zest.fsx - the document itself: <html>, <head> and the page frame.
//
// A layout is a `.zest.fsx` template, evaluated by FSI, so the whole document
// is composed with the Zest.Markup DSL rather than a template language. Three
// bindings are injected above this code:
//
//   content - markup already rendered by the page and by any nested layout
//   page    - title / url / date / description / author / tags of this page
//   site    - title / description / author / language / social_* of the site
//
// A nested layout pulls this file in with `// @layout shell`; no content file
// names it directly, which is why the chrome lives here and nowhere else.
// ---------------------------------------------------------------------------

let siteTitle = site.title
let siteAuthor = if site.author = "" then site.title else site.author
let language = if site.language = "" then "en" else site.language
let baseUrl = (siteData "site.base_url").TrimEnd('/')

let pageDescription =
    if page.description <> "" then page.description else site.description

let documentTitle =
    if page.title = "" then siteTitle else page.title + " | " + siteTitle

/// Make a site-relative URL absolute; canonical links and og:url must not be
/// relative. URLs that are already absolute pass through untouched.
let absoluteUrl (url: string) =
    if url.StartsWith "http" then url else baseUrl + url

/// The year credited in the footer; _prebuild.fsx publishes it as a global.
let buildYear =
    match siteData "buildYear" with
    | "" -> string System.DateTime.UtcNow.Year
    | year -> year

// -- <head> -----------------------------------------------------------------

let seoPage: SeoPage =
    { url = absoluteUrl page.url
      title = documentTitle
      description = pageDescription
      image = ""
      ``type`` = (if page.date = "" then "website" else "article")
      siteName = siteTitle }

/// A <link rel="alternate"> pointing at one feed format.
let feedLink (mime: string) (href: string) =
    voidElem "link"
        [ attr "rel" "alternate"
          attr "type" mime
          attr "title" siteTitle
          attr "href" href ]

// The head is assembled from the Zest.Markup.Seo parts rather than from
// Seo.seoHead, which emits the canonical link twice (once through metaTags and
// once through canonicalUrl). twitter:site needs a handle, so the Twitter Card
// block only appears when the site configures one.
let twitterHandle = site.social_twitter

let headMarkup =
    let tags =
        [ yield! metaTags documentTitle pageDescription (absoluteUrl page.url) "" siteTitle
          yield openGraphHtml seoPage

          if twitterHandle <> "" then
              let handle = if twitterHandle.StartsWith "@" then twitterHandle else "@" + twitterHandle

              yield
                  String.concat "\n"
                      (twitterCardTags "summary_large_image" documentTitle pageDescription "" handle)

          yield feedLink "application/rss+xml" "/rss.xml"
          yield feedLink "application/atom+xml" "/atom.xml"
          yield link "stylesheet" "/assets/css/main.css" ]

    elem "head" [] [ fragmentLines tags ]

// -- Chrome -----------------------------------------------------------------

let navItems =
    [ "/", "Home"
      "/archive/", "Archive"
      "/tags/", "Tags"
      "/about/", "About"
      "/rss.xml", "RSS" ]

let navMarkup =
    elem "nav" [ attr "class" "site-nav"; attr "aria-label" "Main" ]
        [ for href, label in navItems do
              yield elem "a" [ attr "href" href ] [ text label ] ]

let headerMarkup =
    let inner =
        [ elem "a"
              [ attr "class" "site-header__title"; attr "href" "/"; attr "rel" "home" ]
              [ text siteTitle ]
          navMarkup ]
        |> fragmentLines

    elem "header" [ attr "class" "site-header" ] [ divClass "site-header__inner" [ inner ] ]

let footerMarkup =
    let copyright =
        elem "p" [ attr "class" "site-footer__meta" ]
            [ raw "&copy; "; text (buildYear + " " + siteAuthor) ]

    let credit =
        elem "p" [ attr "class" "site-footer__meta" ]
            [ raw "Compiled with "
              elem "a" [ attr "href" "https://github.com/zest-ssg/zest" ] [ text "Zest" ] ]

    elem "footer" [ attr "class" "site-footer" ]
        [ divClass "site-footer__inner" [ fragmentLines [ copyright; credit ] ] ]

// -- Document ---------------------------------------------------------------

let mainMarkup = elem "main" [ attr "class" "page__main" ] [ raw content ]

let pageMarkup =
    divClass "page" [ fragmentLines [ headerMarkup; mainMarkup; footerMarkup ] ]

let documentMarkup =
    elem "html" [ attr "lang" language ] [ fragmentLines [ headMarkup; body [ pageMarkup ] ] ]

printfn "%s" (doctype + "\n" + documentMarkup)
