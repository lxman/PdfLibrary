# Renderer SPI — 2.0 Design

**Status:** Approved design, pending spec review → implementation plan
**Branch:** `skia-v4` (2.0 line)
**Date:** 2026-06-26

## Goal

Make `PdfLibrary` a first-class **"bring your own renderer"** library: a third party can implement a documented, stable, **SkiaSharp-free** contract and render PDFs with any backend (Avalonia, WPF/Direct2D, a web canvas, a headless raster buffer, GDI+, …) without taking a SkiaSharp dependency.

The core (`Lxman.PdfLibrary`) stays **pure managed with zero SkiaSharp**. SkiaSharp is confined entirely to the adapter package (`Lxman.PdfLibrary.Rendering.SkiaSharp`), which becomes a thin reference implementation.

## Locked decisions

1. **First-class public SPI** — the bring-your-own-renderer contract is a supported, documented product surface, not an incidental seam.
2. **Thin target (geometry-only)** — the core does *all* PDF complexity (including glyph resolution and glyph→outline) and hands the target only geometry. A target implements ~10 pure-geometry methods.
3. **Core stays SkiaSharp-free** — the core emits its own geometry type (`IPathBuilder`), never `SKPath`.
4. **Non-embedded fonts** — bundle open, metric-compatible substitutes for the 14 standard fonts in the core *and* extend the font-provider SPI to supply font-program bytes. The bundled set guarantees correct, deterministic, offline rendering; the provider supplies better matches when available.

## Current state (baseline)

- `PdfPage.Render(IRenderTarget target, int pageNumber = 1, double scale = 1.0)` is **already public** — the SPI entry point exists.
- `IRenderTarget` (public) already speaks PDF primitives only — `IPathBuilder`, `PdfGraphicsState`, `PdfFont`, `PdfImage`, `System.Numerics.Matrix3x2`. **No SkiaSharp types leak through it.**
- The core has **zero** SkiaSharp references today (verified: no package ref, no `using SkiaSharp` in core source).
- `PdfImage.GetDecodedData()` + `Width`/`Height`/`BitsPerComponent`/`ColorSpace`/`GetIndexedPalette` are public — a target blits decoded pixels with no codec dependency.
- `PathSegment` is a public record hierarchy (`MoveToSegment`/`LineToSegment`/`CurveToSegment`/`ClosePathSegment`, plain doubles) — the geometry vocabulary a target consumes.

**The gap:** glyph resolution + glyph→outline currently lives in the **SkiaSharp package's** `TextRenderer` (~700 lines) and produces `SKPath`. Non-embedded (standard-14 / referenced-but-not-embedded) text is drawn via `SKTypeface`/`SKFontManager` — the only text rendering that is genuinely SkiaSharp-bound today.

## Architecture

### 1. The geometry-only `IRenderTarget` (the SPI)

The target loses all font/glyph/text knowledge. Final method set:

- **Page lifecycle:** `BeginPage(...)`, `EndPage()`, `Clear()`, `CurrentPageNumber`
- **State:** `SaveState()`, `RestoreState()`, `ApplyCtm(Matrix3x2)`, `OnGraphicsStateChanged(PdfGraphicsState)` (alpha, blend mode)
- **Paths:** `FillPath`, `StrokePath`, `FillAndStrokePath`, `SetClippingPath` (+ `FillPathWithTilingPattern`, `FillPathWithShadingPattern`, `PaintShading` — already optional / default-no-op)
- **Images:** `DrawImage(PdfImage, PdfGraphicsState)` — target calls `GetDecodedData()`
- **Soft mask:** `RenderSoftMask(string subtype, Action<IRenderTarget>)`, `ClearSoftMask()`, `GetPageDimensions()`

**Removed from the target:** `DrawText(...)` and `MeasureTextWidth(...)`.

Text becomes paths: the core resolves each glyph to an outline path **in user space** (text matrix + font scale baked in, exactly like the current "CTM on canvas, glyph transform separate" model) and drives it through the existing path methods per the PDF text-rendering mode (Tr):

| Tr mode | Core calls |
|---|---|
| 0 fill | `FillPath(glyph, state, evenOdd: true)` |
| 1 stroke | `StrokePath(glyph, state)` |
| 2 fill+stroke | `FillAndStrokePath(glyph, state, evenOdd: true)` |
| 3 invisible | skip paint (still advance) |
| 4–7 clip | as above **+** add glyph to text-clip accumulator |

(Glyph fills use even-odd, matching today's converter, which sets `FillType = EvenOdd` to handle the Y-flip winding reversal.) This **unifies glyph rendering with path rendering** — a glyph is just another user-space path the target fills.

### 2. Core text pipeline (moved from the Skia package)

Moves into the core (the internal `PdfRenderer` / a new internal core `TextPipeline`):

- **Glyph resolution** — charCode → glyphId → outline across TrueType / CFF / Type1 (today's `ResolveGlyphId` + dispatch in the Skia `TextRenderer`). Uses `EmbeddedFontMetrics` / `GlyphOutline`, which are already core types.
- **New `GlyphOutlineToPath`** (core) — converts a glyph outline to `IPathBuilder` / `PathSegment` (replaces the Skia `GlyphToSKPathConverter`, which produced `SKPath`).
- **Per-glyph path cache** — moves core-side (keyed by font + glyphId), caching `IPathBuilder`.
- **Width measurement** — the core measures with the resolved font's metrics; the "fixup" width-mismatch logic becomes core-internal (replaces `IRenderTarget.MeasureTextWidth`).

`GlyphOutline` / `GlyphContour` / `ContourPoint` / `GlyphMetrics` / `EmbeddedFontMetrics` **stay internal** — only the core touches them; the target sees paths. This is why approach A reconciles with the public-surface cleanup.

### 3. Fonts: standard-14 substitutes + provider SPI

- **Bundle metric-compatible open substitutes** for the 14 standard fonts as embedded resources in the core; parse them with FontParser (already referenced) → outlines → paths. Guarantees the core can always produce an outline → the thin-target boundary never breaks → deterministic rendering across machines.
  - Coverage required: Helvetica ×4, Times ×4, Courier ×4, Symbol, ZapfDingbats.
  - **Licensing sub-task:** pick a redistributable, metric-compatible set covering all 14 (candidates: URW++ base-35; GNU FreeFont; Liberation Sans/Serif/Mono covers Helvetica/Times/Courier under OFL but not Symbol/ZapfDingbats — a combination may be needed). Vendor with a license file, the way `PublicPixel.ttf` is vendored in tests.
- **Extend `ISystemFontProvider`** to optionally supply **font-program bytes** for a family (today it returns only family *names*). The core parses provider-supplied bytes with FontParser for better matches when available. SkiaSharp becomes *one optional implementation* (SKTypeface → bytes); a consumer with no provider still renders correctly from the bundled substitutes.
- **Resolution order** for non-embedded glyphs: embedded program (if any) → provider bytes (if wired) → bundled substitute (always available).

### Interaction: forms & appearance generation

Form filling is **unaffected** by this redesign. Verified: nothing under `Editing/` references `IRenderTarget`, and `FieldAppearanceGenerator` measures text via the core `PdfFontMetrics.MeasureText(…, "Helvetica", …)`, **not** the `IRenderTarget.MeasureTextWidth` being removed. Filling = write `/V` + generate `/AP` appearance streams + (optional) flatten — pure model editing, decoupled from the renderer.

Rendering a *filled* form is just rendering its `/AP` Widget-appearance content streams through the normal content→target pipeline; under the new SPI their text flattens to glyph paths like any other content.

**Net positive:** the default form-field font is Helvetica — a standard-14, non-embedded font, exactly the case the bundled substitutes cover. Filled-form text therefore renders from the bundled metric-compatible substitute (deterministic, offline) rather than a system font.

**Constraint:** the bundled substitutes must be metric-compatible with the AFM widths `PdfFontMetrics` reports for the standard-14, so the positions baked into `/AP` at fill time match the glyph advances used at render time. (Already implied by "metric-compatible"; called out because forms depend on it.)

### 4. SkiaSharp package → thin adapter

`SkiaSharpRenderTarget` shrinks to: `PathSegment` → `SKPath` (via `SKPathBuilder`), decoded pixels → `SKImage`, clip / state / blend / soft-mask. Its text + glyph code is **deleted** (now core-side). `GlyphToSKPathConverter`, the Skia `TextRenderer` glyph logic, `SystemFontResolver`/`SkiaFontProvider` (re-expressed as an `ISystemFontProvider` byte source) shrink or disappear. The package becomes a *reference adapter*, not a renderer.

`SkiaSharpRenderTarget`'s public/internal fate: it can return to **internal**, since consumers either call `page.RenderTo()` (the Skia convenience entry, which stays public) or implement their own `IRenderTarget`. **Verification gate:** confirm the WPF viewer's incremental-redraw needs are met by `page.RenderTo()` or by the viewer owning a small target, before finalizing internal.

### 5. First-class deliverables

- **SPI documentation** — a "Writing a render target" guide in `Docs/`, listing the exact public contract and a worked example.
- **Sample managed target** — a headless, pure-managed raster target (renders to a `byte[]` RGBA buffer) that proves the contract end-to-end with no SkiaSharp, and doubles as the reference for third parties. Lives as an example project.

## Public SPI surface (documented + stabilized)

The minimal contract a target author depends on:

- `IRenderTarget` (geometry-only)
- `IPathBuilder` **with `Segments` lifted onto the interface** + the `PathSegment` records
- `PdfGraphicsState` — **audit for leaks**; expose a read-only view of PDF-level values (colors, alpha, line params, CTM, rendering intent), no Skia/implementation types
- `PdfImage` decoded-pixel accessors, `ShadingDescriptor`, `PdfTilingPattern`
- `ISystemFontProvider` (extended with a font-bytes method)
- `PdfPage.Render(IRenderTarget, …)` entry point

Everything else under `Rendering/` stays internal.

**SemVer:** removing `DrawText`/`MeasureTextWidth` from `IRenderTarget`, re-internalizing `SkiaSharpRenderTarget`, and the glyph-type demotions are all breaking — this ships in **2.0** (the `skia-v4` branch). The SkiaSharp dependency moving to 4.x is itself already a 2.0-level break for consumers.

## Migration / phasing

The 90 rendering tests are the pixel-identical gate throughout.

1. **Prep (no behavior change):** lift `Segments` onto `IPathBuilder`; audit/clean `PdfGraphicsState` public view; add the font-bytes method to `ISystemFontProvider`.
2. **Glyph→path in the core:** build `GlyphOutlineToPath` (→ `IPathBuilder`); move glyph resolution into the core text pipeline *behind the current `IRenderTarget.DrawText`* (Skia target temporarily still receives `DrawText`). Keep tests green.
3. **Bundled substitutes + provider bytes:** vendor the standard-14 substitute set; wire the resolution order; make non-embedded text render core-side without SkiaSharp.
4. **Flip text to paths:** core emits `FillPath`/`StrokePath`/clip for glyphs; **remove `DrawText`/`MeasureTextWidth`** from `IRenderTarget`; delete Skia text/glyph code.
5. **Adapter + sample + docs:** slim `SkiaSharpRenderTarget` to an adapter (and resolve its internal/public fate against the viewer); add the sample managed target; write the SPI guide.

## Risks / open questions

- **Pixel fidelity of the core glyph pipeline** — moving glyph→path + caching must reproduce current output exactly (the tuned 700-line `TextRenderer`). Gated by the 90 render tests; expect careful matrix/winding work.
- **Substitute-font licensing + size** — must find a redistributable set covering all 14 standard fonts; adds a few MB to the core package.
- **Substitute-font metric fidelity** — substitutes must be metric-compatible enough that non-embedded text lays out correctly (widths drive positioning).
- **`PdfGraphicsState` leak audit** — confirm nothing implementation-specific is exposed once it's a documented SPI type.
- **WPF viewer migration** — confirm incremental rendering is satisfied by the public surface before making `SkiaSharpRenderTarget` internal.
- **Shadings/patterns** — remain target responsibilities (already optional). Not flattened by the core in this design.

## Out of scope

The other 2.0 cleanup items proceed independently of this spec: verb unification (`Load`/`Open`, `CreateEmpty`/`CreateBlank`), `PdfImageBuilder.Done()`, `PdfColor.Components`/`Gray`, namespacing + `Pdf`-prefixing the global enums, `PdfContentProcessor` text-hook accessibility. The internal-plumbing demotions already landed (commit `4485fa0`); the SkiaSharp 4.x API migration already landed (`8df3030`).

## Success criteria

- `Lxman.PdfLibrary` ships with **no SkiaSharp dependency** and renders any PDF to any `IRenderTarget`.
- A third party implements ~10 geometry methods and gets correct, full-fidelity PDF rendering (including embedded + standard-14 text) with no SkiaSharp.
- The bundled sample managed target renders the test corpus with no SkiaSharp referenced.
- `Lxman.PdfLibrary.Rendering.SkiaSharp` is a thin adapter; SkiaSharp appears nowhere else.
- All existing tests stay green; the 90 render tests stay pixel-identical.
