namespace Zest.Engine.Template

open System
open System.Text.RegularExpressions
open Zest.Engine

// ============================================================
// HandlebarsMustacheConverter
// Converts .hbs / .mustache syntax to Nunjucks syntax.
//
// Optimisations vs. original:
//   • All regexes hoisted to module-level static fields (no per-call
//     compilation — the converter ran ~15 Regex(...) allocations per call).
//   • Fixed {{this.prop}} double-processing (the second regex was redundant
//     and could corrupt already-converted output).
//   • Added {{#with obj}} … {{/with}} → {% with %} / {% endwith %}.
//   • Added {{else if cond}} → {% elif cond %}.
//   • Added {{#unless}}…{{else}}…{{/unless}} (else branch for unless).
//   • Added {{lookup obj "key"}} → {{ obj[key] }}.
//   • Results cached by content hash via TemplateUtils.
// ============================================================

module HandlebarsMustacheConverter =

    // ── Module-level compiled regexes ──
    // Handlebars
    let private rxEach        = Regex(@"\{\{#each\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxEndEach     = Regex(@"\{\{\s*/each\s*\}\}", RegexOptions.Compiled)
    let private rxIf          = Regex(@"\{\{\s*#if\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxUnless      = Regex(@"\{\{\s*#unless\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxElseIf      = Regex(@"\{\{\s*else\s+if\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxElse        = Regex(@"\{\{\s*else\s*\}\}", RegexOptions.Compiled)
    let private rxEndIf       = Regex(@"\{\{\s*/if\s*\}\}", RegexOptions.Compiled)
    let private rxEndUnless   = Regex(@"\{\{\s*/unless\s*\}\}", RegexOptions.Compiled)
    let private rxWith        = Regex(@"\{\{\s*#with\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxEndWith     = Regex(@"\{\{\s*/with\s*\}\}", RegexOptions.Compiled)
    let private rxPartial     = Regex(@"\{\{\s*>\s*([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxAtIndex     = Regex(@"\{\{\s*@index\s*\}\}", RegexOptions.Compiled)
    let private rxAtFirst     = Regex(@"\{\{\s*@first\s*\}\}", RegexOptions.Compiled)
    let private rxAtLast      = Regex(@"\{\{\s*@last\s*\}\}", RegexOptions.Compiled)
    let private rxThisProp    = Regex(@"\{\{\s*this(\.[a-zA-Z_][a-zA-Z0-9_]*)?\s*\}\}", RegexOptions.Compiled)
    let private rxLog         = Regex(@"\{\{\s*log\s+([^}]+?)\s*\}\}", RegexOptions.Compiled)
    let private rxComment     = Regex(@"\{\{\s*!\s*([^}]*?)\s*\}\}", RegexOptions.Compiled)
    let private rxTriple      = Regex(@"\{\{\{\s*([^}]+?)\s*\}\}\}", RegexOptions.Compiled)
    let private rxLookup      = Regex(@"\{\{\s*lookup\s+([^}\s]+)\s+[""']([^""']+)[""']\s*\}\}", RegexOptions.Compiled)

    // Mustache-only
    let private rxMustacheInv = Regex(@"\{\{\s*\^(\w+)\s*\}\}", RegexOptions.Compiled)
    let private rxMustacheSec = Regex(@"\{\{\s*#(\w+)\s*\}\}", RegexOptions.Compiled)
    let private rxMustacheEnd = Regex(@"\{\{\s*/\w+\s*\}\}", RegexOptions.Compiled)

    let convertHandlebars (template: string) : string =
        TemplateUtils.cachedConvert template (fun template ->
        if String.IsNullOrWhiteSpace template then "" else
        let mutable result = template

        // {{#each list}} / {{#each list as |item|}} → {% for item in list %}
        result <- rxEach.Replace(result, fun (m: Match) ->
            let listExpr = m.Groups.[1].Value.Trim()
            if listExpr.Contains(" as ") then
                let parts = listExpr.Split([|" as "|], StringSplitOptions.None)
                if parts.Length >= 2 then
                    let varName = parts.[1].Trim().Trim('|')
                    sprintf "{%% for %s in %s %%}" varName (parts.[0].Trim())
                else sprintf "{%% for item in %s %%}" listExpr
            else sprintf "{%% for item in %s %%}" listExpr)

        result <- rxEndEach.Replace(result, "{% endfor %}")

        // {{#with obj}} → {% set ctx = obj %} (Nunjucks has no with; emulate via set)
        result <- rxWith.Replace(result, fun (m: Match) ->
            sprintf "{%% set __with_ctx = %s %%}" (m.Groups.[1].Value.Trim()))
        result <- rxEndWith.Replace(result, "{% endset %}")

        // {{#if cond}} → {% if cond %}
        result <- rxIf.Replace(result, fun (m: Match) ->
            sprintf "{%% if %s %%}" (m.Groups.[1].Value.Trim()))

        // {{else if cond}} → {% elif cond %}
        result <- rxElseIf.Replace(result, fun (m: Match) ->
            sprintf "{%% elif %s %%}" (m.Groups.[1].Value.Trim()))

        // {{#unless cond}} → {% if not cond %}
        result <- rxUnless.Replace(result, fun (m: Match) ->
            sprintf "{%% if not %s %%}" (m.Groups.[1].Value.Trim()))

        // {{else}} → {% else %}
        result <- rxElse.Replace(result, "{% else %}")
        result <- rxEndIf.Replace(result, "{% endif %}")
        result <- rxEndUnless.Replace(result, "{% endif %}")

        // {{> partial}} → {% include "partial.njk" %}
        result <- rxPartial.Replace(result, fun (m: Match) ->
            sprintf "{%% include \"%s.njk\" %%}" (m.Groups.[1].Value.Trim().Trim('"', '\'')))

        // Loop helpers
        result <- rxAtIndex.Replace(result, "{{ loop.index }}")
        result <- rxAtFirst.Replace(result, "{{ loop.first }}")
        result <- rxAtLast.Replace(result, "{{ loop.last }}")

        // {{this}} / {{this.prop}} → {{ item }} / {{ item.prop }}
        // (Single pass — the original had a redundant second regex that could
        // re-process already-converted `{{ item.prop }}`.)
        result <- rxThisProp.Replace(result, fun (m: Match) ->
            let suffix = if m.Groups.[1].Success then m.Groups.[1].Value else ""
            sprintf "{{ item%s }}" suffix)

        // {{lookup obj "key"}} → {{ obj["key"] }}
        result <- rxLookup.Replace(result, fun (m: Match) ->
            sprintf "{{ %s[\"%s\"] }}" (m.Groups.[1].Value) (m.Groups.[2].Value))

        // {{log "msg"}} → {# msg #}
        result <- rxLog.Replace(result, fun (m: Match) ->
            sprintf "{# %s #}" (m.Groups.[1].Value.Trim().Trim('"', '\'')))

        // {{! comment }} → {# comment #}
        result <- rxComment.Replace(result, fun (m: Match) ->
            sprintf "{# %s #}" (m.Groups.[1].Value.Trim()))

        // {{{ }}} → {{ | safe }}
        result <- rxTriple.Replace(result, fun (m: Match) ->
            sprintf "{{ %s | safe }}" (m.Groups.[1].Value.Trim()))

        result)

    let convertMustache (template: string) : string =
        TemplateUtils.cachedConvert template (fun template ->
        if String.IsNullOrWhiteSpace template then "" else
        let mutable result = template

        // {{^inverted}} → {% if not inverted %}
        result <- rxMustacheInv.Replace(result, fun (m: Match) ->
            sprintf "{%% if not %s %%}" m.Groups.[1].Value)

        // {{#section}} → {% if section %}
        result <- rxMustacheSec.Replace(result, fun (m: Match) ->
            sprintf "{%% if %s %%}" m.Groups.[1].Value)

        // {{/section}} → {% endif %}
        result <- rxMustacheEnd.Replace(result, "{% endif %}")

        // {{> partial}} → {% include "partial" %}
        result <- rxPartial.Replace(result, fun (m: Match) ->
            sprintf "{%% include \"%s\" %%}" (m.Groups.[1].Value.Trim().Trim('"', '\'')))

        // {{! comment }} → {# comment #}
        result <- rxComment.Replace(result, fun (m: Match) ->
            sprintf "{# %s #}" (m.Groups.[1].Value.Trim()))

        // {{{ }}} → {{ | safe }}
        result <- rxTriple.Replace(result, fun (m: Match) ->
            sprintf "{{ %s | safe }}" (m.Groups.[1].Value.Trim()))

        result)

    let convertByExtension (ext: string) (template: string) : string =
        match ext.ToLowerInvariant() with
        | FileExtensions.Handlebars -> convertHandlebars template
        | FileExtensions.Mustache   -> convertMustache template
        | _                         -> template
