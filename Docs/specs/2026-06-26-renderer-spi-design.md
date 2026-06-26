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
4. **Non-embedded fonts — locate, don't bundle.** Confirmed (via the Everything index, on a real system) that standard OS font directories already carry metric-compatible base-14 substitutes. The core ships a managed, SkiaSharp-free **system-font locator** (the default `ISystemFontProvider`) that scans the standard dirs and returns font-program **bytes** for FontParser. Reading installed fonts is **not** redistribution → no bundling, no licensing, no Skia. A tiny optional fallback (only Symbol/ZapfDingbats) covers truly-bare headless systems.

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

### 3. Fonts: locate installed substitutes (SkiaSharp-free)

Confirmed via the Everything index on a real system: standard OS font dirs already carry metric-compatible base-14 substitutes — `C:\Windows\Fonts` holds full **Liberation** + **DejaVu**; the per-user dir holds the full **URW base-35 / Nimbus** set (Sans/Roman/MonoPS + StandardSymbolsPS + D050000L = all 14). Generally: Windows always ships Arial/Times New Roman/Courier New/Symbol (metric-identical to the base-14); macOS ships the real Helvetica/Times/Courier + Symbol/ZapfDingbats; Linux ships Liberation/DejaVu/Nimbus via fontconfig.

The core ships a managed, **SkiaSharp-free** system-font locator (the default `ISystemFontProvider`):
- Scans standard OS font dirs with `System.IO` — Windows: `%WINDIR%\Fonts` + `%LOCALAPPDATA%\Microsoft\Windows\Fonts`; Linux: `/usr/share/fonts`, `/usr/local/share/fonts`, `~/.local/share/fonts`, `~/.fonts`; macOS: `/System/Library/Fonts`, `/Library/Fonts`, `~/Library/Fonts`.
- Maps each base-14 face → ordered candidate families: Helvetica→{Arial, Liberation Sans, Nimbus Sans, Arimo, DejaVu Sans}; Times→{Times New Roman, Liberation Serif, Nimbus Roman, Tinos, DejaVu Serif}; Courier→{Courier New, Liberation Mono, Nimbus Mono PS, Cousine, DejaVu Sans Mono}; Symbol→{Symbol, StandardSymbolsPS}; ZapfDingbats→{ZapfDingbats, D050000L}.
- Returns font-program **bytes** → FontParser → outlines → paths.

**Reading installed fonts ≠ redistribution** → no licensing concern, no package bloat, no SkiaSharp. (The Everything HTTP server was a *developer* tool to confirm availability; the library uses plain `System.IO` enumeration, not Everything.)

**Headless fallback** (bare container, no fonts): the consumer supplies fonts via the provider SPI; or an *optional* tiny bundle of just **Symbol/ZapfDingbats** (URW `StandardSymbolsPS` + `D050000L`, redistributable, ~tens of KB — the only faces a minimal Linux box reliably lacks); or accept degraded rendering. Text faces are effectively always present.

**Resolution order** for non-embedded glyphs: embedded program → located system substitute → optional bundled Symbol/Dingbats fallback.

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

## Interactive forms — the input layer

The geometry SPI is **output-only**: it pushes pixels to a display surface and has no notion of "editable field." Interactivity is a **separate, orthogonal, renderer-agnostic layer** the client composes on top. The library provides the field *model*, *geometry*, and *value round-trip*; the client provides the native input controls (WPF/Avalonia/web/…). The library never owns a UI widget — that is what keeps it UI-agnostic. **No input/event methods are added to `IRenderTarget`.**

**The interactive loop:**
1. Render the page via the geometry SPI → static pixels (incl. each field's current `/AP`).
2. Enumerate fields via the forms read API — name, type, value, options, flags. *(Exists: `FormFieldTree` / `PdfFormField`.)*
3. For each field, get its **widget rectangle(s) + page index**, map PDF coords → device coords with the render's transform, and overlay a native control. *(Two new public pieces — see gaps.)*
4. User edits the native control; the client captures the value.
5. Client calls the forms write API — `field.Value = …` / `Check()` / `SelectedOption = …` / `SelectedValues = …` — which updates `/V` and regenerates `/AP`. **This is how the data returns: into the `PdfDocument` model.** *(Exists.)*
6. Client re-renders the affected region (or leaves the live control on top); `doc.Save(...)` persists the filled form. *(Exists.)*

So "how does the information get back to us?" → **through the editing API into the document model**, not through the render target. `field.Value` is both the commit point for user input and the getter to read it back; `Save` serializes it.

**New public surface required (two gaps):**
- **Field geometry.** Today `PdfFormField.Widgets` (the annotation dicts holding `/Rect`) is `internal`. Expose, per field, its widget rectangle(s) in PDF page coordinates **+ the page index** (a field may have multiple widgets across pages — radio groups). Likely shape: `IReadOnlyList<PdfFieldWidget>` where `PdfFieldWidget { int PageIndex; PdfRect Rect; string? OnStateName; }`.
- **Coordinate mapping.** A public PDF-page ↔ device transform for a given render (scale, rotation, crop offset, Y-flip) so the client positions overlays and hit-tests clicks. The client already *receives* `BeginPage(scale, cropOffset, rotation)` (it implements the target), but the Y-flip/rotation math is error-prone — provide a `PageGeometry`/matrix helper plus its inverse (device → PDF) for hit-testing.

This layer is **additive public API** (expose geometry + a transform helper) and is independent of the thin-target text refactor — it can land in its own phase without touching the render pipeline.

## Public SPI surface (documented + stabilized)

The minimal contract a target author depends on:

- `IRenderTarget` (geometry-only)
- `IPathBuilder` **with `Segments` lifted onto the interface** + the `PathSegment` records
- `PdfGraphicsState` — **audit for leaks**; expose a read-only view of PDF-level values (colors, alpha, line params, CTM, rendering intent), no Skia/implementation types
- `PdfImage` decoded-pixel accessors, `ShadingDescriptor`, `PdfTilingPattern`
- `ISystemFontProvider` (extended with a font-bytes method)
- `PdfPage.Render(IRenderTarget, …)` entry point
- **Interactive forms:** per-field widget geometry (rect + page index) + a public PDF↔device `PageGeometry`/transform helper (overlay positioning + hit-testing) — see "Interactive forms" above

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
