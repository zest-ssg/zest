<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#inicio-rápido">Inicio rápido</a> · <a href="#documentación">Documentación</a>
</p>

---

**Zest** es un generador de sitios estáticos híbrido F# + C# donde las plantillas son código real — no cadenas de texto. Construido sobre la filosofía de que tu lenguaje de plantillas y tu lenguaje anfitrión deben ser uno solo.

## Características

- **Plantilla como Código** — `.zpage.fsx` son scripts F# reales ejecutados en tiempo de compilación mediante `dotnet fsi`. F# completo: comprensiones de listas, coincidencia de patrones, interpolación de cadenas, cálculo arbitrario.
- **`.zhtml` Páginas Ligeras** — Páginas HTML puras con sintaxis de plantilla Nunjucks opcional. Sin sobrecarga de FSI.
- **HTML DSL** — Compón HTML declarativamente: `render [ h1 []; p [] ]`.
- **Markdown** — Archivos `.md` estándar con soporte frontmatter.
- **ZCSS** — Un superconjunto de CSS con anidamiento, enlaces `let` estilo F#, expresiones matemáticas, funciones de color y mixins — compilado a CSS estándar.
- **Plantillas ZestNjk** — Motor de plantillas compatible con Nunjucks para layouts: filtros, expresiones, macros, `{% if %}`, `{% for %}`, herencia de plantillas, integración con API Zest. Usa extensión `.znjk`.
- **`_init.fsx`** — Script de inicialización opcional (se ejecuta antes de la compilación) para inyectar datos dinámicos, cargar JSON/TOML, leer variables de entorno.
- **Configuración TOML** — Valores predeterminados sin configuración; personalización mediante `_config.toml` y `_data/*.toml`. Sin YAML.
- **Recarga en Vivo** — `zest serve` observa cambios y reconstruye automáticamente.
- **Evaluación por Lotes** — Múltiples scripts de página F# evaluados en un solo proceso FSI para compilaciones rápidas.
- **Compilaciones Incrementales** — La detección de cambios en archivos omite páginas y activos no modificados.
- **Multiplataforma** — Compilaciones para Windows x64, Linux x64/ARM64, macOS ARM64.

## Inicio Rápido

```bash
# Crear un nuevo proyecto
zest init my-site

# Desarrollar con recarga en vivo
cd my-site && zest serve --port 8080

# Compilación de producción
zest build

# Previsualizar el sitio compilado
zest preview
```

## Ejemplo: Página `.zpage.fsx`

```fsharp
// @title Hello World
// @layout default
// @description Mi primera página Zest

let pageTitle = "Hola desde F#"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "Esta página es generada por código F# real en tiempo de compilación." ]
    ul [ for i in items -> li [ text i ] ]
]
```

## Ejemplo: Hoja de Estilo ZCSS

```zcss
// Enlaces let estilo F# con expresiones matemáticas
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// Abreviaturas de propiedad de dos letras
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

Compilado a:

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## Ejemplo: `_init.fsx`

```fsharp
// _init.fsx — se ejecuta antes de cada compilación
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## Estructura del Proyecto

```
my-site/
├── _config.toml            # Configuración del sitio (TOML)
├── _init.zpage.fsx         # Script de inicialización opcional
├── _data/
│   └── site.toml           # Datos globales (accesibles desde scripts/plantillas)
├── content/
│   ├── index.zpage.fsx     # Página de inicio (plantilla script F#)
│   ├── about.md            # Página Acerca de (Markdown)
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # HTML puro (sin sobrecarga FSI)
├── _layouts/
│   ├── default.html        # Layouts (Nunjucks o reemplazo nativo)
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → compilado automáticamente a style.css
└── _site/                  # Salida de compilación (generada automáticamente)
```

## Arquitectura

| Proyecto | Lenguaje | Responsabilidad |
|---------|----------|----------------|
| **Zest.App** | C# | Punto de entrada CLI, enrutamiento de comandos |
| **Zest.Engine** | F# | Motor principal: compilaciones, HTML DSL, ScriptRunner, Markdown, compilador ZCSS, motor de plantillas ZestNjk |
| **Zest.Dsl** | F# | Ayudantes DSL precompilados para evaluación de scripts FSI |
| **Zest.Infra** | C# | Carga de configuración, vigilancia de archivos, servidor de desarrollo |

## Compilar desde el Código Fuente

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# Publicar para tu plataforma
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## Documentación

### Tipos de Archivo

| Extensión | Propósito | Procesamiento |
|-----------|---------|------------|
| `.zpage.fsx` | Plantillas script F# (F# + Markdown + HTML DSL) | Compilado mediante `dotnet fsi` |
| `.znjk` | Plantillas Zest Nunjucks (sintaxis compatible con Nunjucks + integración API Zest) | Renderizado mediante ZestNjkEngine — soporta filtros, expresiones, `{% if %}`, `{% for %}`, macros, herencia de plantillas |
| `.zcss` | Hojas de estilo ZCSS (superconjunto CSS) | Compilado a `.css` |
| `.md` | Markdown estándar | Renderizado a HTML |
| `.toml` | Configuración y datos (sin YAML) | Analizado en tiempo de compilación |

### Comandos

| Comando | Descripción |
|---------|-------------|
| `zest build` | Construir el sitio en `_site/` |
| `zest serve` | Iniciar servidor de desarrollo con recarga en vivo |
| `zest preview` | Previsualizar el sitio construido |
| `zest init <name>` | Crear un nuevo proyecto |
| `zest clean` | Limpiar la salida de compilación |

### Referencia ZCSS

| Funcionalidad | Sintaxis |
|---------|--------|
| Variables (SCSS) | `$name: value;` |
| Variables (F#) | `let name = value` |
| Matemáticas | `let x = 0.25r * 4` |
| Funciones de color | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| Operador pipe | `value \|> fn(args)` → `fn(value, args)` |
| Abreviaturas de unidad | `r`→`rem`, `p`→`%` |
| Abreviaturas de propiedad | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| Anidamiento | Modo indentación o llaves |
| Mixins | `@mixin`, `@include` |
| Bucles | `@each`, `@for` |
| Condicionales | `@if`, `@else` |
| Módulos integrados | `@use "zest:utilities"`, `@use "zest:palette"`, etc. |

### Motores de Layout

| Motor | Valor de Config | Funcionalidades |
|--------|-------------|----------|
| **ZestNjk** (predeterminado) | `template_engine = "znjk"` | Filtros, expresiones, `{% if %}`, `{% for %}`, macros, herencia de plantillas, filtros API Zest (`pages_by_tag`, `recent`, `by_collection`, `search`, `where`) |
| **Reemplazo Nativo** | `template_engine = "replace"` | Sustitución simple `{{ variable }}` |

### Referencia HTML DSL

```fsharp
// Elementos
h1 [ text "Título" ]
p  [ text "Párrafo" ]
a  [ href "https://example.com"; text "Enlace" ]

// Atributos
div [ class' "container"; id "main" ] [ ... ]

// Atajos de clase CSS
divC "card" [ p [ text "Contenido" ] ]   // <div class="card">
spanC "badge" [ text "Nuevo" ]           // <span class="badge">

// Comprensiones de lista
ul [ for item in items -> li [ text item ] ]

// Condicionales
if condition then
    p [ text "Sí" ]
else
    p [ text "No" ]
```

### API `_init.fsx`

| Función | Propósito |
|----------|---------|
| `addGlobal key value` | Inyectar par clave-valor en datos globales |
| `loadJson path` | Analizar archivo JSON |
| `loadToml path` | Analizar archivo TOML |
| `loadEnv key` | Leer variable de entorno |
| `console_log msg` | Salida de depuración a stderr |
| `exec cmd args` | Ejecutar comando shell |

## Filosofía de Diseño

**Zest no es un generador de sitios estáticos de propósito general.** Es una respuesta específica a restricciones específicas.

1. **F# como Plantilla** — La plantilla es el programa. Los archivos `.zpage.fsx` son código F# real, no cadenas.
2. **ZCSS como Motor de Layout** — No es un preprocesador CSS, sino un motor de layout que emite CSS.
3. **TOML como Contrato** — Sin YAML. Nunca.
4. **JavaScript como Orden** — Sin Node.js, sin npm, sin bundlers. JavaScript existe solo para interactividad del lado del cliente.
5. **Los Pocos Entusiastas** — Construido para quienes aman F#, odian YAML y prefieren herramientas simples.

## Licencia

Apache 2.0 — ver [LICENSE](LICENSE).
