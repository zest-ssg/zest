// @title Tags
// @permalink /tags/
// @layout default
// @description Every tag used on this blog, with post counts.
//
// The engine's TaxonomyGenerator auto-creates /tags/<term>/ listing pages;
// this content file claims the /tags/ index URL, so the cloud below — built
// with DslCollections.tag_cloud — renders instead of the generator's default
// index template.

open Zest.Dsl

let cloud = tag_cloud 1
let totalTags = cloud.Length
let totalPosts = cloud |> List.sumBy snd

// Relative weight drives the visual size of each tag in the cloud.
let weightClass (count: int) =
    if count >= 5 then "xl"
    elif count >= 3 then "lg"
    elif count >= 2 then "md"
    else "sm"

let cloudHtml =
    if cloud.IsEmpty then
        p [ text (t "meta.no_posts") ]
    else
        divC "tag-cloud" [
            for (tag, count) in cloud ->
                aC ("tag-cloud__tag tag-cloud__tag--" + weightClass count) (sprintf "/tags/%s/" tag) [
                    text tag
                    spanC "tag-cloud__count" [ text (sprintf " %d" count) ]
                ]
        ]

render [
    h1 [ text (t "nav.tags") ]
    pC "tags__lead" [
        text (sprintf "%d %s · %s" totalTags (t "tags.terms") (pluralize totalPosts "post"))
    ]
    cloudHtml
]
