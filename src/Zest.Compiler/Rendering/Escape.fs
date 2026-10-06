// Escape.fs
//
// The single place HTML escaping happens.
//
// Escaping used to be spread over the HTML writer, the attribute builders, the
// comment helpers and the Markdown renderer, which is how a value ends up
// escaped twice in one path (producing visible `&amp;amp;`) and not at all in
// another (producing an injection). Every one of those paths now calls in here.
//
// The rule this file encodes: escaping happens at the *output* boundary, on the
// final string, and only there. A helper that builds markup returns a node;
// only the renderer turns nodes into text.
//
// Dependencies: Zest.Compiler.Model

namespace Zest.Compiler.Rendering
open System
open System.Collections.Concurrent
open System.Net
open System.Text.RegularExpressions
open Zest.Compiler.Model

/// Context-aware HTML escaping.
module Escape =

    /// Characters that let an attribute *name* escape its own token and become
    /// more markup: whitespace ends the name, `=` starts a value, and the quote
    /// characters close the attribute.
    ///
    /// A denylist rather than an allowlist, because framework attributes such as
    /// `x-on:click`, `@click` and `v-bind:href` are all legitimate and none of
    /// them need any of the characters below.
    let private isForbiddenInAttrName (c: char) =
        Char.IsWhiteSpace c || Char.IsControl c
        || c = '=' || c = '<' || c = '>' || c = '"' || c = '\'' || c = '`'

    /// A syntactically usable attribute name.
    let isValidAttrName (name: string) =
        not (String.IsNullOrEmpty name) && not (name |> Seq.exists isForbiddenInAttrName)

    // Names already reported, so a bad attribute on a 500-page site produces
    // one warning rather than 500.
    let private reportedNames = ConcurrentDictionary<string, byte>()

    /// Warn — once per distinct name — that an attribute was dropped.
    let reportInvalidAttrName (name: string) =
        if reportedNames.TryAdd(name, 0uy) then
            Diagnostics.warn
                "[Zest] Dropped an HTML attribute whose name is not a valid token: '%s'. \
                 Attribute names must not be built from untrusted text."
                name

    /// Encode an attribute value for a double-quoted attribute. Covers `&`,
    /// `<`, `>`, `"` and `'`, which is everything that can end the attribute.
    let inline escapeAttrValue (value: string) : string = WebUtility.HtmlEncode value

    /// Encode element text content.
    let inline escapeText (text: string) : string = WebUtility.HtmlEncode text

    /// Escape text for the body of an HTML comment.
    ///
    /// `--` is not permitted inside a comment, and a value containing `-->`
    /// closes the comment and injects everything after it as live markup.
    let escapeComment (text: string) : string =
        if String.IsNullOrEmpty text then ""
        else
            text.Replace("--", "- -")
                .Replace(">", "&gt;")
                .Replace("<", "&lt;")

    let private rawCloseCache = ConcurrentDictionary<string, Regex>()

    /// Neutralise a literal closing tag inside a `Raw` block of script or style
    /// text.
    ///
    /// `<style>` and `<script>` hold `Raw` content, so a generated stylesheet or
    /// an inline script whose *content* contains `</style>` ends the element and
    /// turns the rest into live markup. Rewriting `</style` to `<\/style` closes
    /// that hole: both CSS and JavaScript treat `\/` inside a string or regular
    /// expression as an escaped `/`, so the code still means the same thing.
    let escapeRawTagClose (tag: string) (text: string) : string =
        if String.IsNullOrEmpty text then text
        else
            let re =
                rawCloseCache.GetOrAdd(tag, fun t ->
                    Regex("</(?=" + Regex.Escape t + ")", RegexOptions.IgnoreCase))
            re.Replace(text, "<\\/")
