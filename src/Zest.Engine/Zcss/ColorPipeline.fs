namespace Zest.Engine.Zcss

open System
open System.Text.RegularExpressions

// ============================================================
// Color Pipeline — Color manipulation functions
// ============================================================

module ColorPipeline =

    let private hexToRgb (hex: string) : (int * int * int) option =
        let h = hex.TrimStart('#')
        if h.Length = 6 then
            try Some(Convert.ToInt32(h.[0..1], 16),
                     Convert.ToInt32(h.[2..3], 16),
                     Convert.ToInt32(h.[4..5], 16))
            with _ -> None
        elif h.Length = 3 then
            try Some(Convert.ToInt32(string h.[0] + string h.[0], 16),
                     Convert.ToInt32(string h.[1] + string h.[1], 16),
                     Convert.ToInt32(string h.[2] + string h.[2], 16))
            with _ -> None
        elif h.Length = 8 then  // #RRGGBBAA
            try Some(Convert.ToInt32(h.[0..1], 16),
                     Convert.ToInt32(h.[2..3], 16),
                     Convert.ToInt32(h.[4..5], 16))
            with _ -> None
        else None

    let private clamp v = max 0 (min 255 v)
    let private toHex r g b = sprintf "#%02x%02x%02x" (clamp r) (clamp g) (clamp b)

    let lighten (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r+d) (g+d) (b+d)

    let darken (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> let d = pct * 255 / 100 in toHex (r-d) (g-d) (b-d)

    let alpha (hex: string) (a: float) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> sprintf "rgba(%d,%d,%d,%.2f)" r g b a

    let mix (hex1: string) (hex2: string) (pct: int) =
        match hexToRgb hex1, hexToRgb hex2 with
        | Some(r1,g1,b1), Some(r2,g2,b2) ->
            let w = float pct / 100.0
            toHex (int (float r1*w + float r2*(1.0-w)))
                  (int (float g1*w + float g2*(1.0-w)))
                  (int (float b1*w + float b2*(1.0-w)))
        | _ -> hex1

    // ── New color functions ──

    let complement (hex: string) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> toHex (255-r) (255-g) (255-b)

    let grayscale (hex: string) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            let gray = int (float r * 0.299 + float g * 0.587 + float b * 0.114)
            toHex gray gray gray

    let invert (hex: string) = complement hex

    let saturate (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            let avg = (r + g + b) / 3
            let factor = float pct / 100.0
            let s v = int (float avg + (float v - float avg) * (1.0 + factor))
            toHex (s r) (s g) (s b)

    let desaturate (hex: string) (pct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            let avg = (r + g + b) / 3
            let factor = 1.0 - float pct / 100.0
            let s v = int (float avg + (float v - float avg) * factor)
            toHex (s r) (s g) (s b)

    let adjustHue (hex: string) (deg: float) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            // Convert RGB to HSL, adjust hue, convert back
            let rf = float r / 255.0
            let gf = float g / 255.0
            let bf = float b / 255.0
            let mx = max rf (max gf bf)
            let mn = min rf (min gf bf)
            let l = (mx + mn) / 2.0
            let s =
                if mx = mn then 0.0
                elif l < 0.5 then (mx - mn) / (mx + mn)
                else (mx - mn) / (2.0 - mx - mn)
            let h =
                if mx = mn then 0.0
                elif mx = rf then 60.0 * (gf - bf) / (mx - mn)
                elif mx = gf then 60.0 * (bf - rf) / (mx - mn) + 120.0
                else 60.0 * (rf - gf) / (mx - mn) + 240.0
            let newH = (h + deg) % 360.0
            let newH = if newH < 0.0 then newH + 360.0 else newH
            // HSL to RGB
            let c = (1.0 - abs (2.0 * l - 1.0)) * s
            let x = c * (1.0 - abs ((newH / 60.0) % 2.0 - 1.0))
            let m = l - c / 2.0
            let (r', g', b') =
                if newH < 60.0 then (c, x, 0.0)
                elif newH < 120.0 then (x, c, 0.0)
                elif newH < 180.0 then (0.0, c, x)
                elif newH < 240.0 then (0.0, x, c)
                elif newH < 300.0 then (x, 0.0, c)
                else (c, 0.0, x)
            toHex (int ((r' + m) * 255.0)) (int ((g' + m) * 255.0)) (int ((b' + m) * 255.0))

    let tint (hex: string) (pct: int) = mix "#ffffff" hex pct
    let shade (hex: string) (pct: int) = mix "#000000" hex pct

    // ── Additional color functions ──

    let transparentize (hex: string) (a: float) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) -> sprintf "rgba(%d,%d,%d,%.2f)" r g b (1.0 - a)

    let rgba (r: int) (g: int) (b: int) (a: float) =
        sprintf "rgba(%d,%d,%d,%.2f)" r g b a

    let rgb (r: int) (g: int) (b: int) =
        sprintf "rgb(%d,%d,%d)" r g b

    let hsl (h: float) (s: float) (l: float) =
        sprintf "hsl(%.0f,%.0f%%,%.0f%%)" h s l

    let hsla (h: float) (s: float) (l: float) (a: float) =
        sprintf "hsla(%.0f,%.0f%%,%.0f%%,%.2f)" h s l a

    let scaleColor (hex: string) (satPct: int) (lightPct: int) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            let scaled = saturate hex satPct
            match hexToRgb scaled with
            | None -> hex
            | Some(r2, g2, b2) ->
                if lightPct > 0 then lighten scaled lightPct
                else darken scaled (-lightPct)

    /// Scale a color's lightness by an amount in [-100, 100]. Positive
    /// lightens toward white, negative darkens toward black. This is the
    /// single-argument form matching the spec's `scale-color($color, $amount)`.
    let scaleColorByAmount (hex: string) (amount: int) =
        if amount > 0 then lighten hex amount
        elif amount < 0 then darken hex (-amount)
        else hex

    /// Return a high-contrast foreground color (black or white) for the
    /// given background. Uses perceived luminance; backgrounds brighter
    /// than the midpoint get `#000`, others get `#fff`.
    let contrastColor (hex: string) =
        match hexToRgb hex with
        | None -> hex
        | Some(r, g, b) ->
            // Perceived brightness (Rec. 601 luma weights)
            let luma = int (float r * 0.299 + float g * 0.587 + float b * 0.114)
            if luma > 128 then "#000000" else "#ffffff"

    // ── Regex-based function resolution ──

    let private fnPatterns =
        [|
            Regex(@"lighten\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> lighten m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"darken\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> darken m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"alpha\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> alpha m.Groups.[1].Value (float m.Groups.[2].Value))
            Regex(@"transparentize\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> transparentize m.Groups.[1].Value (float m.Groups.[2].Value))
            Regex(@"mix\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> mix m.Groups.[1].Value m.Groups.[2].Value (int m.Groups.[3].Value))
            Regex(@"complement\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> complement m.Groups.[1].Value)
            Regex(@"grayscale\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> grayscale m.Groups.[1].Value)
            Regex(@"invert\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> invert m.Groups.[1].Value)
            Regex(@"saturate\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> saturate m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"desaturate\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> desaturate m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"adjust-hue\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(-?[\d.]+)deg\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> adjustHue m.Groups.[1].Value (float m.Groups.[2].Value))
            Regex(@"tint\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> tint m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"shade\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(\d+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> shade m.Groups.[1].Value (int m.Groups.[2].Value))
            Regex(@"rgba\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> rgba (int m.Groups.[1].Value) (int m.Groups.[2].Value) (int m.Groups.[3].Value) (float m.Groups.[4].Value))
            Regex(@"rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> rgb (int m.Groups.[1].Value) (int m.Groups.[2].Value) (int m.Groups.[3].Value))
            Regex(@"hsla\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*,\s*([\d.]+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> hsla (float m.Groups.[1].Value) (float m.Groups.[2].Value) (float m.Groups.[3].Value) (float m.Groups.[4].Value))
            Regex(@"hsl\(\s*([\d.]+)\s*,\s*([\d.]+)%\s*,\s*([\d.]+)%\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> hsl (float m.Groups.[1].Value) (float m.Groups.[2].Value) (float m.Groups.[3].Value))
            Regex(@"contrast-color\(\s*(#[0-9a-fA-F]+|\w+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> contrastColor m.Groups.[1].Value)
            Regex(@"scale-color\(\s*(#[0-9a-fA-F]+|\w+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled),
                (fun (m: Match) -> scaleColorByAmount m.Groups.[1].Value (int m.Groups.[2].Value))
        |]

    let resolve (value: string) : string =
        let mutable result = value
        let mutable changed = true
        let mutable iterations = 0
        let maxIterations = 20  // safety limit to prevent infinite loops
        while changed && iterations < maxIterations do
            changed <- false
            iterations <- iterations + 1
            for (pat, fn) in fnPatterns do
                let m = pat.Match(result)
                if m.Success then
                    result <- result.Substring(0, m.Index) + fn m + result.Substring(m.Index + m.Length)
                    changed <- true
        result
