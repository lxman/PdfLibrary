# Plan D2a — Shared Core: PageTransform + PdfImage→RGBA Hoist

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the two SkiaSharp-free pieces a WPF (or any non-Skia raster) render target needs, into core `PdfLibrary`: a shared `PageTransform.Build` (the page initial-transform matrix, unifying 3 existing copies) and `PdfImageToRgba.ToRgba` (the PdfImage→RGBA pixel decode, hoisted out of the SkiaSharp `ImageRenderer`). The SkiaSharp `ImageRenderer` is then refactored to delegate to the hoist, so the existing image-render tests prove the extraction is faithful.

**Architecture:** Both pieces move to `PdfLibrary/Rendering/` (core, cross-platform, SkiaSharp-free). `PageTransform.Build` replaces the inline matrix in `SkiaSharpRenderTarget.BeginPage`, `SvgRenderTarget`, and `PdfPage.GetGeometry`. `PdfImageToRgba.ToRgba` contains the ~680 lines of pure pixel math from `ImageRenderer.CreateBitmapFromPdfImage` (10 color-space branches + SMask), returning an RGBA8888 byte buffer; `CreateBitmapFromPdfImage` keeps only the ~4-line SkiaSharp glue (wrap the bytes in an `SKBitmap`). No rendered output changes — that is the gate.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, `System.Buffers` (ArrayPool), xUnit.

## Global Constraints

- Core `PdfLibrary` stays SkiaSharp-free; multi-target net8.0/9.0/10.0. `PageTransform` + `PdfImageToRgba` are `public static` in namespace `PdfLibrary.Rendering`.
- **The gate for the hoist is pixel-fidelity:** after `ImageRenderer` delegates to `PdfImageToRgba`, every existing image-render test must stay green (byte-for-byte / within the existing tolerant thresholds). A divergence = a faithful-extraction bug.
- `PdfImageToRgba` output: **RGBA8888, R,G,B,A byte order, top-first rows** (matching the current `SKColorType.Rgba8888` buffer the loops already build); plus an `isPremul` flag (true only for image-mask stencils). Inherits the current limitations (no 16bpc, no Lab → caller falls back / draws nothing) — document in the XML summary.
- SMask is read from `image.Stream.Dictionary` (internal to `PdfLibrary` — the core hoist has access; do NOT add a public member).

## File Structure

- `PdfLibrary/Rendering/PageTransform.cs` (create) — `Build(...)→Matrix3x2`.
- `PdfLibrary/Document/PdfPage.cs`, `PdfLibrary.Rendering.SkiaSharp/SkiaSharpRenderTarget.cs`, `PdfLibrary.Rendering.Svg/SvgRenderTarget.cs` (modify) — call `PageTransform.Build`.
- `PdfLibrary/Rendering/PdfImageToRgba.cs` (create) — `ToRgba(...)` + the moved pixel math.
- `PdfLibrary.Rendering.SkiaSharp/Rendering/ImageRenderer.cs` (modify) — `CreateBitmapFromPdfImage` delegates to `ToRgba`.
- Test: `PdfLibrary.Tests/Rendering/PageTransformTests.cs`.

---

## Task 1: `PageTransform.Build` (unify the initial-transform matrix)

**Files:**
- Create: `PdfLibrary/Rendering/PageTransform.cs`
- Modify: `PdfLibrary/Document/PdfPage.cs` (`GetGeometry`), `PdfLibrary.Rendering.SkiaSharp/SkiaSharpRenderTarget.cs` (`BeginPage`), `PdfLibrary.Rendering.Svg/SvgRenderTarget.cs` (`InitialTransform`/`BeginPage`)
- Test: `PdfLibrary.Tests/Rendering/PageTransformTests.cs`

**Background:** The page initial transform (PDF user space Y-up → image pixels Y-down, with crop offset, scale, rotation) is built identically in 3 places (verified token-identical except SkiaSharp's mathematically-equivalent rotation-0 fast path). Extract one helper; point all callers at it. Existing render tests + `PageGeometryTests` validate (no behavior change).

**Interfaces:**
- Produces: `public static class PageTransform` with `static Matrix3x2 Build(double width, double height, double scale, double cropX, double cropY, int rotation)`.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/PageTransformTests.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PageTransformTests
{
    [Fact]
    public void Build_RotationZero_MatchesKnownMatrix()
    {
        // width=600,height=800,scale=2,crop=(0,0): matrix(2,0,0,-2, 0, 800*2)
        Matrix3x2 m = PageTransform.Build(600, 800, 2.0, 0, 0, 0);
        Assert.Equal(2, m.M11, 4);
        Assert.Equal(0, m.M12, 4);
        Assert.Equal(0, m.M21, 4);
        Assert.Equal(-2, m.M22, 4);
        Assert.Equal(0, m.M31, 4);
        Assert.Equal(1600, m.M32, 4);
        // PDF (0,0) -> image (0, 1600) (bottom).
        Vector2 o = Vector2.Transform(Vector2.Zero, m);
        Assert.Equal(0, o.X, 3); Assert.Equal(1600, o.Y, 3);
    }

    [Fact]
    public void Build_CropOffset_TranslatesOrigin()
    {
        Matrix3x2 m = PageTransform.Build(600, 800, 1.0, 10, 20, 0);
        Assert.Equal(-10, m.M31, 4);          // -cropX*scale
        Assert.Equal(800 + 20, m.M32, 4);      // (cropY+height)*scale
    }

    [Fact]
    public void Build_Rotation90_SwapsExtents()
    {
        // For 90°, finalHeight = width; verify it doesn't throw and is invertible.
        Matrix3x2 m = PageTransform.Build(600, 800, 1.0, 0, 0, 90);
        Assert.True(Matrix3x2.Invert(m, out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PageTransformTests"`
Expected: FAIL — `PageTransform` doesn't exist.

- [ ] **Step 3: Implement + unify callers**

Create `PdfLibrary/Rendering/PageTransform.cs`:

```csharp
using System.Numerics;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds the page initial transform: PDF user space (Y-up, points) → rendered-image pixels
/// (Y-down, top-left origin) at a scale, accounting for crop-box offset and page rotation.
/// Single source of truth shared by the render targets and <see cref="PdfLibrary.Document.PdfPage.GetGeometry"/>.
/// </summary>
public static class PageTransform
{
    /// <param name="width">CropBox width (PDF points).</param>
    /// <param name="height">CropBox height (PDF points).</param>
    /// <param name="scale">Render scale (1.0 = 72 DPI).</param>
    /// <param name="cropX">CropBox lower-left X (offset from MediaBox origin).</param>
    /// <param name="cropY">CropBox lower-left Y.</param>
    /// <param name="rotation">Page rotation, normalized 0/90/180/270.</param>
    public static Matrix3x2 Build(double width, double height, double scale,
        double cropX, double cropY, int rotation)
    {
        double finalHeight = rotation is 90 or 270 ? width : height;
        (float tx, float ty) = rotation switch
        {
            90 => (0f, (float)width),
            180 => ((float)width, (float)height),
            270 => ((float)height, 0f),
            _ => (0f, 0f)
        };
        var rad = (float)(-rotation * Math.PI / 180.0);
        return Matrix3x2.CreateTranslation((float)-cropX, (float)-cropY)
             * Matrix3x2.CreateRotation(rad)
             * Matrix3x2.CreateTranslation(tx, ty)
             * Matrix3x2.CreateScale((float)scale, (float)-scale)
             * Matrix3x2.CreateTranslation(0f, (float)(finalHeight * scale));
    }
}
```

Then replace the inline matrix in all three callers with `PageTransform.Build(...)`:
- `PdfPage.GetGeometry` (the `Matrix3x2 m = ...CreateTranslation...` block) → `Matrix3x2 m = PageTransform.Build(width, height, scale, cropX, cropY, rotation);` (keep the `pw`/`ph`/`PageGeometry` construction).
- `SvgRenderTarget`: replace the private `InitialTransform(...)` body's content with `return PageTransform.Build(width, height, scale, cropX, cropY, rotation);` (or call `PageTransform.Build` directly at its one call site and delete the private helper).
- `SkiaSharpRenderTarget.BeginPage`: replace the rotation-branching `initialTransform = ...` (both the rotation-0 fast path and the rotation!=0 block) with `Matrix3x2 initialTransform = PageTransform.Build(width, height, scale, cropOffsetX, cropOffsetY, rotation);` (keep everything that consumes `initialTransform`).

- [ ] **Step 4: Run tests to verify green (incl. existing render/geometry tests — no behavior change)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PageTransformTests"` (PASS), then `--filter "FullyQualifiedName~PdfLibrary.Tests.Rendering&Category!=LocalOnly"` and `--filter "FullyQualifiedName~PageGeometryTests"` (PASS — the unify must not change any rendered output or geometry).

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/PageTransform.cs PdfLibrary/Document/PdfPage.cs PdfLibrary.Rendering.SkiaSharp/SkiaSharpRenderTarget.cs PdfLibrary.Rendering.Svg/SvgRenderTarget.cs PdfLibrary.Tests/Rendering/PageTransformTests.cs
git commit -m "refactor(render): unify page initial-transform into shared PageTransform.Build"
```

---

## Task 2: `PdfImageToRgba` hoist + `ImageRenderer` delegation

**Files:**
- Create: `PdfLibrary/Rendering/PdfImageToRgba.cs`
- Modify: `PdfLibrary.Rendering.SkiaSharp/Rendering/ImageRenderer.cs` (`CreateBitmapFromPdfImage` delegates; consolidate `ConvertRawBytesToSkBitmap`'s CMYK)

**Background:** Move the pixel-decode math out of `ImageRenderer.CreateBitmapFromPdfImage` (lines 172–994, ≈680 lines of pure math) into a core, SkiaSharp-free `PdfImageToRgba.ToRgba`. This is an **extraction** — the per-pixel arithmetic is copied verbatim; only the output target changes from an `SKBitmap` (via `GetPixels`/`Marshal.Copy`) to an RGBA `byte[]`. Then `CreateBitmapFromPdfImage` calls `ToRgba` and wraps the returned bytes in an `SKBitmap` (the ~4-line glue). The existing image-render tests are the fidelity gate.

**Interfaces:**
- Consumes (all in `PdfLibrary`, accessible from core): `PdfImage` (`GetDecodedData`/`Width`/`Height`/`BitsPerComponent`/`ColorSpace`/`ColorSpaceArray`(internal)/`DecodeArray`/`IsImageMask`/`GetIndexedPalette`/`HasAlpha`/`Stream`(internal)); `IccColorConverter.TryConvertInterleavedToSrgb`; `CalRgbConverter.FromCalRgbArray`/`.ToSrgb`.
- Produces: `public static class PdfImageToRgba` with
  `static RgbaImage? ToRgba(PdfImage image, PdfDocument? doc, (byte R, byte G, byte B)? imageMaskColor = null, bool blackPointCompensation = false, string? renderingIntent = null)`
  where `public readonly record struct RgbaImage(byte[] Rgba, int Width, int Height, bool IsPremultiplied)`. Returns `null` for the unsupported cases the current code returns null for (16bpc, Lab, unknown).

- [ ] **Step 1: Create `PdfImageToRgba` by moving the pixel math**

Create `PdfLibrary/Rendering/PdfImageToRgba.cs` with `RgbaImage` + `ToRgba`. Move ALL of `CreateBitmapFromPdfImage`'s logic EXCEPT the SkiaSharp glue:
- The SMask preamble (lines ~237–283): read `/SMask` off `image.Stream.Dictionary`, decode to `smaskData byte[]?` (verbatim).
- All 10 branches (the table in `.superpowers/sdd/d2-map.md §1.1`): JPX, ImageMask, Indexed, DeviceRGB/CalRGB, DeviceGray 1bpc, DeviceGray 8bpc, ICCBased 1/3/4-comp, DeviceCMYK. Each branch currently builds a `pixelBuffer`/writes into the bitmap pointer — instead, build/return a `byte[] rgba` (RGBA8888, top-first), the SAME bytes the loops already produce.
- Replace each branch's `new SKBitmap(...)` + `GetPixels()` + `Marshal.Copy(...)` + `NotifyPixelsChanged()` with: allocate the `byte[] rgba` (`new byte[w*h*4]`) and write the loop output directly into it; `return new RgbaImage(rgba, w, h, isPremul)`.
- `imageMaskColor`: the SKColor param becomes `(byte R,byte G,byte B)?` — branch 2 uses it as the stencil paint color.
- Move the helpers `CalibrateCalRgbBuffer` and the CMYK→RGB formula; consolidate the duplicate CMYK from `ConvertRawBytesToSkBitmap` (map §4.3) into one private `static (byte,byte,byte) CmykToRgb(byte c,byte m,byte y,byte k)` used by both the CMYK branch and the JPX path.
- Carry the XML summary noting unsupported: 16bpc, Lab → returns `null`.

(This is a verbatim math move — do not "improve" the per-pixel arithmetic; faithfulness is verified in Step 3.)

- [ ] **Step 2: Make `ImageRenderer.CreateBitmapFromPdfImage` delegate**

In `ImageRenderer.cs`, replace the body of `CreateBitmapFromPdfImage` with:

```csharp
private SKBitmap? CreateBitmapFromPdfImage(PdfImage image, SKColor? imageMaskColor = null,
    bool blackPointCompensation = false, string? renderingIntent = null)
{
    (byte, byte, byte)? maskColor = imageMaskColor is { } c ? (c.Red, c.Green, c.Blue) : null;
    PdfImageToRgba.RgbaImage? r = PdfImageToRgba.ToRgba(image, _document, maskColor, blackPointCompensation, renderingIntent);
    if (r is not { } img) return null;

    var info = new SKImageInfo(img.Width, img.Height, SKColorType.Rgba8888,
        img.IsPremultiplied ? SKAlphaType.Premul : SKAlphaType.Unpremul);
    var bitmap = new SKBitmap(info);
    System.Runtime.InteropServices.Marshal.Copy(img.Rgba, 0, bitmap.GetPixels(), img.Rgba.Length);
    bitmap.NotifyPixelsChanged();
    return bitmap;
}
```
(Use the renderer's existing `PdfDocument` field for `doc` — confirm its name. Keep `ConvertRawBytesToSkBitmap` only if still referenced elsewhere; otherwise delete it since its math moved. Remove now-dead helpers/usings; the file should still compile with no unused-warning.)

> **Verify-as-you-go:** the exact `_document`/doc field name on `ImageRenderer`; whether `Unpremul` vs `Opaque` matters for the no-SMask branches (the original chose `Opaque` for those — preserve per-branch alpha-type by having `ToRgba` also return the intended `SKAlphaType` equivalent, OR keep it simple: `Unpremul` is visually identical to `Opaque` when all alpha=255; if a render test regresses on alpha, thread an explicit alpha-type. Prefer matching the original's per-branch alpha type — return an enum/flag if needed rather than guessing).

- [ ] **Step 3: The fidelity gate — existing image-render tests stay pixel-identical**

Run the image-bearing render tests (these load real PDFs, rasterize images via `CreateBitmapFromPdfImage`, and compare):
`dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~ImageSamplingTests|FullyQualifiedName~DctDecodeCmykTests|FullyQualifiedName~Jp2CmykRenderTests|FullyQualifiedName~BlendModeTests|FullyQualifiedName~FormXObjectAlphaTests|FullyQualifiedName~SkiaSharpRenderPipelineTests" --nologo`
Expected: PASS, byte-for-byte where they assert pixels. **If any fails, the extraction diverged — diff the failing branch's math against the original; do NOT weaken the test.** Likely culprits: a dropped Decode-array invert, wrong alpha type, an off-by-one in row stride, or the CMYK consolidation changing rounding.

- [ ] **Step 4: Commit**

```bash
git add PdfLibrary/Rendering/PdfImageToRgba.cs PdfLibrary.Rendering.SkiaSharp/Rendering/ImageRenderer.cs
git commit -m "refactor(images): hoist PdfImage->RGBA into core PdfImageToRgba; ImageRenderer delegates"
```

---

## Task 3: Verification

**Files:** none.

- [ ] **Step 1: Full suite + Skia-free core + Release**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo` (PASS — esp. all render/image tests); `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/` (no output — `PdfImageToRgba` + `PageTransform` are SkiaSharp-free); `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo` and `dotnet build PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj -c Release --nologo` (0W/0E each).

- [ ] **Step 2: No commit.**

## Self-Review Notes

- **Spec coverage:** delivers the cross-platform prerequisites for the WPF target (D2b): the shared page transform and the SkiaSharp-free image decode. `PdfImageToRgba` also unblocks non-JPEG images in the SVG target later (deferred there).
- **Faithfulness gate:** the image-render tests staying pixel-identical after `ImageRenderer` delegates is the proof the ~680-line extraction is correct — the same "no rendered-output change" gate used for the B3b text flip.
- **No new public-API risk beyond two static helpers** (`PageTransform`, `PdfImageToRgba` + `RgbaImage`), both plain data/transform utilities.

## Out of scope → Plan D2b

- `PdfLibrary.Rendering.Wpf` (`WpfRenderTarget`): `IRenderTarget` → `DrawingContext`/`StreamGeometry`/`Pen`; images via `PdfImageToRgba` → `BitmapSource` with the **RGBA→BGRA byte swap** + `PixelFormats.Bgra32`/`Pbgra32`; WPF `Pen.DashStyle` unitless-of-thickness conversion; `net*-windows` + `<UseWPF>`; STA-thread (`RenderTargetBitmap`) tests. Consumes `PageTransform.Build` for its root transform.
