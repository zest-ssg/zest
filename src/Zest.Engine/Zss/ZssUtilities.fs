namespace Zest.Engine.Zss

open System
open System.Collections.Generic
open System.Text

// ============================================================
// ZSS Utilities — Built-in utility classes and functions
// ============================================================

module Utilities =

    /// Generate built-in utility classes as ZSS source text.
    /// These can be imported via `@use "zest:utilities"`.
    let builtinUtilities = """
// ── Zest Built-in Utilities ──────────────────────────────

// Display
.d-none    { d: none }
.d-block  { d: block }
.d-inline { d: inline }
.d-inline-block { d: inline-block }
.d-flex   { d: flex }
.d-inline-flex { d: inline-flex }
.d-grid   { d: grid }
.d-table  { d: table }

// Flexbox
.flex-row    { flex-direction: row }
.flex-col   { flex-direction: column }
.flex-wrap  { flex-wrap: wrap }
.flex-nowrap { flex-wrap: nowrap }
.flex-1     { flex: 1 1 0% }
.flex-auto  { flex: 1 1 auto }
.flex-none  { flex: none }
.flex-grow  { flex-grow: 1 }
.flex-grow-0 { flex-grow: 0 }
.flex-shrink { flex-shrink: 1 }
.flex-shrink-0 { flex-shrink: 0 }

// Alignment
.items-start    { ai: flex-start }
.items-end      { ai: flex-end }
.items-center   { ai: center }
.items-baseline { ai: baseline }
.items-stretch  { ai: stretch }
.justify-start   { jc: flex-start }
.justify-end     { jc: flex-end }
.justify-center  { jc: center }
.justify-between { jc: space-between }
.justify-around  { jc: space-around }
.justify-evenly  { jc: space-evenly }
.self-start  { as: flex-start }
.self-end    { as: flex-end }
.self-center { as: center }
.self-stretch { as: stretch }

// Gap
.gap-0   { gap: 0 }
.gap-1   { gap: 0.25rem }
.gap-2   { gap: 0.5rem }
.gap-3   { gap: 1rem }
.gap-4   { gap: 1.5rem }
.gap-5   { gap: 2rem }
.gap-6   { gap: 3rem }

// Margin
.m-0  { m: 0 }
.m-1  { m: 0.25rem }
.m-2  { m: 0.5rem }
.m-3  { m: 1rem }
.m-4  { m: 1.5rem }
.m-5  { m: 2rem }
.m-6  { m: 3rem }
.m-auto { m: auto }
.mx-0 { mx: 0 }
.mx-1 { mx: 0.25rem }
.mx-2 { mx: 0.5rem }
.mx-3 { mx: 1rem }
.mx-4 { mx: 1.5rem }
.mx-5 { mx: 2rem }
.mx-auto { mx: auto }
.my-0 { my: 0 }
.my-1 { my: 0.25rem }
.my-2 { my: 0.5rem }
.my-3 { my: 1rem }
.my-4 { my: 1.5rem }
.my-5 { my: 2rem }
.mt-0 { mt: 0 }
.mt-1 { mt: 0.25rem }
.mt-2 { mt: 0.5rem }
.mt-3 { mt: 1rem }
.mt-4 { mt: 1.5rem }
.mt-5 { mt: 2rem }
.mb-0 { mb: 0 }
.mb-1 { mb: 0.25rem }
.mb-2 { mb: 0.5rem }
.mb-3 { mb: 1rem }
.mb-4 { mb: 1.5rem }
.mb-5 { mb: 2rem }
.ml-0 { ml: 0 }
.ml-1 { ml: 0.25rem }
.ml-2 { ml: 0.5rem }
.ml-3 { ml: 1rem }
.ml-4 { ml: 1.5rem }
.ml-5 { ml: 2rem }
.ml-auto { ml: auto }
.mr-0 { mr: 0 }
.mr-1 { mr: 0.25rem }
.mr-2 { mr: 0.5rem }
.mr-3 { mr: 1rem }
.mr-4 { mr: 1.5rem }
.mr-5 { mr: 2rem }

// Padding
.p-0  { p: 0 }
.p-1  { p: 0.25rem }
.p-2  { p: 0.5rem }
.p-3  { p: 1rem }
.p-4  { p: 1.5rem }
.p-5  { p: 2rem }
.p-6  { p: 3rem }
.px-0 { px: 0 }
.px-1 { px: 0.25rem }
.px-2 { px: 0.5rem }
.px-3 { px: 1rem }
.px-4 { px: 1.5rem }
.px-5 { px: 2rem }
.py-0 { py: 0 }
.py-1 { py: 0.25rem }
.py-2 { py: 0.5rem }
.py-3 { py: 1rem }
.py-4 { py: 1.5rem }
.py-5 { py: 2rem }
.pt-0 { pt: 0 }
.pt-1 { pt: 0.25rem }
.pt-2 { pt: 0.5rem }
.pt-3 { pt: 1rem }
.pt-4 { pt: 1.5rem }
.pt-5 { pt: 2rem }
.pb-0 { pb: 0 }
.pb-1 { pb: 0.25rem }
.pb-2 { pb: 0.5rem }
.pb-3 { pb: 1rem }
.pb-4 { pb: 1.5rem }
.pb-5 { pb: 2rem }
.pl-0 { pl: 0 }
.pl-1 { pl: 0.25rem }
.pl-2 { pl: 0.5rem }
.pl-3 { pl: 1rem }
.pl-4 { pl: 1.5rem }
.pl-5 { pl: 2rem }
.pr-0 { pr: 0 }
.pr-1 { pr: 0.25rem }
.pr-2 { pr: 0.5rem }
.pr-3 { pr: 1rem }
.pr-4 { pr: 1.5rem }
.pr-5 { pr: 2rem }

// Width / Height
.w-full { w: 100% }
.w-auto { w: auto }
.w-screen { w: 100vw }
.h-full { h: 100% }
.h-auto { h: auto }
.h-screen { h: 100vh }
.min-w-0 { mnw: 0 }
.min-h-0 { mnh: 0 }
.max-w-full { mw: 100% }
.max-h-full { mh: 100% }

// Text
.text-left { ta: left }
.text-center { ta: center }
.text-right { ta: right }
.text-justify { ta: justify }
.text-upper { tt: uppercase }
.text-lower { tt: lowercase }
.text-capitalize { tt: capitalize }
.text-none { td: none }
.text-underline { td: underline }
.text-line-through { td: line-through }
.text-bold { fw: 700 }
.text-semibold { fw: 600 }
.text-medium { fw: 500 }
.text-normal { fw: 400 }
.text-light { fw: 300 }
.text-italic { font-style: italic }
.text-not-italic { font-style: normal }

// Font sizes
.text-xs { fs: 0.75rem }
.text-sm { fs: 0.875rem }
.text-base { fs: 1rem }
.text-lg { fs: 1.125rem }
.text-xl { fs: 1.25rem }
.text-2xl { fs: 1.5rem }
.text-3xl { fs: 1.875rem }
.text-4xl { fs: 2.25rem }
.text-5xl { fs: 3rem }

// Colors (can be overridden with $primary, etc.)
.text-primary { c: $primary }
.text-secondary { c: $secondary }
.text-muted { c: $text-muted }
.text-white { c: #fff }
.text-black { c: #000 }

// Background
.bg-primary { bgc: $primary }
.bg-secondary { bgc: $secondary }
.bg-transparent { bgc: transparent }
.bg-white { bgc: #fff }
.bg-black { bgc: #000 }

// Border
.border { bd: 1px solid $border }
.border-0 { bd: 0 }
.border-t { bdt: 1px solid $border }
.border-b { bdb: 1px solid $border }
.border-l { bdl: 1px solid $border }
.border-r { bdr: 1px solid $border }
.rounded { bdr: 0.25rem }
.rounded-sm { bdr: 0.125rem }
.rounded-md { bdr: 0.375rem }
.rounded-lg { bdr: 0.5rem }
.rounded-xl { bdr: 0.75rem }
.rounded-2xl { bdr: 1rem }
.rounded-full { bdr: 9999px }
.rounded-none { bdr: 0 }

// Overflow
.overflow-auto { ov: auto }
.overflow-hidden { ov: hidden }
.overflow-visible { ov: visible }
.overflow-scroll { ov: scroll }
.overflow-x-auto { ovx: auto }
.overflow-x-hidden { ovx: hidden }
.overflow-y-auto { ovy: auto }
.overflow-y-hidden { ovy: hidden }

// Position
.relative { pos: relative }
.absolute { pos: absolute }
.fixed { pos: fixed }
.sticky { pos: sticky }
.static { pos: static }

// Top/Right/Bottom/Left
.top-0 { t: 0 }
.right-0 { r: 0 }
.bottom-0 { b: 0 }
.left-0 { l: 0 }
.top-auto { t: auto }
.right-auto { r: auto }
.bottom-auto { b: auto }
.left-auto { l: auto }

// Z-index
.z-0 { z: 0 }
.z-10 { z: 10 }
.z-20 { z: 20 }
.z-30 { z: 30 }
.z-40 { z: 40 }
.z-50 { z: 50 }
.z-auto { z: auto }

// Opacity
.opacity-0 { o: 0 }
.opacity-25 { o: 0.25 }
.opacity-50 { o: 0.5 }
.opacity-75 { o: 0.75 }
.opacity-100 { o: 1 }

// Cursor
.cursor-auto { cur: auto }
.cursor-default { cur: default }
.cursor-pointer { cur: pointer }
.cursor-wait { cur: wait }
.cursor-text { cur: text }
.cursor-move { cur: move }
.cursor-not-allowed { cur: not-allowed }

// Shadow
.shadow-sm { bxsh: 0 1px 2px 0 rgba(0,0,0,0.05) }
.shadow { bxsh: 0 1px 3px 0 rgba(0,0,0,0.1), 0 1px 2px 0 rgba(0,0,0,0.06) }
.shadow-md { bxsh: 0 4px 6px -1px rgba(0,0,0,0.1), 0 2px 4px -1px rgba(0,0,0,0.06) }
.shadow-lg { bxsh: 0 10px 15px -3px rgba(0,0,0,0.1), 0 4px 6px -2px rgba(0,0,0,0.05) }
.shadow-xl { bxsh: 0 20px 25px -5px rgba(0,0,0,0.1), 0 10px 10px -5px rgba(0,0,0,0.04) }
.shadow-none { bxsh: none }

// Transition
.transition { tr: all 0.15s ease }
.transition-none { tr: none }
.transition-colors { tr: color, bgc, bd, bdc, bxsh 0.15s ease }
.transition-opacity { tr: opacity 0.15s ease }
.transition-transform { tr: transform 0.15s ease }

// Transform
.transform { trf: none }
.translate-x-0 { trf: translateX(0) }
.translate-y-0 { trf: translateY(0) }
.rotate-0 { trf: rotate(0) }
.scale-100 { trf: scale(1) }
.scale-0 { trf: scale(0) }

// Grid
.grid-cols-1 { gtc: repeat(1, 1fr) }
.grid-cols-2 { gtc: repeat(2, 1fr) }
.grid-cols-3 { gtc: repeat(3, 1fr) }
.grid-cols-4 { gtc: repeat(4, 1fr) }
.grid-cols-5 { gtc: repeat(5, 1fr) }
.grid-cols-6 { gtc: repeat(6, 1fr) }
.grid-cols-12 { gtc: repeat(12, 1fr) }

// Misc
.hidden { d: none }
.visible { vis: visible }
.invisible { vis: hidden }
.pointer-events-none { pe: none }
.pointer-events-auto { pe: auto }
.select-none { us: none }
.select-text { us: text }
.select-all { us: all }
.box-border { bxz: border-box }
.box-content { bxz: content-box }
"""

    /// Common CSS reset (normalize-like)
    let cssReset = """
*, *::before, *::after {
  bxz: border-box
  m: 0
  p: 0
}
"""

    /// Predefined color palettes
    let colorPalettes = """
// ── Color Palettes ────────────────────────────────────────
$palette-blue:   #3b82f6
$palette-indigo:  #6366f1
$palette-purple:  #8b5cf6
$palette-pink:    #ec4899
$palette-red:     #ef4444
$palette-orange:  #f97316
$palette-yellow:  #eab308
$palette-green:   #22c55e
$palette-teal:    #14b8a6
$palette-cyan:    #06b6d4
$palette-gray:    #6b7280
$palette-slate:   #64748b

// ── Semantic Colors (with !default) ──────────────────────
$primary:    #3b82f6 !default
$secondary:  #6b7280 !default
$success:    #22c55e !default
$warning:    #f97316 !default
$danger:     #ef4444 !default
$info:       #06b6d4 !default
$light:      #f3f4f6 !default
$dark:       #1f2937 !default
$text:       #1f2937 !default
$text-muted: #6b7280 !default
$border:     #e5e7eb !default
$bg:         #ffffff !default
$bg-alt:     #f9fafb !default
"""

    /// Animation utilities
    let animationUtilities = """
// ── Animation Utilities ──────────────────────────────────

// Keyframes
@keyframes fadeIn {
  from { o: 0 }
  to   { o: 1 }
}
@keyframes fadeOut {
  from { o: 1 }
  to   { o: 0 }
}
@keyframes slideInUp {
  from { trf: translateY(100%) }
  to   { trf: translateY(0) }
}
@keyframes slideInDown {
  from { trf: translateY(-100%) }
  to   { trf: translateY(0) }
}
@keyframes slideInLeft {
  from { trf: translateX(-100%) }
  to   { trf: translateX(0) }
}
@keyframes slideInRight {
  from { trf: translateX(100%) }
  to   { trf: translateX(0) }
}
@keyframes bounce {
  0%, 20%, 53%, 80%, 100% { trf: translateY(0) }
  40%, 43% { trf: translateY(-30px) }
  70% { trf: translateY(-15px) }
  90% { trf: translateY(-4px) }
}
@keyframes pulse {
  0%, 100% { o: 1 }
  50% { o: 0.5 }
}
@keyframes spin {
  from { trf: rotate(0deg) }
  to   { trf: rotate(360deg) }
}
@keyframes ping {
  0% { trf: scale(1); o: 1 }
  75%, 100% { trf: scale(2); o: 0 }
}
@keyframes wiggle {
  0%, 100% { trf: rotate(-3deg) }
  50% { trf: rotate(3deg) }
}

// Animation classes
.animate-fade-in     { anim: fadeIn 0.3s ease-in }
.animate-fade-out     { anim: fadeOut 0.3s ease-out }
.animate-slide-up     { anim: slideInUp 0.3s ease-out }
.animate-slide-down   { anim: slideInDown 0.3s ease-out }
.animate-slide-left   { anim: slideInLeft 0.3s ease-out }
.animate-slide-right  { anim: slideInRight 0.3s ease-out }
.animate-bounce       { anim: bounce 1s ease-in-out infinite }
.animate-pulse        { anim: pulse 2s ease-in-out infinite }
.animate-spin         { anim: spin 1s linear infinite }
.animate-ping         { anim: ping 1s cubic-bezier(0,0,0.2,1) infinite }
.animate-wiggle       { anim: wiggle 0.5s ease-in-out infinite }
.animate-none         { anim: none }

// Animation timing
.ease-in    { anim-timing-function: ease-in }
.ease-out   { anim-timing-function: ease-out }
.ease-in-out { anim-timing-function: ease-in-out }
.ease-linear { anim-timing-function: linear }
.duration-75   { anim-duration: 75ms }
.duration-100  { anim-duration: 100ms }
.duration-150  { anim-duration: 150ms }
.duration-300  { anim-duration: 300ms }
.duration-500  { anim-duration: 500ms }
.duration-700  { anim-duration: 700ms }
.duration-1000 { anim-duration: 1000ms }
.delay-75   { anim-delay: 75ms }
.delay-100  { anim-delay: 100ms }
.delay-150  { anim-delay: 150ms }
.delay-300  { anim-delay: 300ms }
.delay-500  { anim-delay: 500ms }
.delay-700  { anim-delay: 700ms }
.delay-1000 { anim-delay: 1000ms }
"""

    /// Gradient utilities
    let gradientUtilities = """
// ── Gradient Utilities ───────────────────────────────────
.bg-gradient-to-r   { bgi: linear-gradient(to right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-l   { bgi: linear-gradient(to left, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-t   { bgi: linear-gradient(to top, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-b   { bgi: linear-gradient(to bottom, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-tr  { bgi: linear-gradient(to top right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-tl  { bgi: linear-gradient(to top left, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-br  { bgi: linear-gradient(to bottom right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-bl  { bgi: linear-gradient(to bottom left, var(--gradient-stops, $primary, $secondary)) }

// Solid gradient presets
.bg-gradient-primary   { bgi: linear-gradient(135deg, $primary, lighten($primary, 10%)) }
.bg-gradient-success   { bgi: linear-gradient(135deg, $success, lighten($success, 10%)) }
.bg-gradient-danger    { bgi: linear-gradient(135deg, $danger, lighten($danger, 10%)) }
.bg-gradient-warning   { bgi: linear-gradient(135deg, $warning, lighten($warning, 10%)) }
.bg-gradient-info      { bgi: linear-gradient(135deg, $info, lighten($info, 10%)) }
.bg-gradient-rainbow   { bgi: linear-gradient(135deg, #ef4444, #f97316, #eab308, #22c55e, #06b6d4, #8b5cf6) }
"""

    /// Filter and transform utilities
    let filterUtilities = """
// ── Filter Utilities ──────────────────────────────────────
.filter-none       { filter: none }
.filter-blur-sm    { filter: blur(4px) }
.filter-blur       { filter: blur(8px) }
.filter-blur-md    { filter: blur(12px) }
.filter-blur-lg    { filter: blur(16px) }
.filter-blur-xl    { filter: blur(24px) }
.filter-grayscale  { filter: grayscale(100%) }
.filter-grayscale-0 { filter: grayscale(0%) }
.filter-invert     { filter: invert(100%) }
.filter-sepia      { filter: sepia(100%) }
.filter-saturate   { filter: saturate(2) }
.filter-contrast   { filter: contrast(1.5) }
.filter-brightness { filter: brightness(1.5) }
.filter-hue-rotate { filter: hue-rotate(90deg) }
.filter-drop-shadow { filter: drop-shadow(0 4px 6px rgba(0,0,0,0.1)) }

// Backdrop filter
.backdrop-blur-sm   { backdrop-filter: blur(4px) }
.backdrop-blur      { backdrop-filter: blur(8px) }
.backdrop-blur-md   { backdrop-filter: blur(12px) }
.backdrop-blur-lg   { backdrop-filter: blur(16px) }
.backdrop-grayscale { backdrop-filter: grayscale(100%) }

// Transform
.transform          { trf: none }
.transform-gpu      { trf: translateZ(0) }
.translate-x-0      { trf: translateX(0) }
.translate-x-full   { trf: translateX(100%) }
.translate-x-1/2   { trf: translateX(50%) }
.translate-x-n1/2  { trf: translateX(-50%) }
.translate-y-0      { trf: translateY(0) }
.translate-y-full   { trf: translateY(100%) }
.translate-y-1/2   { trf: translateY(50%) }
.translate-y-n1/2  { trf: translateY(-50%) }
.rotate-0    { trf: rotate(0deg) }
.rotate-45   { trf: rotate(45deg) }
.rotate-90   { trf: rotate(90deg) }
.rotate-180  { trf: rotate(180deg) }
.rotate-270  { trf: rotate(270deg) }
.scale-0     { trf: scale(0) }
.scale-50    { trf: scale(0.5) }
.scale-75    { trf: scale(0.75) }
.scale-90    { trf: scale(0.9) }
.scale-100   { trf: scale(1) }
.scale-110   { trf: scale(1.1) }
.scale-125   { trf: scale(1.25) }
.scale-150   { trf: scale(1.5) }
.skew-x-0    { trf: skewX(0deg) }
.skew-x-12   { trf: skewX(12deg) }
.skew-y-0    { trf: skewY(0deg) }
.skew-y-12   { trf: skewY(12deg) }
"""

    /// Layout and container utilities
    let layoutUtilities = """
// ── Layout Utilities ──────────────────────────────────────
.container { mw: 1280px; mx: auto; px: 1rem }
.container-sm { mw: 640px; mx: auto; px: 1rem }
.container-md { mw: 768px; mx: auto; px: 1rem }
.container-lg { mw: 1024px; mx: auto; px: 1rem }
.container-xl { mw: 1280px; mx: auto; px: 1rem }
.container-2xl { mw: 1536px; mx: auto; px: 1rem }
.container-fluid { w: 100%; px: 1rem }

// Aspect ratio
.aspect-square { asp: 1 / 1 }
.aspect-video { asp: 16 / 9 }
.aspect-photo { asp: 4 / 3 }
.aspect-wide { asp: 21 / 9 }
.aspect-auto { asp: auto }

// Object fit
.object-contain { object-fit: contain }
.object-cover { object-fit: cover }
.object-fill { object-fit: fill }
.object-none { object-fit: none }
.object-scale-down { object-fit: scale-down }

// Columns
.columns-1 { column-count: 1 }
.columns-2 { column-count: 2 }
.columns-3 { column-count: 3 }
.columns-4 { column-count: 4 }
.gap-1 { column-gap: 0.25rem }
.gap-2 { column-gap: 0.5rem }
.gap-3 { column-gap: 1rem }
"""

    /// Process @use directives — resolve built-in modules
    let resolveUse (path: string) : string option =
        match path with
        | "zest:utilities" | "utilities" -> Some builtinUtilities
        | "zest:reset" | "reset" -> Some cssReset
        | "zest:palette" | "palette" | "zest:colors" | "colors" -> Some colorPalettes
        | "zest:animations" | "animations" -> Some animationUtilities
        | "zest:gradients" | "gradients" -> Some gradientUtilities
        | "zest:filters" | "filters" -> Some filterUtilities
        | "zest:layout" | "layout" -> Some layoutUtilities
        | "zest:all" | "all" ->
            Some (builtinUtilities + animationUtilities + gradientUtilities + filterUtilities + layoutUtilities)
        | _ -> None
