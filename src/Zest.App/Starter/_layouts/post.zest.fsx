// @layout shell

// ---------------------------------------------------------------------------
// post.zest.fsx - an article: title, author, date, body, and a footer carrying
// the tag list and the way back to the archive.
//
// The paper page with metadata that only a dated entry has; `// @layout shell`
// supplies the document around it.
// ---------------------------------------------------------------------------

let paperTitle = if page.title = "" then site.title else page.title

let paperAuthor =
    if page.author <> "" then page.author
    elif site.author <> "" then site.author
    else site.title

let paperHead =
    let children =
        [ h1Class "paper__title" [ text paperTitle ]
          pClass "paper__author" [ text paperAuthor ] ]
        @ (if page.date = "" then
               []
           else
               [ pClass "paper__date"
                     [ text (Dates.formatDateCustom page.date "MMMM d, yyyy") ] ])

    headerClass "paper__head" [ fragmentLines children ]

let tagsMarkup =
    if page.tags.Length = 0 then
        ""
    else
        let links =
            page.tags
            |> Array.map (fun tag ->
                elem "a" [ attr "href" ("/tags/" + tag.ToLowerInvariant() + "/") ] [ text tag ])
            |> String.concat ""

        let children =
            [ elem "span" [ attr "class" "paper__tags-label" ] [ text "Tags" ]
              links ]

        pClass "paper__tags" [ fragmentLines children ]

let paperFooter =
    let back =
        elem "p" [ attr "class" "paper__nav" ]
            [ elem "a" [ attr "href" "/archive/" ] [ text "All posts" ]
              raw " &middot; "
              elem "a" [ attr "href" "/" ] [ text "Home" ] ]

    elem "footer" [ attr "class" "paper__footer" ] [ fragmentLines [ tagsMarkup; back ] ]

printfn
    "%s"
    (articleClass "paper"
        [ fragmentLines
              [ paperHead
                divClass "paper__body" [ raw content ]
                paperFooter ] ])
