<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#schnellstart">Schnellstart</a> · <a href="#dokumentation">Dokumentation</a>
</p>

---

**Zest** ist ein hybrider F# + C# Static Site Generator, bei dem Templates echter Code sind – keine Zeichenketten. Aufgebaut auf der Philosophie, dass Template-Sprache und Host-Sprache eins sein sollten.

## Funktionen

- **Template als Code** — `.zpage.fsx` sind echte F#-Skripte, die zur Build-Zeit via `dotnet fsi` ausgeführt werden. Volles F#: List Comprehensions, Pattern Matching, String-Interpolation, beliebige Berechnungen.
- **`.zhtml` Leichtgewicht-Seiten** — Reine HTML-Seiten mit optionaler Nunjucks-Template-Syntax. Kein FSI-Overhead.
- **HTML DSL** — Deklarative HTML-Komposition: `render [ h1 []; p [] ]`.
- **Markdown** — Standard `.md`-Dateien mit Frontmatter-Unterstützung.
- **ZCSS** — Ein CSS-Superset mit Verschachtelung, F#-artigen `let`-Bindings, mathematischen Ausdrücken, Farbfunktionen und Mixins — kompiliert zu Standard-CSS.
- **ZestNjk Templates** — Nunjucks-kompatible Template-Engine für Layouts: Filter, Ausdrücke, Makros, `{% if %}`, `{% for %}`, Template-Vererbung, Zest-API-Integration. Verwendet `.znjk`-Erweiterung.
- **`_init.fsx`** — Optionales Initialisierungsskript (läuft vor dem Build) zum Injizieren dynamischer Daten, Laden von JSON/TOML, Lesen von Umgebungsvariablen.
- **TOML-Konfiguration** — Zero-Config-Standardwerte; Anpassung über `_config.toml` und `_data/*.toml`. Kein YAML.
- **Live Reload** — `zest serve` überwacht Änderungen und baut automatisch neu.
- **Batch-Auswertung** — Mehrere F#-Seitenskripte werden in einem einzigen FSI-Prozess ausgewertet für schnelle Builds.
- **Inkrementelle Builds** — Dateiänderungserkennung überspringt unveränderte Seiten und Assets.
- **Plattformübergreifend** — Builds für Windows x64, Linux x64/ARM64, macOS ARM64.

## Schnellstart

```bash
# Neues Projekt erstellen
zest init my-site

# Entwicklung mit Live Reload
cd my-site && zest serve --port 8080

# Produktions-Build
zest build

# Gebaute Seite vorschauen
zest preview
```

## Beispiel: `.zpage.fsx` Seite

```fsharp
// @title Hello World
// @layout default
// @description Meine erste Zest-Seite

let pageTitle = "Hallo von F#"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "Diese Seite wird zur Build-Zeit von echtem F#-Code generiert." ]
    ul [ for i in items -> li [ text i ] ]
]
```

## Beispiel: ZCSS Stylesheet

```zcss
// F#-artige let-Bindings mit mathematischen Ausdrücken
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// Zwei-Buchstaben-Eigenschafts-Abkürzungen
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

Kompiliert zu:

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## Beispiel: `_init.fsx`

```fsharp
// _init.fsx — wird vor jedem Build ausgeführt
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## Projektstruktur

```
my-site/
├── _config.toml            # Seitenkonfiguration (TOML)
├── _init.zpage.fsx         # Optionales Initialisierungsskript
├── _data/
│   └── site.toml           # Globale Daten (zugänglich aus Skripten/Templates)
├── content/
│   ├── index.zpage.fsx     # Startseite (F#-Skript-Template)
│   ├── about.md            # Über-Seite (Markdown)
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # Reines HTML (kein FSI-Overhead)
├── _layouts/
│   ├── default.html        # Layouts (Nunjucks oder native Ersetzung)
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → automatisch zu style.css kompiliert
└── _site/                  # Build-Ausgabe (automatisch generiert)
```

## Architektur

| Projekt | Sprache | Verantwortung |
|---------|----------|----------------|
| **Zest.App** | C# | CLI-Einstiegspunkt, Befehls-Routing |
| **Zest.Engine** | F# | Kern-Engine: Builds, HTML DSL, ScriptRunner, Markdown, ZCSS-Compiler, ZestNjk-Template-Engine |
| **Zest.Dsl** | F# | Vorkompilierte DSL-Helfer für FSI-Skriptauswertung |
| **Zest.Infra** | C# | Konfigurationsladung, Dateiüberwachung, Entwicklungsserver |

## Aus dem Quellcode bauen

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# Für Ihre Plattform veröffentlichen
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## Dokumentation

### Dateitypen

| Erweiterung | Zweck | Verarbeitung |
|-----------|---------|------------|
| `.zpage.fsx` | F#-Skript-Templates (F# + Markdown + HTML DSL) | Kompiliert via `dotnet fsi` |
| `.znjk` | Zest Nunjucks Templates (Nunjucks-kompatible Syntax + Zest-API-Integration) | Gerendert via ZestNjkEngine — unterstützt Filter, Ausdrücke, `{% if %}`, `{% for %}`, Makros, Template-Vererbung |
| `.zcss` | ZCSS Stylesheets (CSS-Superset) | Kompiliert zu `.css` |
| `.md` | Standard Markdown | Zu HTML gerendert |
| `.toml` | Konfiguration und Daten (kein YAML) | Zur Build-Zeit geparst |

### Befehle

| Befehl | Beschreibung |
|---------|-------------|
| `zest build` | Seite nach `_site/` bauen |
| `zest serve` | Entwicklungsserver mit Live Reload starten |
| `zest preview` | Gebaute Seite vorschauen |
| `zest init <name>` | Neues Projekt erstellen |
| `zest clean` | Build-Ausgabe bereinigen |

### ZCSS-Referenz

| Funktion | Syntax |
|---------|--------|
| Variablen (SCSS) | `$name: value;` |
| Variablen (F#) | `let name = value` |
| Mathematik | `let x = 0.25r * 4` |
| Farbfunktionen | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| Pipe-Operator | `value \|> fn(args)` → `fn(value, args)` |
| Einheiten-Abkürzungen | `r`→`rem`, `p`→`%` |
| Eigenschafts-Abkürzungen | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| Verschachtelung | Einrückungs- oder Klammer-Modus |
| Mixins | `@mixin`, `@include` |
| Schleifen | `@each`, `@for` |
| Bedingungen | `@if`, `@else` |
| Eingebaute Module | `@use "zest:utilities"`, `@use "zest:palette"`, usw. |

### Layout-Engines

| Engine | Konfigurationswert | Funktionen |
|--------|-------------|----------|
| **ZestNjk** (Standard) | `template_engine = "znjk"` | Filter, Ausdrücke, `{% if %}`, `{% for %}`, Makros, Template-Vererbung, Zest-API-Filter (`pages_by_tag`, `recent`, `by_collection`, `search`, `where`) |
| **Native Ersetzung** | `template_engine = "replace"` | Einfache `{{ variable }}`-Ersetzung |

### HTML DSL-Referenz

```fsharp
// Elemente
h1 [ text "Titel" ]
p  [ text "Absatz" ]
a  [ href "https://example.com"; text "Link" ]

// Attribute
div [ class' "container"; id "main" ] [ ... ]

// CSS-Klassen-Abkürzungen
divC "card" [ p [ text "Inhalt" ] ]   // <div class="card">
spanC "badge" [ text "Neu" ]          // <span class="badge">

// List Comprehensions
ul [ for item in items -> li [ text item ] ]

// Bedingungen
if condition then
    p [ text "Ja" ]
else
    p [ text "Nein" ]
```

### `_init.fsx` API

| Funktion | Zweck |
|----------|---------|
| `addGlobal key value` | Schlüssel-Wert in globale Daten injizieren |
| `loadJson path` | JSON-Datei parsen |
| `loadToml path` | TOML-Datei parsen |
| `loadEnv key` | Umgebungsvariable lesen |
| `console_log msg` | Debug-Ausgabe nach stderr |
| `exec cmd args` | Shell-Befehl ausführen |

## Design-Philosophie

**Zest ist kein universeller Static Site Generator.** Es ist eine spezifische Antwort auf spezifische Einschränkungen.

1. **F# als Template** — Das Template ist das Programm. `.zpage.fsx`-Dateien sind echter F#-Code, keine Zeichenketten.
2. **ZCSS als Layout-Engine** — Kein CSS-Präprozessor, sondern eine Layout-Engine, die CSS ausgibt.
3. **TOML als Vertrag** — Kein YAML. Niemals.
4. **JavaScript als Ordnung** — Kein Node.js, kein npm, keine Bundler. JavaScript existiert nur für clientseitige Interaktivität.
5. **Die Eifrigen Wenigen** — Gebaut für diejenigen, die F# lieben, YAML hassen und einfache Werkzeuge bevorzugen.

## Lizenz

Apache 2.0 — siehe [LICENSE](LICENSE).
