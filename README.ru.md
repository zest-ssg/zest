<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#быстрый-старт">Быстрый старт</a> · <a href="#документация">Документация</a>
</p>

---

**Zest** — это гибридный генератор статических сайтов на F# + C#, где шаблоны — это настоящий код, а не строки. Построен на философии, что язык шаблонов и основной язык должны быть единым целым.

## Возможности

- **Шаблон как Код** — `.zpage.fsx` — настоящие F# скрипты, выполняемые во время сборки через `dotnet fsi`. Полный F#: генераторы списков, сопоставление с образцом, интерполяция строк, произвольные вычисления.
- **`.zhtml` Лёгкие Страницы** — Чистые HTML-страницы с опциональным синтаксисом шаблонов Nunjucks. Без накладных расходов FSI.
- **HTML DSL** — Декларативная композиция HTML: `render [ h1 []; p [] ]`.
- **Markdown** — Стандартные `.md` файлы с поддержкой frontmatter.
- **ZCSS** — Надмножество CSS с вложенностью, привязками `let` в стиле F#, математическими выражениями, цветовыми функциями и миксинами — компилируется в стандартный CSS.
- **Шаблоны ZestNjk** — Совместимый с Nunjucks шаблонный движок для макетов: фильтры, выражения, макросы, `{% if %}`, `{% for %}`, наследование шаблонов, интеграция с Zest API. Использует расширение `.znjk`.
- **`_init.fsx`** — Опциональный скрипт инициализации (запускается перед сборкой) для внедрения динамических данных, загрузки JSON/TOML, чтения переменных окружения.
- **Конфигурация TOML** — Настройки по умолчанию без конфигурации; настройка через `_config.toml` и `_data/*.toml`. Никакого YAML.
- **Живая Перезагрузка** — `zest serve` отслеживает изменения и автоматически пересобирает.
- **Пакетная Оценка** — Несколько F# скриптов страниц оцениваются в одном процессе FSI для быстрой сборки.
- **Инкрементальные Сборки** — Обнаружение изменений файлов пропускает неизменённые страницы и ресурсы.
- **Кроссплатформенность** — Сборки для Windows x64, Linux x64/ARM64, macOS ARM64.

## Быстрый Старт

```bash
# Создать новый проект
zest init my-site

# Разработка с живой перезагрузкой
cd my-site && zest serve --port 8080

# Продакшн-сборка
zest build

# Предпросмотр собранного сайта
zest preview
```

## Пример: Страница `.zpage.fsx`

```fsharp
// @title Hello World
// @layout default
// @description Моя первая страница Zest

let pageTitle = "Привет из F#"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "Эта страница сгенерирована настоящим F# кодом во время сборки." ]
    ul [ for i in items -> li [ text i ] ]
]
```

## Пример: Таблица Стилей ZCSS

```zcss
// Привязки let в стиле F# с математическими выражениями
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// Двухбуквенные сокращения свойств
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

Компилируется в:

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## Пример: `_init.fsx`

```fsharp
// _init.fsx — запускается перед каждой сборкой
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## Структура Проекта

```
my-site/
├── _config.toml            # Конфигурация сайта (TOML)
├── _init.zpage.fsx         # Опциональный скрипт инициализации
├── _data/
│   └── site.toml           # Глобальные данные (доступны из скриптов/шаблонов)
├── content/
│   ├── index.zpage.fsx     # Главная страница (F# скрипт-шаблон)
│   ├── about.md            # Страница «О нас» (Markdown)
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # Чистый HTML (без накладных расходов FSI)
├── _layouts/
│   ├── default.html        # Макеты (Nunjucks или нативная замена)
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → автоматически компилируется в style.css
└── _site/                  # Результат сборки (создаётся автоматически)
```

## Архитектура

| Проект | Язык | Ответственность |
|---------|----------|----------------|
| **Zest.App** | C# | Точка входа CLI, маршрутизация команд |
| **Zest.Engine** | F# | Основной движок: сборки, HTML DSL, ScriptRunner, Markdown, компилятор ZCSS, шаблонный движок ZestNjk |
| **Zest.Dsl** | F# | Предкомпилированные помощники DSL для оценки скриптов FSI |
| **Zest.Infra** | C# | Загрузка конфигурации, отслеживание файлов, сервер разработки |

## Сборка из Исходного Кода

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# Опубликовать для вашей платформы
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## Документация

### Типы Файлов

| Расширение | Назначение | Обработка |
|-----------|---------|------------|
| `.zpage.fsx` | F# скрипт-шаблоны (F# + Markdown + HTML DSL) | Компилируется через `dotnet fsi` |
| `.znjk` | Шаблоны Zest Nunjucks (совместимый с Nunjucks синтаксис + интеграция с Zest API) | Рендерится через ZestNjkEngine — поддерживает фильтры, выражения, `{% if %}`, `{% for %}`, макросы, наследование шаблонов |
| `.zcss` | Таблицы стилей ZCSS (надмножество CSS) | Компилируется в `.css` |
| `.md` | Стандартный Markdown | Рендерится в HTML |
| `.toml` | Конфигурация и данные (без YAML) | Разбирается во время сборки |

### Команды

| Команда | Описание |
|---------|-------------|
| `zest build` | Собрать сайт в `_site/` |
| `zest serve` | Запустить сервер разработки с живой перезагрузкой |
| `zest preview` | Предпросмотр собранного сайта |
| `zest init <name>` | Создать новый проект |
| `zest clean` | Очистить результат сборки |

### Справочник ZCSS

| Возможность | Синтаксис |
|---------|--------|
| Переменные (SCSS) | `$name: value;` |
| Переменные (F#) | `let name = value` |
| Математика | `let x = 0.25r * 4` |
| Цветовые функции | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| Оператор pipe | `value \|> fn(args)` → `fn(value, args)` |
| Сокращения единиц | `r`→`rem`, `p`→`%` |
| Сокращения свойств | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| Вложенность | Режим отступов или фигурных скобок |
| Миксины | `@mixin`, `@include` |
| Циклы | `@each`, `@for` |
| Условия | `@if`, `@else` |
| Встроенные модули | `@use "zest:utilities"`, `@use "zest:palette"` и др. |

### Макетные Движки

| Движок | Значение Конфигурации | Возможности |
|--------|-------------|----------|
| **ZestNjk** (по умолчанию) | `template_engine = "znjk"` | Фильтры, выражения, `{% if %}`, `{% for %}`, макросы, наследование шаблонов, фильтры Zest API (`pages_by_tag`, `recent`, `by_collection`, `search`, `where`) |
| **Нативная Замена** | `template_engine = "replace"` | Простая замена `{{ variable }}` |

### Справочник HTML DSL

```fsharp
// Элементы
h1 [ text "Заголовок" ]
p  [ text "Абзац" ]
a  [ href "https://example.com"; text "Ссылка" ]

// Атрибуты
div [ class' "container"; id "main" ] [ ... ]

// Сокращения CSS классов
divC "card" [ p [ text "Содержимое" ] ]   // <div class="card">
spanC "badge" [ text "Новое" ]            // <span class="badge">

// Генераторы списков
ul [ for item in items -> li [ text item ] ]

// Условия
if condition then
    p [ text "Да" ]
else
    p [ text "Нет" ]
```

### `_init.fsx` API

| Функция | Назначение |
|----------|---------|
| `addGlobal key value` | Внедрить пару ключ-значение в глобальные данные |
| `loadJson path` | Разобрать JSON файл |
| `loadToml path` | Разобрать TOML файл |
| `loadEnv key` | Прочитать переменную окружения |
| `console_log msg` | Отладочный вывод в stderr |
| `exec cmd args` | Выполнить команду оболочки |

## Философия Дизайна

**Zest — не универсальный генератор статических сайтов.** Это конкретный ответ на конкретные ограничения.

1. **F# как Шаблон** — Шаблон есть программа. Файлы `.zpage.fsx` — настоящий F# код, а не строки.
2. **ZCSS как Макетный Движок** — Не препроцессор CSS, а макетный движок, выдающий CSS.
3. **TOML как Контракт** — Никакого YAML. Никогда.
4. **JavaScript как Порядок** — Никакого Node.js, npm, сборщиков. JavaScript существует только для клиентской интерактивности.
5. **Ревностное Меньшинство** — Создано для тех, кто любит F#, ненавидит YAML и предпочитает простые инструменты.

## Лицензия

Apache 2.0 — см. [LICENSE](LICENSE).
