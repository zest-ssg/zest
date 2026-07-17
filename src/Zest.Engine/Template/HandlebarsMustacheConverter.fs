namespace Zest.Engine.Template

open System
open System.Text
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// HandlebarsMustacheConverter
// Converts .hbs / .mustache syntax to Nunjucks syntax.
// ============================================================

module HandlebarsMustacheConverter =

    let convertHandlebars (template: string) : string =
        if String.IsNullOrWhiteSpace template then ""
        else
            let mutable result = template

            // {{#each list}} → {% for item in list %}
            result <- Regex(@"\{\{#each\s+([^}]+?)\s*\}\}").Replace(result, fun (m: Match) ->
                let listExpr = m.Groups.[1].Value.Trim()
                let listExpr2 =
                    if listExpr.Contains(" as ") then
                        let parts = listExpr.Split([|" as "|], StringSplitOptions.None)
                        if parts.Length >= 2 then
                            let varName = parts.[1].Trim().Trim('|')
                            sprintf "%s in %s" varName (parts.[0].Trim())
                        else listExpr
                    else sprintf "item in %s" listExpr
                sprintf "{%% for %s %%}" listExpr2)

            // {{/each}} → {% endfor %}
            result <- Regex(@"\{\{\s*/each\s*\}\}").Replace(result, "{% endfor %}")

            // {{#if cond}} → {% if cond %}
            result <- Regex(@"\{\{\s*#if\s+([^}]+?)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{%% if %s %%}" (m.Groups.[1].Value.Trim()))

            // {{#unless cond}} → {% if not cond %}
            result <- Regex(@"\{\{\s*#unless\s+([^}]+?)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{%% if not %s %%}" (m.Groups.[1].Value.Trim()))

            // {{else}} → {% else %}
            result <- Regex(@"\{\{\s*else\s*\}\}").Replace(result, "{% else %}")

            // {{/if}} → {% endif %}
            result <- Regex(@"\{\{\s*/if\s*\}\}").Replace(result, "{% endif %}")
            result <- Regex(@"\{\{\s*/unless\s*\}\}").Replace(result, "{% endif %}")

            // {{> partial}} → {% include "partial.njk" %}
            result <- Regex(@"\{\{\s*>\s*([^}]+?)\s*\}\}").Replace(result,
                fun (m: Match) ->
                    let name = m.Groups.[1].Value.Trim().Trim('"', '\'')
                    sprintf "{%% include \"%s.njk\" %%}" name)

            // {{@index}} → {{ loop.index }}
            result <- Regex(@"\{\{\s*@index\s*\}\}").Replace(result, "{{ loop.index }}")
            result <- Regex(@"\{\{\s*@first\s*\}\}").Replace(result, "{{ loop.first }}")
            result <- Regex(@"\{\{\s*@last\s*\}\}").Replace(result, "{{ loop.last }}")

            // {{this}} → {{ item }} and this.prop → item.prop
            result <- Regex(@"\{\{\s*this(\.[a-zA-Z_][a-zA-Z0-9_]*)?\s*\}\}").Replace(result,
                fun (m: Match) ->
                    let suffix = if m.Groups.[1].Success then m.Groups.[1].Value else ""
                    sprintf "{{ item%s }}" suffix)
            // Also handle this inside expression context (after conversions)
            result <- Regex(@"\{\{\s*this\.([a-zA-Z_][a-zA-Z0-9_]*)\s*\}\}").Replace(result, "{{ item.$1 }}")

            // {{log "msg"}} → {# msg #}
            result <- Regex(@"\{\{\s*log\s+([^}]+?)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{# %s #}" (m.Groups.[1].Value.Trim().Trim('"', '\'')))

            // {{! comment }} → {# comment #}
            result <- Regex(@"\{\{\s*!\s*([^}]*?)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{# %s #}" (m.Groups.[1].Value.Trim()))

            // {{{ }}} → {{  | safe }}
            result <- Regex(@"\{\{\{\s*([^}]+?)\s*\}\}\}").Replace(result,
                fun (m: Match) -> sprintf "{{ %s | safe }}" (m.Groups.[1].Value.Trim()))

            result

    let convertMustache (template: string) : string =
        if String.IsNullOrWhiteSpace template then ""
        else
            let mutable result = template

            // {{^inverted}} → {% if not inverted %}
            result <- Regex(@"\{\{\s*\^(\w+)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{%% if not %s %%}" m.Groups.[1].Value)

            // {{#section}} → {% if section %}
            result <- Regex(@"\{\{\s*#(\w+)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{%% if %s %%}" m.Groups.[1].Value)

            // {{/section}} → {% endif %}
            result <- Regex(@"\{\{\s*/\w+\s*\}\}").Replace(result, "{% endif %}")

            // {{> partial}} → {% include "partial" %}
            result <- Regex(@"\{\{\s*>\s*([^}]+?)\s*\}\}").Replace(result,
                fun (m: Match) ->
                    let name = m.Groups.[1].Value.Trim().Trim('"', '\'')
                    sprintf "{%% include \"%s\" %%}" name)

            // {{! comment }} → {# comment #}
            result <- Regex(@"\{\{\s*!\s*([^}]*?)\s*\}\}").Replace(result,
                fun (m: Match) -> sprintf "{# %s #}" (m.Groups.[1].Value.Trim()))

            // {{{ }}} → {{  | safe }}
            result <- Regex(@"\{\{\{\s*([^}]+?)\s*\}\}\}").Replace(result,
                fun (m: Match) -> sprintf "{{ %s | safe }}" (m.Groups.[1].Value.Trim()))

            result

    let convertByExtension (ext: string) (template: string) : string =
        match ext.ToLowerInvariant() with
        | FileExtensions.Handlebars -> convertHandlebars template
        | FileExtensions.Mustache   -> convertMustache template
        | _                         -> template
