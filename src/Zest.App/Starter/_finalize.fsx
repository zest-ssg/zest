// _finalize.fsx — Zest post-build script.
//
// Runs once per build, after every page and asset has been written to the
// output directory. It must sit at the project root under exactly this name; it
// is never discovered in subdirectories, never routed, and never published.
//
// Its job is the finished output — inspect it, index it, reshape it. It cannot
// feed values back into templates (that is _prebuild.fsx, which runs before
// anything is rendered), and it may only write inside the output directory, so
// a mistake here can never reach the sources.
//
// Available context:
//
//   site        title, url, description, author, language, version
//   pages       every routed page: route, output, title, source
//   build       duration_ms, page_count, asset_count, output_bytes, started_at
//   output_dir  absolute path of the build output
//
// Available helpers:
//
//   formatHtml html          pretty-print HTML with two-space indentation
//   minifyHtml html          strip HTML whitespace and comments
//   formatCss css            pretty-print CSS
//   minifyCss css            compress CSS
//   formatJs js              pretty-print JavaScript
//   minifyJs js              compress JavaScript
//   rewriteFiles ext fn      rewrite every matching file under output_dir
//   writeFile path content   write a file inside output_dir
//   readFile path            read a file, resolved against the project root
//   exists path              does a file or a directory exist
//   listFiles path           site-relative files below a directory
//   sizeOf path              bytes on disk
//   toJson value             serialize a value as indented JSON
//   setFailOnError false     report a problem without failing the build

// A layout is F#, so it composes markup as strings: nesting is carried by the
// tags themselves, not by the indentation around them, and the written HTML
// comes out as one long line per element. That is fine for a browser and bleak
// for anyone reading `_site/` or reviewing a diff — which is why the last thing
// this build does is pretty-print the HTML it just wrote.
//
// `rewriteFiles` walks the whole output directory rather than a list of pages,
// so the home windows the pagination generator emits, the tag and archive
// pages, and anything added later are all covered without naming any of them.
// It rewrites only files whose text actually changed, so a rebuild of an
// unchanged site leaves every timestamp — and every incremental cache — alone.
//
// `formatHtml` re-indents the markup but never reflows text: it leaves the
// contents of <pre>, <script> and <style> untouched, keeps inline runs such as
// <a>, <span> and <code> on their own line, and preserves text spacing, so the
// rendered page is byte-for-byte what it was before the pass.
rewriteFiles ".html" formatHtml

// More examples — uncomment what you need.
//
// Compress instead of pretty-print, for a deployed copy:
//
//   rewriteFiles ".html" minifyHtml
//
// Fail the build when a page links to something that was never written:
//
//   let routes = pages |> List.map (fun p -> p.route) |> Set.ofList
//   for page in pages do
//       let html = readFile page.output
//       for m in Regex.Matches(html, "href=\"(/[^\"]*)\"") do
//           let href = m.Groups.[1].Value
//           if not (routes.Contains href) && not (exists ("_site" + href)) then
//               eprintfn "  dead link on %s: %s" page.route href
//
// Emit a build manifest next to the site:
//
//   writeFile "build.json" (toJson {| pages = build.page_count; bytes = build.output_bytes |})
