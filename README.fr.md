<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#démarrage-rapide">Démarrage rapide</a> · <a href="#documentation">Documentation</a>
</p>

---

**Zest** est un générateur de site statique hybride F# + C# où les templates sont du vrai code — pas des chaînes de caractères. Construit sur la philosophie que votre langage de templating et votre langage hôte ne devraient faire qu'un.

## Fonctionnalités

- **Template comme Code** — `.zpage.fsx` sont de vrais scripts F# exécutés au moment du build via `dotnet fsi`. F# complet : compréhensions de liste, pattern matching, interpolation de chaînes, calcul arbitraire.
- **`.zhtml` Pages Légères** — Pages HTML pures avec syntaxe de template Nunjucks optionnelle. Sans surcharge FSI.
- **HTML DSL** — Composez du HTML de manière déclarative : `render [ h1 []; p [] ]`.
- **Markdown** — Fichiers `.md` standards avec support frontmatter.
- **ZCSS** — Un sur-ensemble CSS avec imbrication, liaisons `let` style F#, expressions mathématiques, fonctions de couleur et mixins — compilé en CSS standard.
- **Templates ZestNjk** — Moteur de template compatible Nunjucks pour les layouts : filtres, expressions, macros, `{% if %}`, `{% for %}`, héritage de template, intégration API Zest. Extension `.znjk`.
- **`_init.fsx`** — Script d'initialisation optionnel (exécuté avant le build) pour injecter des données dynamiques, charger du JSON/TOML, lire des variables d'environnement.
- **Configuration TOML** — Valeurs par défaut zéro-config ; personnalisation via `_config.toml` et `_data/*.toml`. Pas de YAML.
- **Rechargement à Chaud** — `zest serve` surveille les modifications et reconstruit automatiquement.
- **Évaluation par Lots** — Plusieurs scripts de page F# évalués dans un seul processus FSI pour des builds rapides.
- **Builds Incrémentaux** — La détection de changements de fichiers ignore les pages et assets inchangés.
- **Multi-Plateforme** — Builds pour Windows x64, Linux x64/ARM64, macOS ARM64.

## Démarrage Rapide

```bash
# Créer un nouveau projet
zest init my-site

# Développer avec rechargement à chaud
cd my-site && zest serve --port 8080

# Build de production
zest build

# Prévisualiser le site construit
zest preview
```

## Exemple : Page `.zpage.fsx`

```fsharp
// @title Hello World
// @layout default
// @description Ma première page Zest

let pageTitle = "Bonjour depuis F#"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "Cette page est générée par du vrai code F# au moment du build." ]
    ul [ for i in items -> li [ text i ] ]
]
```

## Exemple : Feuille de Style ZCSS

```zcss
// Liaisons let style F# avec expressions mathématiques
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// Raccourcis de propriété à deux lettres
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

Compilé en :

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## Exemple : `_init.fsx`

```fsharp
// _init.fsx — exécuté avant chaque build
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## Structure du Projet

```
my-site/
├── _config.toml            # Configuration du site (TOML)
├── _init.zpage.fsx         # Script d'initialisation optionnel
├── _data/
│   └── site.toml           # Données globales (accessibles depuis scripts/templates)
├── content/
│   ├── index.zpage.fsx     # Page d'accueil (template script F#)
│   ├── about.md            # Page À propos (Markdown)
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # HTML pur (sans surcharge FSI)
├── _layouts/
│   ├── default.html        # Layouts (Nunjucks ou remplacement natif)
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → compilé automatiquement en style.css
└── _site/                  # Sortie de build (générée automatiquement)
```

## Architecture

| Projet | Langage | Responsabilité |
|---------|----------|----------------|
| **Zest.App** | C# | Point d'entrée CLI, routage des commandes |
| **Zest.Engine** | F# | Moteur principal : builds, HTML DSL, ScriptRunner, Markdown, compilateur ZCSS, moteur de template ZestNjk |
| **Zest.Dsl** | F# | Helpers DSL précompilés pour l'évaluation de scripts FSI |
| **Zest.Infra** | C# | Chargement de configuration, surveillance de fichiers, serveur de développement |

## Compiler depuis les Sources

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# Publier pour votre plateforme
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## Documentation

### Types de Fichiers

| Extension | Objectif | Traitement |
|-----------|---------|------------|
| `.zpage.fsx` | Templates script F# (F# + Markdown + HTML DSL) | Compilé via `dotnet fsi` |
| `.znjk` | Templates Zest Nunjucks (syntaxe compatible Nunjucks + intégration API Zest) | Rendu via ZestNjkEngine — prend en charge filtres, expressions, `{% if %}`, `{% for %}`, macros, héritage de template |
| `.zcss` | Feuilles de style ZCSS (sur-ensemble CSS) | Compilé en `.css` |
| `.md` | Markdown standard | Rendu en HTML |
| `.toml` | Configuration et données (pas de YAML) | Analysé au moment du build |

### Commandes

| Commande | Description |
|---------|-------------|
| `zest build` | Construire le site dans `_site/` |
| `zest serve` | Démarrer le serveur de développement avec rechargement à chaud |
| `zest preview` | Prévisualiser le site construit |
| `zest init <name>` | Créer un nouveau projet |
| `zest clean` | Nettoyer la sortie de build |

### Référence ZCSS

| Fonctionnalité | Syntaxe |
|---------|--------|
| Variables (SCSS) | `$name: value;` |
| Variables (F#) | `let name = value` |
| Mathématiques | `let x = 0.25r * 4` |
| Fonctions de couleur | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| Opérateur pipe | `value \|> fn(args)` → `fn(value, args)` |
| Raccourcis d'unité | `r`→`rem`, `p`→`%` |
| Raccourcis de propriété | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| Imbrication | Mode indentation ou accolades |
| Mixins | `@mixin`, `@include` |
| Boucles | `@each`, `@for` |
| Conditionnels | `@if`, `@else` |
| Modules intégrés | `@use "zest:utilities"`, `@use "zest:palette"`, etc. |

### Moteurs de Layout

| Moteur | Valeur de Config | Fonctionnalités |
|--------|-------------|----------|
| **ZestNjk** (par défaut) | `template_engine = "znjk"` | Filtres, expressions, `{% if %}`, `{% for %}`, macros, héritage de template, filtres API Zest (`pages_by_tag`, `recent`, `by_collection`, `search`, `where`) |
| **Remplacement Natif** | `template_engine = "replace"` | Substitution simple `{{ variable }}` |

### Référence HTML DSL

```fsharp
// Éléments
h1 [ text "Titre" ]
p  [ text "Paragraphe" ]
a  [ href "https://example.com"; text "Lien" ]

// Attributs
div [ class' "container"; id "main" ] [ ... ]

// Raccourcis de classe CSS
divC "card" [ p [ text "Contenu" ] ]   // <div class="card">
spanC "badge" [ text "Nouveau" ]       // <span class="badge">

// Compréhensions de liste
ul [ for item in items -> li [ text item ] ]

// Conditionnels
if condition then
    p [ text "Oui" ]
else
    p [ text "Non" ]
```

### API `_init.fsx`

| Fonction | Objectif |
|----------|---------|
| `addGlobal key value` | Injecter une paire clé-valeur dans les données globales |
| `loadJson path` | Analyser un fichier JSON |
| `loadToml path` | Analyser un fichier TOML |
| `loadEnv key` | Lire une variable d'environnement |
| `console_log msg` | Sortie de débogage vers stderr |
| `exec cmd args` | Exécuter une commande shell |

## Philosophie de Conception

**Zest n'est pas un générateur de site statique universel.** C'est une réponse spécifique à des contraintes spécifiques.

1. **F# comme Template** — Le template est le programme. Les fichiers `.zpage.fsx` sont du vrai code F#, pas des chaînes.
2. **ZCSS comme Moteur de Layout** — Pas un préprocesseur CSS, mais un moteur de layout qui émet du CSS.
3. **TOML comme Contrat** — Pas de YAML. Jamais.
4. **JavaScript comme Ordre** — Pas de Node.js, pas de npm, pas de bundlers. JavaScript n'existe que pour l'interactivité côté client.
5. **Les Passionnés** — Construit pour ceux qui aiment F#, détestent YAML et préfèrent les outils simples.

## Licence

Apache 2.0 — voir [LICENSE](LICENSE).
