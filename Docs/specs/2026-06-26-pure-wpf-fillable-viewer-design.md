# Pure-WPF Fillable Viewer — Design Spec

**Date:** 2026-06-26
**Status:** approved (brainstorm), pending spec review
**Branch:** skia-v4 (2.0 work)

## Goal

Make `PdfLibrary.Wpf.Viewer` a **pure-WPF, SkiaSharp-free, fillable** PDF viewer: it renders pages through a new WPF render target (vector `DrawingGroup`), and overlays native WPF input controls over interactive form fields so users can fill them in and save. This both exercises the geometry-only `IRenderTarget` SPI with a second real backend and delivers a working forms experience.

## Context

The 2.0 renderer redesign already made the core SkiaSharp-free: text is flattened to glyph-outline paths, and `IRenderTarget` is geometry-only (`FillPath`/`StrokePath`/`SetClippingPath`/`DrawImage`/state, 19 members, 2 default no-ops). The SVG target (`PdfLibrary.Rendering.Svg`) proved an independent non-Skia backend is implementable. Form *filling* already works: `PdfTextField.Value`, `PdfButtonField.Check()`/`.SelectedOption`, `PdfChoiceField.SelectedValues`, and `Forms.Flatten()` are public and regenerate appearance streams via `PdfFontMetrics` (independent of the render SPI). See `.superpowers/sdd/d-forms-map.md` for the current surface.

What's missing for a fillable viewer: (a) the **geometry** to place controls over fields (widget rects + a PDF→image transform are internal/unexposed), and (b) a **non-Skia WPF rendering path** so the viewer can drop SkiaSharp.

## Key decisions

- **SkiaSharp stays as the cross-platform raster backend.** `PdfLibrary.Rendering.SkiaSharp` remains a shipped package; the 7 pixel-fidelity render-test files keep using it; `PdfLibrary.Integration` keeps it. We remove Skia *only from the viewer* (and drop `Generator`'s Skia dependency as a small cleanup). Skia is not eradicated repo-wide.
- **Vector rendering.** The WPF target produces a retained vector `DrawingGroup`; WPF rescales it on zoom (crisp for text/line-art; embedded raster images scale like any bitmap). No per-zoom re-render from us.
- **Coordinate split (unchanged from prior decision):** the library owns PDF→image (`PageGeometry`); the client (viewer) owns image→screen (one WPF zoom/scroll transform applied to *both* the vector visual and the control overlay, so they never drift).
- **Giant single-raster pages** (e.g. a full-page TIFF) render correctly but get no vector benefit and are bounded by decode-to-RGBA memory (backend-agnostic). Memory-bounded decode is a **documented limitation / deferred optimization**, not a v1 requirement.
- **Windows-only:** the WPF target and viewer are `net*-windows`; the forms-geometry API is cross-platform core.

## Architecture — three components

### Component 1 — Forms geometry API (core, framework-neutral, independent)

New/promoted public surface in `PdfLibrary` (forms model in `PdfLibrary.Editing.Forms`; geometry on `PdfLibrary.Document.PdfPage`):

- **`PdfFieldWidget`** (new, public): one visual placement of a field.
  - `int PageIndex` — 0-based page the widget annotation lives on (resolved by scanning page `/Annots`, cf. `FormFlattener.FindOwningPage`).
  - `PdfRect Rect` — the widget `/Rect` in PDF user space (Y-up).
  - `string? OnStateName` — the "on" appearance state for checkbox/radio (`/AP /N` key), else null.
  - back-reference to the owning `PdfFormField`.
- **`PdfFormField.Widgets`** promoted `internal IReadOnlyList<PdfDictionary>` → **public `IReadOnlyList<PdfFieldWidget>`** (projection over the existing widget dicts; the raw dict stays internal).
- **`PageGeometry`** (new, public value type) returned by **`PdfPage.GetGeometry(double scale = 1.0)`**:
  - `Matrix3x2 PdfToImage` — PDF user space (Y-up) → rendered-image pixels (Y-down, top-left origin). Identical to the renderer's initial transform: rotation-0 = `matrix(scale,0,0,-scale,-cropX*scale,(cropY+height)*scale)`, with the 90/180/270 variants. Built from the page's public `GetCropBox()`/`GetMediaBox()`/`Rotate` + the supplied scale.
  - `Matrix3x2 ImageToPdf` — the inverse (for click→PDF hit-testing).
  - `int PixelWidth`, `int PixelHeight` — rendered pixel size at `scale`.
  - `PdfRect MapRectToImage(PdfRect pdfRect)` — convenience to map a widget rect into pixel space.

This component has no rendering dependency and ships independently; the SVG and Skia targets already build the same matrix internally (it is now extracted as the single public source of truth, and the renderer targets MAY be refactored to consume it later — out of scope here).

### Component 2 — WPF render target (`PdfLibrary.Rendering.Wpf`, new, Windows-only)

A new package targeting `net8.0-windows;net9.0-windows;net10.0-windows`, referencing only `PdfLibrary` core (no SkiaSharp).

- **`WpfRenderTarget : IRenderTarget`** draws into a `System.Windows.Media.DrawingContext` obtained from a `DrawingGroup`/`DrawingVisual`, and exposes the finished `DrawingGroup` (or a `DrawingImage`) after `EndPage`.
  - `BeginPage` opens the group and applies the page initial transform (same matrix as `PageGeometry.PdfToImage`) as the root `Transform`.
  - `FillPath`/`StrokePath`/`FillAndStrokePath` → `StreamGeometry` (from `IPathBuilder.Segments`: `M`/`L`/`C`/`Z`) drawn with a `Brush`/`Pen`; fill rule even-odd vs nonzero via `Geometry.FillRule`. Colors via the existing public `PdfColorToRgb`. **Stroke width and dashes scaled by the CTM linear factor `sqrt(|det(Ctm)|)`** (the `PathRenderer`/SVG lesson — path coords are CTM-baked, scalar measures are not). Glyph fills use even-odd.
  - `SetClippingPath` + `SaveState`/`RestoreState` → `DrawingContext.PushClip`/`Push(...)` with a matching pop stack.
  - `DrawImage` → **`PdfImageToRgba`** (new core helper, see below) → `WriteableBitmap` (BGRA) → `DrawImage` in the unit square with the image Y-flip + `state.Ctm`.
  - `ApplyCtm` tracks `state.Ctm` (paths are pre-baked; used for images). `OnGraphicsStateChanged` no-op/state. `GetPageDimensions` from the page+scale.
  - Shadings (`PaintShading`/`FillPathWithShadingPattern`, default no-ops), tiling patterns, and soft masks: **approximate first** — gradient shadings → WPF `LinearGradientBrush`/`RadialGradientBrush` where cheap, else solid fallback; tiling → solid fill; soft masks → render unmasked. Mark deferred/approximate (parity with the SVG target's scope).
- **`PdfImageToRgba`** (new, core, public): hoist the pixel-decode math out of the SkiaSharp `ImageRenderer.CreateBitmapFromPdfImage` (~350–400 lines: color spaces, indexed-palette expansion, CMYK, 1-bit, image masks) into a SkiaSharp-free `(byte[] rgba, int width, int height) ToRgba(PdfImage, …)`. The SkiaSharp `ImageRenderer` MAY later delegate to it (one source of truth), but that refactor is out of scope; this component only *adds* the helper and the WPF consumer. Memory-bounded/streamed decode for giant images is deferred.
- **`WpfPageExtensions.RenderToDrawing(this PdfPage, scale)`** convenience returning the `DrawingGroup`.

### Component 3 — Pure-WPF viewer (rewire `PdfLibrary.Wpf.Viewer`)

- Replace the SkiaSharp display path (`SkiaRenderer` / `SKImage`) with hosting the WPF `DrawingGroup` (e.g. a `DrawingVisual` host or an `Image` with a `DrawingImage`). **Remove the SkiaSharp `<ProjectReference>`/`using`s.**
- **Form-control overlay:** an overlay layer above the page visual. For each page in view, enumerate `editor.Forms` → `field.Widgets` on that page; for each widget, position a native control at `PageGeometry.MapRectToImage(widget.Rect)`, mapped to screen by the same zoom/scroll transform as the page visual. Control per field type:
  - `PdfTextField` → `TextBox` (multiline if `IsMultiline`); on commit set `field.Value`.
  - `PdfButtonField` Checkbox → `CheckBox` → `.Check()`/`.Uncheck()`; Radio → grouped `RadioButton`s keyed by `OnStateName` → `.SelectedOption`.
  - `PdfChoiceField` → `ComboBox` (combo) / `ListBox` (list) bound to `.Options` → `.SelectedValues`.
  - `PdfSignatureField` → read-only indicator.
- While editing, the **live control is authoritative on screen** (it covers the field region), so no mid-edit page re-render is needed. Writing back through the field setter regenerates the field's appearance stream in the model; that regenerated appearance is what persists on save (and what the Skia/SVG/WPF renderers draw for the field thereafter).
- **Save / Flatten** commands: `editor.Save(path)` and optional `Forms.Flatten()` then save.

## Data flow

`PdfDocument.Load` → viewer renders each page via `WpfRenderTarget` → hosts the `DrawingGroup` → builds the overlay from `editor.Forms[*].Widgets` + `page.GetGeometry(scale)` → user edits a control → control writes back to the field (appearance regenerates) → on Save, `editor.Save()` (optionally `Flatten()` first).

## Error handling

- `GetGeometry` on a page with a degenerate/missing box → fall back to MediaBox (existing `GetCropBox` behavior); never throw for normal docs.
- `PdfImageToRgba` on an unsupported/corrupt image → return null/empty; the WPF target skips the image (logs), as the renderers do today.
- Widget with no resolvable page (orphan) → omit from `Widgets` (PageIndex unresolvable) rather than throw.
- A field type the overlay doesn't handle → render the page content (the baked appearance) with no live control.

## Testing strategy

- **D1 (forms geometry):** cross-platform core tests. `PageGeometry` matrix correctness (rotation 0/90/180/270, scale, crop offset; round-trip `ImageToPdf∘PdfToImage ≈ identity`); `PdfFieldWidget` enumeration (rect/page/on-state) against a fixture form PDF; a **headless round-trip**: load a form → read widget geometry → set values → `Flatten()` → save → reload → assert values/appearance.
- **D2 (WPF target):** Windows-targeted tests (the package is `net*-windows`). Structural assertions on the produced `DrawingGroup` (expected `GeometryDrawing`/`ImageDrawing` children, stroke widths CTM-scaled); optional `RenderTargetBitmap` rasterization for a non-trivial-content smoke check. `PdfImageToRgba` unit tests (RGB/Gray/CMYK/indexed/1-bit → expected RGBA) cross-platform in core.
- **D3 (viewer):** manual run — open form PDFs, fill fields, zoom (confirm vector crispness + overlay tracking), save, reload to confirm persisted values. Build-clean + Skia-free grep on the viewer.

## Non-goals / deferred

- Eradicating SkiaSharp repo-wide (kept as the cross-platform raster backend + test gate).
- Memory-bounded/streamed decode for giant single-raster pages (documented limitation).
- Full shading/tiling-pattern/soft-mask fidelity in the WPF target (approximate first, like SVG).
- Refactoring the SkiaSharp/SVG targets to consume `PageGeometry`/`PdfImageToRgba` (additive now; unify later).
- Form features beyond fill: JavaScript actions, validation/format scripts, digital signing.

## Implementation staging (for writing-plans)

Three coherent, independently-testable slices:

- **Plan D1 — Forms geometry API:** `PdfFieldWidget`, public `PdfFormField.Widgets`, `PageGeometry` + `PdfPage.GetGeometry`. Ships a usable public API + the headless round-trip test. Independent of D2/D3.
- **Plan D2 — WPF render target:** `PdfImageToRgba` core helper + `PdfLibrary.Rendering.Wpf` (`WpfRenderTarget` + `RenderToDrawing`). The big slice; depends on the RGBA helper (its first task).
- **Plan D3 — Pure-WPF viewer:** rewire the viewer onto `WpfRenderTarget`, drop its SkiaSharp reference, add the native form-control overlay via D1, Save/Flatten; drop `Generator`'s Skia dependency. Depends on D1 + D2.
