// @title Search
// @layout default
// @description Static full-text search across every post — no server required.
//
// The index is computed at build time with DslCollections (site_pages) and
// injected into the page as JSON via `jsonBlock`; a small vanilla-JS filter
// then runs entirely in the browser.

open Zest.Dsl

let index =
    site_pages ()
    |> Array.filter (fun p -> p.url.StartsWith("/posts/") && p.url <> "/posts/")
    |> Array.map (fun p ->
        {| url = p.url; title = p.title; date = p.date; tags = p.tags; description = p.description |})

let placeholder = t "search.placeholder"
let emptyMsg    = t "search.empty"
let noQueryMsg  = t "search.no_query"

render [
    h1 [ text (t "nav.search") ]
    pC "search__lead" [ text (t "search.lead") ]
    divC "search" [
        voidElem "input" [ attr "type" "search"
                           attr "name" "q"
                           attr "class" "search__input"
                           attr "placeholder" placeholder
                           attr "aria-label" placeholder
                           attr "autocomplete" "off" ]
        divC "search__results" [ pC "search__hint" [ text noQueryMsg ] ]
    ]
    jsonBlock "__SEARCH_INDEX__" index
    jsonBlock "__SEARCH_TEXT__" {| empty = emptyMsg; noQuery = noQueryMsg |}
    js """
        (function () {
          const index = window.__SEARCH_INDEX__ || [];
          const text = window.__SEARCH_TEXT__ || { empty: '', noQuery: '' };
          const input = document.querySelector('.search__input');
          const results = document.querySelector('.search__results');
          if (!input || !results) return;
          const esc = (s) => String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
          const render = (list) => {
            if (!list.length) {
              results.innerHTML = '<p class="search__empty">' + esc(text.empty) + '</p>';
              return;
            }
            results.innerHTML = list.map((p) => {
              const tags = (p.tags || []).map((t) =>
                '<a href="/tags/' + encodeURIComponent(t) + '/">#' + esc(t) + '</a>').join('');
              return '<article class="post-list__item">'
                + '<div class="post-list__meta"><time datetime="' + esc(p.date) + '">' + esc(p.date) + '</time></div>'
                + '<h3 class="post-list__title"><a href="' + esc(p.url) + '">' + esc(p.title) + '</a></h3>'
                + (tags ? '<div class="post-list__tags">' + tags + '</div>' : '')
                + (p.description ? '<p class="post-list__excerpt">' + esc(p.description) + '</p>' : '')
                + '</article>';
            }).join('');
          };
          const search = () => {
            const q = input.value.trim().toLowerCase();
            if (!q) {
              results.innerHTML = '<p class="search__hint">' + esc(text.noQuery) + '</p>';
              return;
            }
            const terms = q.split(/\s+/);
            const hits = index.filter((p) => {
              const haystack = (p.title + ' ' + (p.tags || []).join(' ') + ' ' + p.description).toLowerCase();
              return terms.every((term) => haystack.indexOf(term) !== -1);
            });
            render(hits);
          };
          input.addEventListener('input', search);
          input.addEventListener('search', search); // native clear button
        })();
    """
]
