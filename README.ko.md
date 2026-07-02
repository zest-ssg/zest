<p align="center">
  <img src="zest.png" alt="Zest" width="128" height="128">
</p>

<h1 align="center">Zest SSG</h1>

<p align="center"><em>Zenith Efficient Static Toolkit</em></p>

<p align="center">
  <a href="LICENSE">License</a> · <a href="#빠른-시작">빠른 시작</a> · <a href="#문서">문서</a>
</p>

---

**Zest**는 F# + C# 하이브리드 정적 사이트 생성기로, 템플릿이 문자열이 아닌 실제 코드입니다. 템플릿 언어와 호스트 언어가 하나여야 한다는 철학 위에 구축되었습니다.

## 기능

- **코드로서의 템플릿** — `.zpage.fsx`는 빌드 시 `dotnet fsi`로 실행되는 실제 F# 스크립트입니다. 완전한 F#: 리스트 컴프리헨션, 패턴 매칭, 문자열 보간, 임의 계산.
- **`.zhtml` 경량 페이지** — 선택적 Nunjucks 템플릿 구문을 가진 순수 HTML 페이지. FSI 오버헤드 없음.
- **HTML DSL** — 선언적으로 HTML 구성: `render [ h1 []; p [] ]`.
- **Markdown** — frontmatter를 지원하는 표준 `.md` 파일.
- **ZCSS** — 중첩, F# 스타일 `let` 바인딩, 수학 표현식, 색상 함수, 믹스인을 갖춘 CSS 슈퍼셋 — 표준 CSS로 컴파일.
- **ZestNjk 템플릿** — 레이아웃을 위한 Nunjucks 호환 템플릿 엔진: 필터, 표현식, 매크로, `{% if %}`, `{% for %}`, 템플릿 상속, Zest API 통합. `.znjk` 확장자 사용.
- **`_init.fsx`** — 동적 데이터 주입, JSON/TOML 로드, 환경 변수 읽기를 위한 선택적 초기화 스크립트 (빌드 전 실행).
- **TOML 설정** — 제로 설정 기본값; `_config.toml` 및 `_data/*.toml`로 사용자 정의. YAML 없음.
- **라이브 리로드** — `zest serve`가 변경 사항을 감시하고 자동 재빌드.
- **배치 평가** — 여러 F# 페이지 스크립트를 단일 FSI 프로세스에서 평가하여 빠른 빌드.
- **증분 빌드** — 파일 변경 감지로 변경되지 않은 페이지와 애셋 건너뛰기.
- **크로스 플랫폼** — Windows x64, Linux x64/ARM64, macOS ARM64용 빌드.

## 빠른 시작

```bash
# 새 프로젝트 생성
zest init my-site

# 라이브 리로드로 개발
cd my-site && zest serve --port 8080

# 프로덕션 빌드
zest build

# 빌드된 사이트 미리보기
zest preview
```

## 예제: `.zpage.fsx` 페이지

```fsharp
// @title Hello World
// @layout default
// @description 나의 첫 Zest 페이지

let pageTitle = "F#에서 온 인사"
let items = ["F#"; "Zest"; "SSG"]

render [
    h1 [ text pageTitle ]
    p  [ text "이 페이지는 빌드 시 실제 F# 코드에 의해 생성됩니다." ]
    ul [ for i in items -> li [ text i ] ]
]
```

## 예제: ZCSS 스타일시트

```zcss
// F# 스타일 let 바인딩과 수학 표현식
let primary    = #3b82f6
let space1     = 0.25r
let space4     = space1 * 4     // 1rem
let primary-light = primary |> lighten(45%)

// 두 글자 속성 단축어
.tag
  color: $primary
  background-color: $primary-light
  padding-block: $space4
  border-radius: 9999px
```

컴파일 결과:

```css
.tag {
  color: #3b82f6;
  background-color: #adf4ff;
  padding-block: 1rem;
  border-radius: 9999px;
}
```

## 예제: `_init.fsx`

```fsharp
// _init.fsx — 매 빌드 전 실행
addGlobal "api_url" "https://api.example.com"

let team = loadJson "data/team.json"
addGlobal "team" team

let env = loadEnv "ZEST_ENV"
if env = "production" then
    addGlobal "analytics_id" "UA-XXXXX-Y"
```

## 프로젝트 구조

```
my-site/
├── _config.toml            # 사이트 설정（TOML）
├── _init.zpage.fsx         # 선택적 초기화 스크립트（빌드 전 실행）
├── _data/
│   └── site.toml           # 전역 데이터（스크립트/템플릿에서 접근 가능）
├── content/
│   ├── index.zpage.fsx     # 홈페이지（F# 스크립트 템플릿）
│   ├── about.md            # 소개 페이지（Markdown）
│   └── posts/
│       ├── hello-world.zpage.fsx
│       └── contact.zhtml   # 순수 HTML（FSI 오버헤드 없음）
├── _layouts/
│   ├── default.html        # 레이아웃（Nunjucks 또는 네이티브 치환）
│   └── post.html
├── assets/
│   └── css/
│       └── style.zcss      # ZCSS → 자동으로 style.css로 컴파일
└── _site/                  # 빌드 출력（자동 생성）
```

## 아키텍처

| 프로젝트 | 언어 | 책임 |
|---------|----------|----------------|
| **Zest.App** | C# | CLI 진입점, 명령 라우팅 |
| **Zest.Engine** | F# | 핵심 엔진: 빌드, HTML DSL, ScriptRunner, Markdown, ZCSS 컴파일러, ZestNjk 템플릿 엔진 |
| **Zest.Dsl** | F# | FSI 스크립트 평가용 사전 컴파일 DSL 헬퍼 |
| **Zest.Infra** | C# | 설정 로딩, 파일 감시, 개발 서버 |

## 소스에서 빌드

```bash
git clone https://github.com/zest-ssg/zest
cd zest
dotnet build Zest.sln

# 플랫폼용으로 게시
dotnet publish src/Zest.App/Zest.App.csproj -c Release -r win-x64 --self-contained false
# Linux:  -r linux-x64
# macOS:  -r osx-arm64
```

## 문서

### 파일 유형

| 확장자 | 용도 | 처리 |
|-----------|---------|------------|
| `.zpage.fsx` | F# 스크립트 템플릿（F# + Markdown + HTML DSL） | `dotnet fsi`로 컴파일 |
| `.znjk` | Zest Nunjucks 템플릿（Nunjucks 호환 구문 + Zest API 통합） | ZestNjkEngine으로 렌더링 — 필터, 표현식, `{% if %}`, `{% for %}`, 매크로, 템플릿 상속 지원 |
| `.zcss` | ZCSS 스타일시트（CSS 슈퍼셋） | `.css`로 컴파일 |
| `.md` | 표준 Markdown | HTML로 렌더링 |
| `.toml` | 설정 및 데이터（YAML 없음） | 빌드 시 파싱 |

### 명령어

| 명령어 | 설명 |
|---------|-------------|
| `zest build` | 사이트를 `_site/`에 빌드 |
| `zest serve` | 개발 서버 시작（라이브 리로드） |
| `zest preview` | 빌드된 사이트 미리보기 |
| `zest init <name>` | 새 프로젝트 생성 |
| `zest clean` | 빌드 출력 정리 |

### ZCSS 레퍼런스

| 기능 | 구문 |
|---------|--------|
| 변수（SCSS） | `$name: value;` |
| 변수（F#） | `let name = value` |
| 수학 | `let x = 0.25r * 4` |
| 색상 함수 | `lighten(#hex, %)`, `darken(#hex, %)`, `mix(a, b, %)` |
| 파이프 연산자 | `value \|> fn(args)` → `fn(value, args)` |
| 단위 단축어 | `r`→`rem`, `p`→`%` |
| 속성 단축어 | `py`→`padding-block`, `mx`→`margin-inline`, `bgc`→`background-color` |
| 중첩 | 인덴트 모드 또는 브레이스 모드 |
| 믹스인 | `@mixin`, `@include` |
| 반복문 | `@each`, `@for` |
| 조건문 | `@if`, `@else` |
| 내장 모듈 | `@use "zest:utilities"`, `@use "zest:palette"` 등 |

### 레이아웃 엔진

| 엔진 | 설정 값 | 기능 |
|--------|-------------|----------|
| **ZestNjk**（기본） | `template_engine = "znjk"` | 필터, 표현식, `{% if %}`, `{% for %}`, 매크로, 템플릿 상속, Zest API 필터（`pages_by_tag`, `recent`, `by_collection`, `search`, `where`） |
| **네이티브 치환** | `template_engine = "replace"` | 간단한 `{{ variable }}` 치환 |

### HTML DSL 레퍼런스

```fsharp
// 요소
h1 [ text "제목" ]
p  [ text "단락" ]
a  [ href "https://example.com"; text "링크" ]

// 속성
div [ class' "container"; id "main" ] [ ... ]

// CSS 클래스 단축어
divC "card" [ p [ text "내용" ] ]   // <div class="card">
spanC "badge" [ text "새글" ]       // <span class="badge">

// 리스트 컴프리헨션
ul [ for item in items -> li [ text item ] ]

// 조건문
if condition then
    p [ text "예" ]
else
    p [ text "아니오" ]
```

### `_init.fsx` API

| 함수 | 용도 |
|----------|---------|
| `addGlobal key value` | 키-값을 전역 데이터에 주입 |
| `loadJson path` | JSON 파일 파싱 |
| `loadToml path` | TOML 파일 파싱 |
| `loadEnv key` | 환경 변수 읽기 |
| `console_log msg` | stderr로 디버그 출력 |
| `exec cmd args` | 셸 명령 실행 |

## 설계 철학

**Zest는 범용 정적 사이트 생성기가 아닙니다.** 특정 제약에 대한 특정 해답입니다.

1. **F#이 템플릿이다** — 템플릿이 프로그램입니다. `.zpage.fsx` 파일은 문자열이 아닌 실제 F# 코드입니다.
2. **ZCSS가 레이아웃 엔진이다** — CSS 프리프로세서가 아니라 CSS를 출력하는 레이아웃 엔진입니다.
3. **TOML이 계약이다** — YAML은 절대 사용하지 않습니다.
4. **JavaScript는 질서를 따른다** — Node.js, npm, 번들러 없음. JavaScript는 클라이언트 측 상호작용만을 위해 존재합니다.
5. **열정적인 소수** — F#을 사랑하고, YAML을 싫어하며, 단순한 도구를 선호하는 사람들을 위해.

## 라이선스

Apache 2.0 — [LICENSE](LICENSE) 참조.
