# Renderer SPI Implementer Guide

**Branch:** `skia-v4`  
**Interface:** `PdfLibrary.Rendering.IRenderTarget`  
**Worked example:** `PdfLibrary.Rendering.Svg.SvgRenderTarget`

---

## 1. The Model

The PDF core flattens all page content to geometry before calling any render target method:

- **Text** — glyph outlines are resolved by the core and emitted as filled/stroked paths. The render target receives only `FillPath`/`StrokePath`/`FillAndStrokePath` calls; it never receives glyph IDs, font handles, or font metrics.
- **Paths** — constructed from PDF path operators (`m`, `l`, `c`, `re`, `h`), baked through the current transformation matrix, and handed to the target as an `IPathBuilder`.
- **Images** — drawn into a 1×1 unit square at the origin, positioned by `state.Ctm`.
- **Shadings** — two optional members have default no-op implementations so a minimal target can ignore them.

A render target **never** touches SkiaSharp, font files, glyph tables, or filter decoding — the core handles all of that.

`IRenderTarget` has **19 members: 17 that must be implemented and 2 that have default no-op implementations** (see §3).

---

## 2. Coordinate Contract

This is the section most likely to cause bugs. Read it carefully.

### Paths are CTM-pre-baked

When the core calls `FillPath`, `StrokePath`, `FillAndStrokePath`, `SetClippingPath`, or `FillPathWithTilingPattern`, the path segments **already have the current transformation matrix (CTM) applied**. The coordinates are in PDF user space with Y increasing upward. The render target must **not** re-apply the CTM to path coordinates.

The render target's only job for paths is to apply the **page initial transform** — the Y-flip, render scale, crop offset, and optional rotation — to convert from PDF user space to device space.

### `ApplyCtm` is for images, not paths

`ApplyCtm(Matrix3x2 ctm)` is called when the PDF `cm` operator changes the CTM. **It has no effect on paths** (which are already baked). It exists so targets can track the current CTM for use in `DrawImage`, which renders a 1×1 unit square whose position is determined by `state.Ctm`.

### The initial transform

`BeginPage` receives the page geometry. From these parameters the target constructs the **initial transform** — the single matrix that converts from PDF user space to device space. All path output lives inside this transform.

**Rotation 0 (most common):**

```csharp
Matrix3x2 init = Matrix3x2.CreateTranslation((float)-cropOffsetX, (float)-cropOffsetY)
               * Matrix3x2.CreateScale((float)scale, (float)-scale)
               * Matrix3x2.CreateTranslation(0, (float)(height * scale));
```

The resulting `Matrix3x2` fields are:

| Field | Value |
|-------|-------|
| `M11` | `scale` |
| `M12` | `0` |
| `M21` | `0` |
| `M22` | `-scale` |
| `M31` | `-cropOffsetX * scale` |
| `M32` | `(cropOffsetY + height) * scale` |

In SVG notation (SVG `matrix(a,b,c,d,e,f)` maps to `M11, M12, M21, M22, M31, M32`):

```
matrix(scale, 0, 0, -scale, -cropOffsetX*scale, (cropOffsetY+height)*scale)
```

**Rotation != 0:**

Insert a crop-offset translate, a clockwise rotation step, and a post-rotation translate before the scale/flip. `Matrix3x2.CreateRotation` expects **counter-clockwise radians**, so negate the angle:

```csharp
float rad = -(float)(rotation * Math.PI / 180.0);
float finalHeight = (rotation == 90 || rotation == 270) ? (float)width : (float)height;
(float tx, float ty) = rotation switch {
    90  => (0f,          (float)width),
    180 => ((float)width, (float)height),
    270 => ((float)height, 0f),
    _   => (0f, 0f)
};

Matrix3x2 init = Matrix3x2.CreateTranslation((float)-cropOffsetX, (float)-cropOffsetY)
               * Matrix3x2.CreateRotation(rad)
               * Matrix3x2.CreateTranslation(tx, ty)
               * Matrix3x2.CreateScale((float)scale, (float)-scale)
               * Matrix3x2.CreateTranslation(0, finalHeight * scale);
```

For 90° and 270°, `finalHeight = width` (the unrotated width becomes the rotated canvas height).

### SVG implementation strategy

The SVG target places the initial transform in the root `<g>` element at `BeginPage`. Every subsequent SVG element is emitted inside that group. Path coordinates are emitted verbatim (CTM is already baked in); image elements carry `state.Ctm` in their own `transform` attribute. The root `<g>` converts both to device space transparently.

```xml
<svg xmlns="http://www.w3.org/2000/svg" width="..." height="..." viewBox="...">
  <g transform="matrix(scale,0,0,-scale,-cx*scale,(cy+h)*scale)">
    <!-- all page content — paths, images, clips -->
  </g>
</svg>
```

---

## 3. Per-Member Contract Table

### Must implement (17 members)

| # | Signature | What it must do |
|---|-----------|-----------------|
| 1 | `void BeginPage(int pageNumber, double width, double height, double scale = 1.0, double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)` | Compute and store the initial transform; open an output canvas/surface/document; clear accumulated content from any previous page. `pageNumber` is 1-based. `width`/`height` are CropBox dimensions in PDF units (1/72 in). |
| 2 | `void EndPage()` | Flush the completed page to the backing store; close any open groups. |
| 3 | `void Clear()` | Reset all state and discard accumulated output. Used when switching documents or resetting the renderer. |
| 4 | `int CurrentPageNumber { get; }` | Return the current 1-based page number. Must be updated by `BeginPage`. |
| 5 | `void StrokePath(IPathBuilder path, PdfGraphicsState state)` | Stroke the path outline using `state.ResolvedStrokeColor`, `StrokeAlpha`, `LineWidth`, `LineCap`, `LineJoin`, `MiterLimit`, and `DashPattern`/`DashPhase`. Coordinates are CTM-pre-baked; apply only the initial transform. |
| 6 | `void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)` | Fill the path interior using `state.ResolvedFillColor` and `FillAlpha`. `evenOdd = true` → even-odd fill rule; `false` → non-zero winding. |
| 7 | `void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)` | Fill then stroke in one operation (equivalent to calling `FillPath` then `StrokePath` on the same path). |
| 8 | `void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd, PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)` | Fill the path with a Type 1 tiling pattern. `renderPatternContent` is a callback that drives the pattern's content stream into a secondary target. Minimal implementations may substitute a solid fill from `state.ResolvedFillColor`. |
| 9 | `void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)` | Establish a clipping region. Subsequent path and image operations must be clipped to this region until the next `RestoreState`. Stack-based (mirrors `SaveState`/`RestoreState`). |
| 10 | `void DrawImage(PdfImage image, PdfGraphicsState state)` | Render the image into a 1×1 unit square at the origin, transformed by `state.Ctm`. Image data is stored top-row-first (Y-down), opposite to PDF's Y-up convention — apply a unit-square Y-flip before placing the image. See §6 for full details. |
| 11 | `void SaveState()` | Push the current graphics state onto a stack (mirrors the PDF `q` operator). At minimum, track the current clip depth so `RestoreState` can unwind it. |
| 12 | `void RestoreState()` | Pop the graphics state stack (mirrors the PDF `Q` operator). Unwind any clip groups opened since the matching `SaveState`. |
| 13 | `void ApplyCtm(Matrix3x2 ctm)` | Record `ctm` for use in `DrawImage`. Called by the PDF `cm` operator. **Do not** apply this to path coordinates — paths are already baked. `ctm` equals `state.Ctm` at the point `DrawImage` is called. |
| 14 | `void OnGraphicsStateChanged(PdfGraphicsState state)` | Called when the PDF `gs` operator changes the graphics state via an ExtGState dictionary. Update cached rendering parameters (alpha, blend mode, soft mask presence, etc.) as needed. |
| 15 | `void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)` | Render a soft mask. `maskSubtype` is `"Alpha"` or `"Luminosity"`. `renderMaskContent` drives the mask's content stream into a provided target. Full implementation requires an offscreen surface; minimal targets may stub this as a no-op comment. |
| 16 | `void ClearSoftMask()` | Remove any active soft mask, reverting to normal rendering. |
| 17 | `(int width, int height, double scale) GetPageDimensions()` | Return the rendered page's pixel dimensions and scale factor. Used internally for soft-mask surface allocation. Typical implementation: `((int)(pageWidth * scale), (int)(pageHeight * scale), scale)`. |

### Default no-ops (2 members — implement only if the target supports shadings)

| # | Signature | Default | Notes |
|---|-----------|---------|-------|
| 18 | `void PaintShading(ShadingDescriptor shading, PdfGraphicsState state)` | No-op body in interface | Called by the PDF `sh` operator — paints an axial or radial shading across the current clip region. Targets that render shadings emit gradient fills; others inherit the no-op. |
| 19 | `void FillPathWithShadingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd, ShadingDescriptor shading)` | No-op body in interface | Fill a path with a PatternType 2 (shading) pattern. Same gradient logic as above, scoped to a path. |

---

## 4. Colors

### Reading fill and stroke color

The graphics state carries pre-resolved color in device space:

```csharp
// On PdfGraphicsState:
List<double> ResolvedFillColor;     // Components in [0.0, 1.0]
string ResolvedFillColorSpace;      // "DeviceGray", "DeviceRGB", "DeviceCMYK", "Lab", ...

List<double> ResolvedStrokeColor;
string ResolvedStrokeColorSpace;

double FillAlpha;                   // 0.0 = transparent, 1.0 = opaque  (PDF 'ca' key)
double StrokeAlpha;                 // 0.0–1.0                           (PDF 'CA' key)
```

### Converting to RGB

`PdfLibrary.Rendering.PdfColorToRgb` is a public SkiaSharp-free helper provided for render target authors:

```csharp
// Namespace: PdfLibrary.Rendering
public static class PdfColorToRgb
{
    // Convert device-space components to an (R, G, B) byte triple.
    public static (byte R, byte G, byte B) ToRgb(IReadOnlyList<double> components, string? colorSpace);

    // Clamp an alpha value to [0,1] and scale to [0,255].
    public static byte AlphaByte(double alpha);
}
```

`ToRgb` handles `DeviceGray`, `CalGray`, `DeviceRGB`, `CalRGB`, `DeviceCMYK`, and `Lab` (via `Wacton.Unicolour`, a `PdfLibrary` core dependency). For unknown color spaces it infers the space from component count (CMYK if ≥4, RGB if ≥3, Gray otherwise).

Usage in a fill operation:

```csharp
var (r, g, b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
byte a = PdfColorToRgb.AlphaByte(state.FillAlpha);
```

---

## 5. Paths

### `IPathBuilder.Segments`

The path handed to every path operation exposes its geometry as:

```csharp
IReadOnlyList<PathSegment> Segments { get; }   // on IPathBuilder
```

There are exactly four segment record types (defined in `PdfLibrary.Rendering`):

```csharp
public abstract record PathSegment;
public record MoveToSegment(double X, double Y)                                          : PathSegment;
public record LineToSegment(double X, double Y)                                          : PathSegment;
public record CurveToSegment(double X1, double Y1, double X2, double Y2, double X3, double Y3) : PathSegment;
public record ClosePathSegment                                                            : PathSegment;
```

`CurveToSegment` is a standard cubic Bézier: `(X1,Y1)` and `(X2,Y2)` are the control points; `(X3,Y3)` is the endpoint.

`ClosePathSegment` draws a straight line from the current point back to the start of the subpath and closes it.

Mapping to SVG `d` attribute:

```
MoveToSegment(X, Y)                        → M{X} {Y}
LineToSegment(X, Y)                        → L{X} {Y}
CurveToSegment(X1,Y1, X2,Y2, X3,Y3)       → C{X1} {Y1} {X2} {Y2} {X3} {Y3}
ClosePathSegment                           → Z
```

All coordinates are in CTM-baked PDF user space (Y-UP). No per-segment coordinate math is required in the target — the root `<g>` initial transform (or equivalent) converts everything to device space.

### Fill rules

| `evenOdd` parameter | PDF rule | SVG `fill-rule` |
|---------------------|----------|-----------------|
| `false` | Non-zero winding | `nonzero` |
| `true` | Even-odd | `evenodd` |

### Note on `Rectangle`

`PathBuilder.Rectangle(x, y, w, h)` expands internally to `MoveTo + LineTo × 3 + ClosePath`. The target receives only the expanded segments; there is no rectangle segment type.

---

## 6. Images

### Placement

`DrawImage(PdfImage image, PdfGraphicsState state)` is called to render one image XObject. The image conceptually occupies a **1×1 unit square** at the origin. `state.Ctm` maps that unit square to its position and size in PDF user space.

To place the image correctly:

1. Apply the **unit-square Y-flip**: PDF image data is stored top-row-first (row 0 = top), opposite to PDF's Y-up coordinate convention. Flip Y within the unit square before applying `state.Ctm`.
2. Apply `state.Ctm` to position and size the flipped unit square in PDF user space.
3. The initial transform (root `<g>` or equivalent) converts from PDF user space to device space.

In SVG the complete image transform is:

```xml
<image
  transform="matrix(M11,M12,M21,M22,M31,M32) translate(0,1) scale(1,-1)"
  width="1" height="1" preserveAspectRatio="none"
  href="data:image/jpeg;base64,..."/>
```

where `matrix(...)` is `state.Ctm` (fields M11, M12, M21, M22, M31, M32). The `translate(0,1) scale(1,-1)` suffix performs the unit-square Y-flip. SVG applies transforms right to left: scale first, then translate, then the CTM matrix.

### `PdfImage` API

| Member | Return type | Description |
|--------|-------------|-------------|
| `GetEncodedData()` | `byte[]` | Raw compressed bytes (before filter decoding). Use this to detect JPEG (SOI marker `FF D8 FF`). |
| `GetDecodedData()` | `byte[]` | Decoded pixel bytes in the image's native format (not RGBA). |
| `Width` | `int` | Pixel width |
| `Height` | `int` | Pixel height |
| `BitsPerComponent` | `int` | Bits per sample (1, 4, 8, or 16) |
| `ColorSpace` | `string` | `"DeviceGray"`, `"DeviceRGB"`, `"DeviceCMYK"`, `"Indexed"`, `"ICCBased"`, etc. |
| `IsImageMask` | `bool` | 1-bit stencil mask painted with the current fill color |
| `HasAlpha` | `bool` | True when an SMask or Mask entry is present |

### JPEG fast path

For JPEG images (`GetEncodedData()` bytes begin with `FF D8 FF`) the encoded bytes can be embedded directly without decoding:

```csharp
byte[] encoded = image.GetEncodedData();
bool isJpeg = encoded.Length > 3 && encoded[0] == 0xFF && encoded[1] == 0xD8 && encoded[2] == 0xFF;
if (isJpeg)
{
    string b64 = Convert.ToBase64String(encoded);
    // emit: href="data:image/jpeg;base64,{b64}"
}
```

For non-JPEG images a `PdfImage → RGBA8888` helper is required (see §8 for current scope).

---

## 7. Worked Example — `SvgRenderTarget`

`PdfLibrary.Rendering.Svg.SvgRenderTarget` (~175 lines, zero SkiaSharp dependencies) is the complete reference implementation of `IRenderTarget`. It demonstrates every aspect of the SPI:

- `BeginPage` builds the initial transform and opens the root `<g>` with it.
- `FillPath`/`StrokePath` call `PdfColorToRgb.ToRgb` and emit `<path d="...">` elements directly inside the root group (no coordinate transformation — coordinates are CTM-pre-baked, the root group handles the rest).
- `SetClippingPath` emits `<clipPath id="cN">` and wraps subsequent content in `<g clip-path="url(#cN)">`.
- `SaveState`/`RestoreState` track clip depth via a stack so dangling groups are unwound on restore.
- `DrawImage` embeds JPEG images as base64 data URIs with a `translate(0,1) scale(1,-1)` unit-square Y-flip.
- `ApplyCtm` stores `ctm` in a field for potential use by `DrawImage`; the actual CTM is also readable from `state.Ctm`.
- `FillPathWithTilingPattern`, `RenderSoftMask`, and `PaintShading`/`FillPathWithShadingPattern` are stubbed with comments (see §8).

### Usage

```csharp
using PdfLibrary.Rendering.Svg;

// Single page, scale 1:
string svg = page.RenderToSvg();

// Custom scale (e.g. 2× for higher-resolution export):
string svg = page.RenderToSvg(scale: 2.0);
```

`RenderToSvg` is an extension method on `PdfPage` that constructs an `SvgRenderTarget`, calls `page.Render(target, ...)`, and returns `target.GetSvg()`.

To render to a custom target, use `page.Render(myTarget, pageNumber, scale)` directly.

---

## 8. What the SVG Example Defers

The `SvgRenderTarget` is vector-complete (paths, clips, state stack, color, text-as-outline-paths) and handles JPEG images. The following items are deferred — noted in code comments — but the SPI fully supports them:

| Feature | SPI member(s) | SVG stub |
|---------|---------------|----------|
| **Non-JPEG images (full RGBA)** | `DrawImage` | Emits a translucent gray placeholder rectangle. Full support requires a `PdfImage → RGBA8888` helper (hoisting `~350 lines` from `ImageRenderer.CreateBitmapFromPdfImage`). |
| **Axial/radial shadings** | `PaintShading`, `FillPathWithShadingPattern` | Inherited default no-ops. A full SVG implementation would emit `<linearGradient>` / `<radialGradient>` elements. |
| **Tiling patterns** | `FillPathWithTilingPattern` | Approximated as a solid fill. A full SVG implementation would emit `<pattern>`. |
| **Soft masks** | `RenderSoftMask`, `ClearSoftMask` | Emits a comment and skips the mask. A full SVG implementation would render the mask content to a secondary `SvgRenderTarget`, rasterize it, and apply it as `<mask>`. |

These stubs do not affect vector content (paths, text, fills, strokes, clips), which renders correctly and fully.
