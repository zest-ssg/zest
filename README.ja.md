<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#クイックスタート">クイックスタート</a> · <a href="#ドキュメント">ドキュメント</a>
</p>

---

**Zest** は F# + C# ハイブリッド静的サイトジェネレーターです。テンプレートは本物のコードであり、文字列ではありません。テンプレート言語とホスト言語は一つであるべきという哲学に基づいています。

## 特徴

- **テンプレートとしてのコード** — `.zpage.fsx` はビルド時に `dotnet fsi` で実行される本物の F# スクリプト。完全な F#: リスト内包表記、パターンマッチング、文字列補間、任意の計算。
- **`.zhtml` 軽量ページ** — オプションの Nunjucks テンプレート構文を持つ純粋な HTML ページ。FSI のオーバーヘッドなし。
- **HTML DSL** — 宣言的に HTML を構成: `render [ h1 []; p [] ]`。
- **Markdown** — frontmatter をサポートする標準 `.md` ファイル。
- **ZCSS** — ネスト、F# スタイルの `let` バインディング、数式、カラー関数、ミックスインを備えた CSS スーパーセット — 標準 CSS にコンパイル。
- **ZestNjk テンプレート** — レイアウト用の Nunjucks 互換テンプレートエンジン: フィルター、式、マクロ、`{% if %}`、`{% for %}`、テンプレート継承、Zest API 統合。`.znjk` 拡張子を使用。
- **`_init.fsx`** — 動的データの注入、JSON/TOML の読み込み、環境変数の読み取りのためのオプションの初期化スクリプト（ビルド前に実行）。
- **TOML 設定** — ゼロ設定のデフォルト; `_config.toml` と `_data/*.toml` でカスタマイズ。YAML は不使用。
- **ライブリロード** — `zest serve` が変更を監視し、自動再ビルド。
- **バッチ評価** — 複数の F# ページスクリプトを単一の FSI プロセスで評価し、高速ビルド。
- **インクリメンタルビルド** — ファイル変更検出が未変更のページとアセットをスキップ。
- **クロスプラットフォーム** — Windows x64、Linux x64/ARM64、macOS ARM64 向けビルド。

## クイックスタート

```bash
# 新規プロジェクトの作成
zest init my-site

# ライブリロードで開発
cd my-site && zest serve --port 8080

# 本番用ビルド
zest build

# ビルドしたサイトのプレビュー
zest preview
```

## 例: `.zpage.fsx` ページ

```fsharp
// @title Hello World
// @layout default
// @description 初めての Zest ページ

let pageTitle = "F# からのご挨拶"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "このページはビルド時に本物の F# コードによって生成されます。" ]
    ul [ for i in items -> li [ text i ] ]
]
```

## 例: ZCSS スタイルシート

```zcss
// F# スタイルの let バインディングと数式
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// 2文字プロパティショートハンド
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

コンパイル結果:

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## 例: `_init.fsx`

```fsharp
// _init.fsx — 各ビルド前に実行
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## プロジェクト構造

```
my-site/
├── _config.toml            # サイト設定（TOML）
├── _init.zpage.fsx         # オプションの初期化スクリプト（ビルド前に実行）
├── _data/
│   └── site.toml           # グローバルデータ（スクリプト/テンプレートからアクセス可能）
├── content/
│   ├── index.zpage.fsx     # ホームページ（F# スクリプトテンプレート）
│   ├── about.md            # About ページ（Markdown）
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # 純粋 HTML（FSI オーバーヘッドなし）
├── _layouts/
│   ├── default.html        # レイアウト（Nunjucks またはネイティブ置換）
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → 自動的に style.css にコンパイル
└── _site/                  # ビルド出力（自動生成）
```

## アーキテクチャ

| プロジェクト | 言語 | 責務 |
|---------|----------|----------------|
| **Zest.App** | C# | CLI エントリポイント、コマンドルーティング |
| **Zest.Engine** | F# | コアエンジン: ビルド、HTML DSL、ScriptRunner、Markdown、ZCSS コンパイラ、ZestNjk テンプレートエンジン |
| **Zest.Dsl** | F# | FSI スクリプト評価用のプリコンパイル DSL ヘルパー |
| **Zest.Infra** | C# | 設定読み込み、ファイル監視、開発サーバー |

## ソースからビルド

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# プラットフォーム向けにパブリッシュ
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## ドキュメント

### ファイルタイプ

| 拡張子 | 目的 | 処理 |
|-----------|---------|------------|
| `.zpage.fsx` | F# スクリプトテンプレート（F# + Markdown + HTML DSL） | `dotnet fsi` でコンパイル |
| `.znjk` | Zest Nunjucks テンプレート（Nunjucks 互換構文 + Zest API 統合） | ZestNjkEngine でレンダリング — フィルター、式、`{% if %}`、`{% for %}`、マクロ、テンプレート継承に対応 |
| `.zcss` | ZCSS スタイルシート（CSS スーパーセット） | `.css` にコンパイル |
| `.md` | 標準 Markdown | HTML にレンダリング |
| `.toml` | 設定とデータ（YAML 不使用） | ビルド時に解析 |

### コマンド

| コマンド | 説明 |
|---------|-------------|
| `zest build` | サイトを `_site/` にビルド |
| `zest serve` | 開発サーバーを起動（ライブリロード） |
| `zest preview` | ビルドしたサイトをプレビュー |
| `zest init <name>` | 新規プロジェクトを作成 |
| `zest clean` | ビルド出力をクリーン |

### ZCSS リファレンス

| 機能 | 構文 |
|---------|--------|
| 変数（SCSS） | `$name: value;` |
| 変数（F#） | `let name = value` |
| 数学 | `let x = 0.25r * 4` |
| カラー関数 | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| パイプ演算子 | `value \|> fn(args)` → `fn(value, args)` |
| 単位ショートハンド | `r`→`rem`, `p`→`%` |
| プロパティショートハンド | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| ネスト | インデントモードまたはブレースモード |
| ミックスイン | `@mixin`, `@include` |
| ループ | `@each`, `@for` |
| 条件分岐 | `@if`, `@else` |
| 組み込みモジュール | `@use "zest:utilities"`, `@use "zest:palette"` など |

### レイアウトエンジン

| エンジン | 設定値 | 機能 |
|--------|-------------|----------|
| **ZestNjk**（デフォルト） | `template_engine = "znjk"` | フィルター、式、`{% if %}`、`{% for %}`、マクロ、テンプレート継承、Zest API フィルター（`pages_by_tag`、`recent`、`by_collection`、`search`、`where`） |
| **ネイティブ置換** | `template_engine = "replace"` | シンプルな `{{ variable }}` 置換 |

### HTML DSL リファレンス

```fsharp
// 要素
h1 [ text "タイトル" ]
p  [ text "段落" ]
a  [ href "https://example.com"; text "リンク" ]

// 属性
div [ class' "container"; id "main" ] [ ... ]

// CSS クラスショートカット
divC "card" [ p [ text "コンテンツ" ] ]   // <div class="card">
spanC "badge" [ text "新着" ]             // <span class="badge">

// リスト内包表記
ul [ for item in items -> li [ text item ] ]

// 条件分岐
if condition then
    p [ text "はい" ]
else
    p [ text "いいえ" ]
```

### `_init.fsx` API

| 関数 | 目的 |
|----------|---------|
| `addGlobal key value` | キーと値をグローバルデータに注入 |
| `loadJson path` | JSON ファイルを解析 |
| `loadToml path` | TOML ファイルを解析 |
| `loadEnv key` | 環境変数を読み取り |
| `console_log msg` | デバッグ出力（stderr） |
| `exec cmd args` | シェルコマンドを実行 |

## 設計思想

**Zest は汎用静的サイトジェネレーターではありません。** 特定の制約に対する特定の解決策です。

1. **F# がテンプレート** — テンプレートこそがプログラム。`.zpage.fsx` ファイルは本物の F# コードであり、文字列ではありません。
2. **ZCSS がレイアウトエンジン** — CSS プリプロセッサではなく、CSS を出力するレイアウトエンジン。
3. **TOML が契約** — YAML は絶対に使わない。
4. **JavaScript は秩序に従う** — Node.js、npm、バンドラーなし。JavaScript はクライアントサイドのインタラクティビティのみに存在。
5. **熱狂的な少数派** — F# を愛し、YAML を嫌い、シンプルなツールを好む人々のために。

## ライセンス

Apache 2.0 — [LICENSE](LICENSE) を参照。
