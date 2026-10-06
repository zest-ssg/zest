// @layout shell

// ---------------------------------------------------------------------------
// default.zest.fsx - a paper page: a centred title, an optional subtitle taken
// from the page description, and the body.
//
// This is the layout a page gets when it declares none, so it is the safe
// place to put whatever every ordinary page needs. `// @layout shell` hands the
// markup below to shell.zest.fsx, which wraps it in the <html> document.
// ---------------------------------------------------------------------------

let paperTitle = if page.title = "" then site.title else page.title

let paperHead =
    let children =
        [ h1Class "paper__title" [ text paperTitle ] ]
        @ (if page.description = "" then
               []
           else
               [ pClass "paper__subtitle" [ text page.description ] ])

    headerClass "paper__head" [ fragmentLines children ]

let paperBody = divClass "paper__body" [ raw content ]

printfn
    "%s"
    (articleClass "paper" [ fragmentLines [ paperHead; paperBody ] ])
