namespace Zest.Dsl

// ============================================================
// DslComponents — Forms, media, layout components
// ============================================================

module DslComponents =
    open Dsl

    // ---- Form elements ----
    let form action ch = elem "form" [attr "action" action] ch
    let input t n v = voidElem "input" [attr "type" t; attr "name" n; attr "value" v]
    let button ch = elem "button" [] ch
    let textarea name ch = elem "textarea" [attr "name" name] ch
    let select name ch = elem "select" [attr "name" name] ch
    let option value ch = elem "option" [attr "value" value] ch
    let label forVal ch = elem "label" [attr "for" forVal] ch

    let formC cls action ch = elem "form" [attr "action" action; attr "class" cls] ch
    let buttonC cls ch = elem "button" [attr "class" cls] ch
    let labelC cls forVal ch = elem "label" [attr "for" forVal; attr "class" cls] ch

    // ---- Layout components ----
    let container ch = divC "container" ch
    let row ch = divC "row" ch
    let col ch = divC "col" ch
    let card ch = divC "card" ch
    let badge t = spanC "badge" [text t]

    // ---- Alert components ----
    let alert level ch = divC ("alert alert-" + level) ch
    let alertInfo ch = divC "alert alert-info" ch
    let alertSuccess ch = divC "alert alert-success" ch
    let alertWarning ch = divC "alert alert-warning" ch
    let alertDanger ch = divC "alert alert-danger" ch

    // ---- Button variants ----
    let btnPrimary ch = buttonC "btn btn-primary" ch
    let btnSecondary ch = buttonC "btn btn-secondary" ch
    let btnSuccess ch = buttonC "btn btn-success" ch
    let btnDanger ch = buttonC "btn btn-danger" ch

    // ---- Figure / media ----
    let figure src alt cap =
        elem "figure" [] [
            voidElem "img" [attr "src" src; attr "alt" alt]
            elem "figcaption" [] [text cap]
        ]

    // ---- Details / summary ----
    let details summary ch =
        elem "details" [] (elem "summary" [] [text summary] :: ch)

    // ---- Utility helpers ----
    let each items f = items |> List.map f |> String.concat ""
    let joinWith sep items = String.concat sep items
    let opt v = match v with Some x -> x | None -> ""
    let renderIf cond node fallback = if cond then node else fallback
    let renderOpt v f = match v with Some x -> f x | None -> ""
