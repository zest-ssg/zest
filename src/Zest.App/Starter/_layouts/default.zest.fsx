// @layout shell

// ---------------------------------------------------------------------------
// default.zest.fsx - a paper page: a centred title, an optional subtitle taken
// from the page description, and the body.
//
// The name is not free: `default` is the layout the engine falls back to for
// every page that declares none, so renaming this file needs a matching
// `default_layout` entry in _config.toml. `// @layout shell` hands the markup
// below to shell.zest.fsx, which wraps it in the <html> document.
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
