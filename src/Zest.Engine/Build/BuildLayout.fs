namespace Zest.Engine

open System
open System.Collections.Generic
open Zest.Engine.Build

/// Layout application — delegates to LayoutEngine for layout loading, placeholder replacement, and recursive layout rendering.
module BuildLayout =

    let internal loadLayouts = LayoutEngine.loadLayouts
    let internal loadIncludes = LayoutEngine.loadIncludes
    let internal buildReplacements = LayoutEngine.buildReplacements
    let internal setIncludesMtime = LayoutEngine.setIncludesMtime
    let internal applyLayout = LayoutEngine.applyLayout
