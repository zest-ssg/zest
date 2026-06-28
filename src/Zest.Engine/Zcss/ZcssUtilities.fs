namespace Zest.Engine.Zcss

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
.display-none    { display: none }
.display-block  { display: block }
.display-inline { display: inline }
.display-inline-block { display: inline-block }
.display-flex   { display: flex }
.display-inline-flex { display: inline-flex }
.display-grid   { display: grid }
.display-table  { display: table }

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
.items-start    { align-items: flex-start }
.items-end      { align-items: flex-end }
.items-center   { align-items: center }
.items-baseline { align-items: baseline }
.items-stretch  { align-items: stretch }
.justify-start   { justify-content: flex-start }
.justify-end     { justify-content: flex-end }
.justify-center  { justify-content: center }
.justify-between { justify-content: space-between }
.justify-around  { justify-content: space-around }
.justify-evenly  { justify-content: space-evenly }
.self-start  { align-self: flex-start }
.self-end    { align-self: flex-end }
.self-center { align-self: center }
.self-stretch { align-self: stretch }

// Gap
.gap-0   { gap: 0 }
.gap-1   { gap: 0.25rem }
.gap-2   { gap: 0.5rem }
.gap-3   { gap: 1rem }
.gap-4   { gap: 1.5rem }
.gap-5   { gap: 2rem }
.gap-6   { gap: 3rem }

// Margin
.margin-0  { margin: 0 }
.margin-1  { margin: 0.25rem }
.margin-2  { margin: 0.5rem }
.margin-3  { margin: 1rem }
.margin-4  { margin: 1.5rem }
.margin-5  { margin: 2rem }
.margin-6  { margin: 3rem }
.margin-auto { margin: auto }
.margin-inline-0 { margin-inline: 0 }
.margin-inline-1 { margin-inline: 0.25rem }
.margin-inline-2 { margin-inline: 0.5rem }
.margin-inline-3 { margin-inline: 1rem }
.margin-inline-4 { margin-inline: 1.5rem }
.margin-inline-5 { margin-inline: 2rem }
.margin-inline-auto { margin-inline: auto }
.margin-block-0 { margin-block: 0 }
.margin-block-1 { margin-block: 0.25rem }
.margin-block-2 { margin-block: 0.5rem }
.margin-block-3 { margin-block: 1rem }
.margin-block-4 { margin-block: 1.5rem }
.margin-block-5 { margin-block: 2rem }
.margin-top-0 { margin-top: 0 }
.margin-top-1 { margin-top: 0.25rem }
.margin-top-2 { margin-top: 0.5rem }
.margin-top-3 { margin-top: 1rem }
.margin-top-4 { margin-top: 1.5rem }
.margin-top-5 { margin-top: 2rem }
.margin-bottom-0 { margin-bottom: 0 }
.margin-bottom-1 { margin-bottom: 0.25rem }
.margin-bottom-2 { margin-bottom: 0.5rem }
.margin-bottom-3 { margin-bottom: 1rem }
.margin-bottom-4 { margin-bottom: 1.5rem }
.margin-bottom-5 { margin-bottom: 2rem }
.margin-left-0 { margin-left: 0 }
.margin-left-1 { margin-left: 0.25rem }
.margin-left-2 { margin-left: 0.5rem }
.margin-left-3 { margin-left: 1rem }
.margin-left-4 { margin-left: 1.5rem }
.margin-left-5 { margin-left: 2rem }
.margin-left-auto { margin-left: auto }
.margin-right-0 { margin-right: 0 }
.margin-right-1 { margin-right: 0.25rem }
.margin-right-2 { margin-right: 0.5rem }
.margin-right-3 { margin-right: 1rem }
.margin-right-4 { margin-right: 1.5rem }
.margin-right-5 { margin-right: 2rem }

// Padding
.padding-0  { padding: 0 }
.padding-1  { padding: 0.25rem }
.padding-2  { padding: 0.5rem }
.padding-3  { padding: 1rem }
.padding-4  { padding: 1.5rem }
.padding-5  { padding: 2rem }
.padding-6  { padding: 3rem }
.padding-inline-0 { padding-inline: 0 }
.padding-inline-1 { padding-inline: 0.25rem }
.padding-inline-2 { padding-inline: 0.5rem }
.padding-inline-3 { padding-inline: 1rem }
.padding-inline-4 { padding-inline: 1.5rem }
.padding-inline-5 { padding-inline: 2rem }
.padding-block-0 { padding-block: 0 }
.padding-block-1 { padding-block: 0.25rem }
.padding-block-2 { padding-block: 0.5rem }
.padding-block-3 { padding-block: 1rem }
.padding-block-4 { padding-block: 1.5rem }
.padding-block-5 { padding-block: 2rem }
.padding-top-0 { padding-top: 0 }
.padding-top-1 { padding-top: 0.25rem }
.padding-top-2 { padding-top: 0.5rem }
.padding-top-3 { padding-top: 1rem }
.padding-top-4 { padding-top: 1.5rem }
.padding-top-5 { padding-top: 2rem }
.padding-bottom-0 { padding-bottom: 0 }
.padding-bottom-1 { padding-bottom: 0.25rem }
.padding-bottom-2 { padding-bottom: 0.5rem }
.padding-bottom-3 { padding-bottom: 1rem }
.padding-bottom-4 { padding-bottom: 1.5rem }
.padding-bottom-5 { padding-bottom: 2rem }
.padding-left-0 { padding-left: 0 }
.padding-left-1 { padding-left: 0.25rem }
.padding-left-2 { padding-left: 0.5rem }
.padding-left-3 { padding-left: 1rem }
.padding-left-4 { padding-left: 1.5rem }
.padding-left-5 { padding-left: 2rem }
.padding-right-0 { padding-right: 0 }
.padding-right-1 { padding-right: 0.25rem }
.padding-right-2 { padding-right: 0.5rem }
.padding-right-3 { padding-right: 1rem }
.padding-right-4 { padding-right: 1.5rem }
.padding-right-5 { padding-right: 2rem }

// Width / Height
.width-full { width: 100% }
.width-auto { width: auto }
.width-screen { width: 100vw }
.height-full { height: 100% }
.height-auto { height: auto }
.height-screen { height: 100vh }
.min-width-0 { min-width: 0 }
.min-height-0 { min-height: 0 }
.max-width-full { max-width: 100% }
.max-height-full { max-height: 100% }

// Text
.text-left { text-align: left }
.text-center { text-align: center }
.text-right { text-align: right }
.text-justify { text-align: justify }
.text-uppercase { text-transform: uppercase }
.text-lowercase { text-transform: lowercase }
.text-capitalize { text-transform: capitalize }
.text-decoration-none { text-decoration: none }
.text-underline { text-decoration: underline }
.text-line-through { text-decoration: line-through }
.text-bold { font-weight: 700 }
.text-semibold { font-weight: 600 }
.text-medium { font-weight: 500 }
.text-normal { font-weight: 400 }
.text-light { font-weight: 300 }
.text-italic { font-style: italic }
.text-not-italic { font-style: normal }

// Font sizes
.text-xs { font-size: 0.75rem }
.text-sm { font-size: 0.875rem }
.text-base { font-size: 1rem }
.text-lg { font-size: 1.125rem }
.text-xl { font-size: 1.25rem }
.text-2xl { font-size: 1.5rem }
.text-3xl { font-size: 1.875rem }
.text-4xl { font-size: 2.25rem }
.text-5xl { font-size: 3rem }

// Colors (can be overridden with $primary, etc.)
.text-primary { color: $primary }
.text-secondary { color: $secondary }
.text-muted { color: $text-muted }
.text-white { color: #fff }
.text-black { color: #000 }

// Background
.background-primary { background-color: $primary }
.background-secondary { background-color: $secondary }
.background-transparent { background-color: transparent }
.background-white { background-color: #fff }
.background-black { background-color: #000 }

// Border
.border { border: 1px solid $border }
.border-0 { border: 0 }
.border-top { border-top: 1px solid $border }
.border-bottom { border-bottom: 1px solid $border }
.border-left { border-left: 1px solid $border }
.border-right { border-right: 1px solid $border }
.rounded { border-radius: 0.25rem }
.rounded-sm { border-radius: 0.125rem }
.rounded-md { border-radius: 0.375rem }
.rounded-lg { border-radius: 0.5rem }
.rounded-xl { border-radius: 0.75rem }
.rounded-2xl { border-radius: 1rem }
.rounded-full { border-radius: 9999px }
.rounded-none { border-radius: 0 }

// Overflow
.overflow-auto { overflow: auto }
.overflow-hidden { overflow: hidden }
.overflow-visible { overflow: visible }
.overflow-scroll { overflow: scroll }
.overflow-x-auto { overflow-x: auto }
.overflow-x-hidden { overflow-x: hidden }
.overflow-y-auto { overflow-y: auto }
.overflow-y-hidden { overflow-y: hidden }

// Position
.position-relative { position: relative }
.position-absolute { position: absolute }
.position-fixed { position: fixed }
.position-sticky { position: sticky }
.position-static { position: static }

// Top/Right/Bottom/Left
.top-0 { top: 0 }
.right-0 { right: 0 }
.bottom-0 { bottom: 0 }
.left-0 { left: 0 }
.top-auto { top: auto }
.right-auto { right: auto }
.bottom-auto { bottom: auto }
.left-auto { left: auto }

// Z-index
.z-index-0 { z-index: 0 }
.z-index-10 { z-index: 10 }
.z-index-20 { z-index: 20 }
.z-index-30 { z-index: 30 }
.z-index-40 { z-index: 40 }
.z-index-50 { z-index: 50 }
.z-index-auto { z-index: auto }

// Opacity
.opacity-0 { opacity: 0 }
.opacity-25 { opacity: 0.25 }
.opacity-50 { opacity: 0.5 }
.opacity-75 { opacity: 0.75 }
.opacity-100 { opacity: 1 }

// Cursor
.cursor-auto { cursor: auto }
.cursor-default { cursor: default }
.cursor-pointer { cursor: pointer }
.cursor-wait { cursor: wait }
.cursor-text { cursor: text }
.cursor-move { cursor: move }
.cursor-not-allowed { cursor: not-allowed }

// Shadow
.shadow-sm { box-shadow: 0 1px 2px 0 rgba(0,0,0,0.05) }
.shadow { box-shadow: 0 1px 3px 0 rgba(0,0,0,0.1), 0 1px 2px 0 rgba(0,0,0,0.06) }
.shadow-md { box-shadow: 0 4px 6px -1px rgba(0,0,0,0.1), 0 2px 4px -1px rgba(0,0,0,0.06) }
.shadow-lg { box-shadow: 0 10px 15px -3px rgba(0,0,0,0.1), 0 4px 6px -2px rgba(0,0,0,0.05) }
.shadow-xl { box-shadow: 0 20px 25px -5px rgba(0,0,0,0.1), 0 10px 10px -5px rgba(0,0,0,0.04) }
.shadow-none { box-shadow: none }

// Transition
.transition { transition: all 0.15s ease }
.transition-none { transition: none }
.transition-colors { transition: color, background-color, border, border-color, box-shadow 0.15s ease }
.transition-opacity { transition: opacity 0.15s ease }
.transition-transform { transition: transform 0.15s ease }

// Transform
.transform { transform: none }
.translate-x-0 { transform: translateX(0) }
.translate-y-0 { transform: translateY(0) }
.rotate-0 { transform: rotate(0) }
.scale-100 { transform: scale(1) }
.scale-0 { transform: scale(0) }

// Grid
.grid-cols-1 { grid-template-columns: repeat(1, 1fr) }
.grid-cols-2 { grid-template-columns: repeat(2, 1fr) }
.grid-cols-3 { grid-template-columns: repeat(3, 1fr) }
.grid-cols-4 { grid-template-columns: repeat(4, 1fr) }
.grid-cols-5 { grid-template-columns: repeat(5, 1fr) }
.grid-cols-6 { grid-template-columns: repeat(6, 1fr) }
.grid-cols-12 { grid-template-columns: repeat(12, 1fr) }

// Misc
.hidden { display: none }
.visible { visibility: visible }
.invisible { visibility: hidden }
.pointer-events-none { pointer-events: none }
.pointer-events-auto { pointer-events: auto }
.select-none { user-select: none }
.select-text { user-select: text }
.select-all { user-select: all }
.box-border { box-sizing: border-box }
.box-content { box-sizing: content-box }
"""

    /// Common CSS reset (normalize-like)
    let cssReset = """
*, *::before, *::after {
  box-sizing: border-box
  margin: 0
  padding: 0
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
  from { opacity: 0 }
  to   { opacity: 1 }
}
@keyframes fadeOut {
  from { opacity: 1 }
  to   { opacity: 0 }
}
@keyframes slideInUp {
  from { transform: translateY(100%) }
  to   { transform: translateY(0) }
}
@keyframes slideInDown {
  from { transform: translateY(-100%) }
  to   { transform: translateY(0) }
}
@keyframes slideInLeft {
  from { transform: translateX(-100%) }
  to   { transform: translateX(0) }
}
@keyframes slideInRight {
  from { transform: translateX(100%) }
  to   { transform: translateX(0) }
}
@keyframes bounce {
  0%, 20%, 53%, 80%, 100% { transform: translateY(0) }
  40%, 43% { transform: translateY(-30px) }
  70% { transform: translateY(-15px) }
  90% { transform: translateY(-4px) }
}
@keyframes pulse {
  0%, 100% { opacity: 1 }
  50% { opacity: 0.5 }
}
@keyframes spin {
  from { transform: rotate(0deg) }
  to   { transform: rotate(360deg) }
}
@keyframes ping {
  0% { transform: scale(1); opacity: 1 }
  75%, 100% { transform: scale(2); opacity: 0 }
}
@keyframes wiggle {
  0%, 100% { transform: rotate(-3deg) }
  50% { transform: rotate(3deg) }
}

// Animation classes
.animate-fade-in     { animation: fadeIn 0.3s ease-in }
.animate-fade-out     { animation: fadeOut 0.3s ease-out }
.animate-slide-up     { animation: slideInUp 0.3s ease-out }
.animate-slide-down   { animation: slideInDown 0.3s ease-out }
.animate-slide-left   { animation: slideInLeft 0.3s ease-out }
.animate-slide-right  { animation: slideInRight 0.3s ease-out }
.animate-bounce       { animation: bounce 1s ease-in-out infinite }
.animate-pulse        { animation: pulse 2s ease-in-out infinite }
.animate-spin         { animation: spin 1s linear infinite }
.animate-ping         { animation: ping 1s cubic-bezier(0,0,0.2,1) infinite }
.animate-wiggle       { animation: wiggle 0.5s ease-in-out infinite }
.animate-none         { animation: none }

// Animation timing
.ease-in    { animation-timing-function: ease-in }
.ease-out   { animation-timing-function: ease-out }
.ease-in-out { animation-timing-function: ease-in-out }
.ease-linear { animation-timing-function: linear }
.duration-75   { animation-duration: 75ms }
.duration-100  { animation-duration: 100ms }
.duration-150  { animation-duration: 150ms }
.duration-300  { animation-duration: 300ms }
.duration-500  { animation-duration: 500ms }
.duration-700  { animation-duration: 700ms }
.duration-1000 { animation-duration: 1000ms }
.delay-75   { animation-delay: 75ms }
.delay-100  { animation-delay: 100ms }
.delay-150  { animation-delay: 150ms }
.delay-300  { animation-delay: 300ms }
.delay-500  { animation-delay: 500ms }
.delay-700  { animation-delay: 700ms }
.delay-1000 { animation-delay: 1000ms }
"""

    /// Gradient utilities
    let gradientUtilities = """
// ── Gradient Utilities ───────────────────────────────────
.bg-gradient-to-r   { background-image: linear-gradient(to right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-l   { background-image: linear-gradient(to left, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-t   { background-image: linear-gradient(to top, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-b   { background-image: linear-gradient(to bottom, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-tr  { background-image: linear-gradient(to top right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-tl  { background-image: linear-gradient(to top left, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-br  { background-image: linear-gradient(to bottom right, var(--gradient-stops, $primary, $secondary)) }
.bg-gradient-to-bl  { background-image: linear-gradient(to bottom left, var(--gradient-stops, $primary, $secondary)) }

// Solid gradient presets
.bg-gradient-primary   { background-image: linear-gradient(135deg, $primary, lighten($primary, 10%)) }
.bg-gradient-success   { background-image: linear-gradient(135deg, $success, lighten($success, 10%)) }
.bg-gradient-danger    { background-image: linear-gradient(135deg, $danger, lighten($danger, 10%)) }
.bg-gradient-warning   { background-image: linear-gradient(135deg, $warning, lighten($warning, 10%)) }
.bg-gradient-info      { background-image: linear-gradient(135deg, $info, lighten($info, 10%)) }
.bg-gradient-rainbow   { background-image: linear-gradient(135deg, #ef4444, #f97316, #eab308, #22c55e, #06b6d4, #8b5cf6) }
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
.transform          { transform: none }
.transform-gpu      { transform: translateZ(0) }
.translate-x-0      { transform: translateX(0) }
.translate-x-full   { transform: translateX(100%) }
.translate-x-1/2   { transform: translateX(50%) }
.translate-x-n1/2  { transform: translateX(-50%) }
.translate-y-0      { transform: translateY(0) }
.translate-y-full   { transform: translateY(100%) }
.translate-y-1/2   { transform: translateY(50%) }
.translate-y-n1/2  { transform: translateY(-50%) }
.rotate-0    { transform: rotate(0deg) }
.rotate-45   { transform: rotate(45deg) }
.rotate-90   { transform: rotate(90deg) }
.rotate-180  { transform: rotate(180deg) }
.rotate-270  { transform: rotate(270deg) }
.scale-0     { transform: scale(0) }
.scale-50    { transform: scale(0.5) }
.scale-75    { transform: scale(0.75) }
.scale-90    { transform: scale(0.9) }
.scale-100   { transform: scale(1) }
.scale-110   { transform: scale(1.1) }
.scale-125   { transform: scale(1.25) }
.scale-150   { transform: scale(1.5) }
.skew-x-0    { transform: skewX(0deg) }
.skew-x-12   { transform: skewX(12deg) }
.skew-y-0    { transform: skewY(0deg) }
.skew-y-12   { transform: skewY(12deg) }
"""

    /// Layout and container utilities
    let layoutUtilities = """
// ── Layout Utilities ──────────────────────────────────────
.container { max-width: 1280px; margin-inline: auto; padding-inline: 1rem }
.container-sm { max-width: 640px; margin-inline: auto; padding-inline: 1rem }
.container-md { max-width: 768px; margin-inline: auto; padding-inline: 1rem }
.container-lg { max-width: 1024px; margin-inline: auto; padding-inline: 1rem }
.container-xl { max-width: 1280px; margin-inline: auto; padding-inline: 1rem }
.container-2xl { max-width: 1536px; margin-inline: auto; padding-inline: 1rem }
.container-fluid { width: 100%; padding-inline: 1rem }

// Aspect ratio
.aspect-square { aspect-ratio: 1 / 1 }
.aspect-video { aspect-ratio: 16 / 9 }
.aspect-photo { aspect-ratio: 4 / 3 }
.aspect-wide { aspect-ratio: 21 / 9 }
.aspect-auto { aspect-ratio: auto }

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
