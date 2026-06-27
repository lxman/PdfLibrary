# Plan D2b — WPF Render Target

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A SkiaSharp-free `IRenderTarget` that renders a PDF page into a WPF `DrawingGroup` (retained vector — crisp at any zoom), so the viewer (D3) can drop SkiaSharp. Consumes the D2a core helpers (`PageTransform.Build`, `PdfImageToRgba`).

**Architecture:** New Windows-only package `PdfLibrary.Rendering.Wpf` referencing only core `PdfLibrary`. `WpfRenderTarget` draws into a `System.Windows.Media.DrawingContext` opened from a `DrawingVisual`; paths → `StreamGeometry`, fills/strokes → `Brush`/`Pen`, clips → `PushClip`, images → `PdfImageToRgba` → `BitmapSource`. The finished `DrawingGroup` is exposed for hosting. Structural twin of `SvgRenderTarget`.

**Tech Stack:** C# 12, .NET 8/9/10 **-windows**, WPF (`System.Windows.Media`), `System.Numerics`, xUnit (STA tests).

## Global Constraints

- The package targets `net8.0-windows;net9.0-windows;net10.0-windows` with `<UseWPF>true</UseWPF>`; references **only** `PdfLibrary` core — **no SkiaSharp** (`grep` must stay clean).
- Coordinate contract (same as all targets, see `Docs/RendererSpi.md`): paths arrive CTM-baked in PDF user space (Y-up); the target applies ONLY the page initial transform (from `PageTransform.Build`); `ApplyCtm` is image-only. **Scalar measures are not baked** — scale `LineWidth` and dashes by `ctmScale = sqrt(|Ctm.M11*M22 - M12*M21|)`.
- Glyph/path fills honor even-odd vs nonzero (`StreamGeometry.FillRule`).
- WPF objects (`DrawingContext`, `BitmapSource`, `RenderTargetBitmap`) require an **STA thread**; tests run on STA (a `[StaFact]` attribute or an STA-thread helper) and `Freeze()` bitmaps before cross-thread asserts.
- `PdfImageToRgba` outputs RGBA8888 (R,G,B,A); WPF `Bgra32` wants B,G,R,A → **swap R↔B**. Map `AlphaMode`: `Premultiplied`→`PixelFormats.Pbgra32` (premultiply RGB first), else `PixelFormats.Bgra32`.

## File Structure

- `PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj` (create)
- `PdfLibrary.Rendering.Wpf/WpfRenderTarget.cs` (create) — the `IRenderTarget`.
- `PdfLibrary.Rendering.Wpf/WpfPageExtensions.cs` (create) — `RenderToDrawing(this PdfPage, scale)`.
- `PdfLibrary.Tests/Rendering/Wpf/StaFactAttribute.cs` (create) — STA test attribute (or a shared STA-run helper).
- `PdfLibrary.Tests/Rendering/Wpf/WpfRenderTargetTests.cs` (create)
- Add the new project to the solution (`.slnx`).

---

## Task 1: Project scaffold + lifecycle + STA test harness

**Files:**
- Create: the csproj, `WpfRenderTarget.cs` (lifecycle + state fields only), `StaFactAttribute.cs`, `WpfRenderTargetTests.cs`
- Modify: the solution file

**Background:** Stand up the Windows-only package, the `IRenderTarget` lifecycle (`BeginPage`/`EndPage`/`Clear`/`GetPageDimensions`/`CurrentPageNumber`) producing an empty `DrawingGroup`, and the STA test harness everything else depends on. Path/clip/image members get stubbed (throw `NotImplementedException` or no-op) and filled in later tasks.

**Interfaces:**
- Consumes: `PdfLibrary.Rendering.IRenderTarget`, `PdfLibrary.Rendering.PageTransform.Build`.
- Produces: `public sealed class WpfRenderTarget : IRenderTarget` with `System.Windows.Media.DrawingGroup Drawing { get; }` (the finished output) and `System.Windows.Media.DrawingVisual Visual { get; }` (for `RenderTargetBitmap`); `public sealed class StaFactAttribute : FactAttribute`.

- [ ] **Step 1: Create the csproj**

`PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0-windows;net9.0-windows;net10.0-windows</TargetFrameworks>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PdfLibrary\PdfLibrary.csproj" />
  </ItemGroup>
</Project>
```
Add to the solution: `dotnet sln <the .slnx> add PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj` (confirm the .slnx filename via `ls *.slnx`).

> The `PdfLibrary.Tests` csproj must reference this new project AND be able to run WPF — it must include a `-windows` TFM. Verify `PdfLibrary.Tests` targets `net10.0` only or also `net10.0-windows`; if WPF types aren't available in the test project, add a `net10.0-windows` TFM (or a `<UseWPF>true</UseWPF>` + reference) so the WPF tests compile/run on Windows. Confirm and adjust in this task; if the test project can't host WPF, place the WPF tests in a new `PdfLibrary.Rendering.Wpf.Tests` project instead.

- [ ] **Step 2: STA test attribute**

`PdfLibrary.Tests/Rendering/Wpf/StaFactAttribute.cs` — an xUnit fact that runs on an STA thread (xUnit v2 runs tests on MTA by default; WPF needs STA). Use the simplest working approach for the repo's xUnit version: either the `Xunit.StaFact` package, or a hand-rolled attribute that marshals the test onto an STA thread. Hand-rolled minimal version:
```csharp
using System.Threading;
using Xunit;
using Xunit.Sdk;

namespace PdfLibrary.Tests.Rendering.Wpf;

// Runs the test body on a dedicated STA thread (WPF DrawingContext/BitmapSource require STA).
public sealed class StaFactAttribute : FactAttribute { }
```
with a matching `[XunitTestCaseDiscoverer]` STA executor — OR, simpler and dependency-free, do not use a custom attribute and instead have each test call a helper:
```csharp
internal static class Sta
{
    public static T Run<T>(Func<T> f)
    {
        T result = default!;
        Exception? ex = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception e) { ex = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (ex != null) throw ex;
        return result;
    }
}
```
Prefer the `Sta.Run(...)` helper (no discoverer plumbing). Use it to wrap every WPF interaction in the tests.

- [ ] **Step 3: Write the failing lifecycle test**

`WpfRenderTargetTests.cs`:
```csharp
using System.Windows.Media;
using PdfLibrary.Rendering.Wpf;

namespace PdfLibrary.Tests.Rendering.Wpf;

public class WpfRenderTargetTests
{
    [Fact]
    public void BeginEndPage_ProducesDrawingGroup_WithInitialTransform()
    {
        DrawingGroup dg = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 600, 800, 1.0, 0, 0, 0);
            t.EndPage();
            DrawingGroup d = t.Drawing;
            d.Freeze();
            return d;
        });
        Assert.NotNull(dg);
        Assert.Equal(1, /* page number captured */ 1);
        // The root transform is the page initial transform: PDF (0,0) -> image (0, 800).
        Assert.NotNull(dg.Transform);
        Point mapped = dg.Transform.Value.Transform(new Point(0, 0));
        Assert.Equal(0, mapped.X, 3);
        Assert.Equal(800, mapped.Y, 3);
    }

    [Fact]
    public void GetPageDimensions_ReturnsScaledCropSize()
    {
        (int w, int h, double s) = Sta.Run(() =>
        {
            var t = new WpfRenderTarget();
            t.BeginPage(1, 600, 800, 2.0, 0, 0, 0);
            var d = t.GetPageDimensions();
            t.EndPage();
            return d;
        });
        Assert.Equal(1200, w);
        Assert.Equal(1600, h);
        Assert.Equal(2.0, s);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~WpfRenderTargetTests" --nologo`
Expected: FAIL — `WpfRenderTarget` doesn't exist.

- [ ] **Step 5: Implement the lifecycle**

`WpfRenderTarget.cs` (lifecycle + fields; other members stubbed):
```csharp
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Rendering.Wpf;

/// <summary>
/// A SkiaSharp-free <see cref="IRenderTarget"/> that renders a PDF page into a WPF
/// <see cref="DrawingGroup"/> (retained vector — crisp at any zoom). Paths arrive CTM-baked in
/// PDF user space; the page initial transform (from <see cref="PageTransform"/>) is the root.
/// </summary>
public sealed class WpfRenderTarget : IRenderTarget
{
    private readonly PdfDocument? _document;
    private DrawingVisual _visual = new();
    private DrawingContext? _dc;
    private Matrix _initial = Matrix.Identity;      // page initial transform (WPF Matrix)
    private Matrix3x2 _ctm = Matrix3x2.Identity;
    private int _pushDepth;                          // total DrawingContext.Push() calls (for EndPage balancing)
    private readonly Stack<int> _saveStack = new();  // push-depth at each SaveState
    private (int w, int h, double scale) _dims;

    public WpfRenderTarget(PdfDocument? document = null) => _document = document;

    public DrawingVisual Visual => _visual;
    public DrawingGroup Drawing => _visual.Drawing;   // the retained scene after EndPage
    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
        double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        _visual = new DrawingVisual();
        _saveStack.Clear(); _pushDepth = 0; _ctm = Matrix3x2.Identity;

        double pw = (rotation is 90 or 270 ? height : width) * scale;
        double ph = (rotation is 90 or 270 ? width : height) * scale;
        _dims = ((int)Math.Ceiling(pw), (int)Math.Ceiling(ph), scale);

        Matrix3x2 m = PageTransform.Build(width, height, scale, cropOffsetX, cropOffsetY, rotation);
        _initial = new Matrix(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);

        _dc = _visual.RenderOpen();
        _dc.PushTransform(new MatrixTransform(_initial));   // root: page initial transform
        _pushDepth++;
    }

    public void EndPage()
    {
        if (_dc is null) return;
        while (_pushDepth-- > 0) _dc.Pop();
        _dc.Close();
        _dc = null;
    }

    public void Clear()
    {
        _visual = new DrawingVisual();
        _saveStack.Clear(); _pushDepth = 0; CurrentPageNumber = 0;
    }

    public (int width, int height, double scale) GetPageDimensions() => _dims;

    public void ApplyCtm(Matrix3x2 ctm) => _ctm = ctm;
    public void OnGraphicsStateChanged(PdfGraphicsState state) { }

    // ---- stubbed; later tasks ----
    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }
    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void SaveState() { }
    public void RestoreState() { }
    public void DrawImage(PdfImage image, PdfGraphicsState state) { }
    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent) { }
    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }
    public void ClearSoftMask() { }
}
```
> **Verify-as-you-go:** the EXACT `IRenderTarget` member list and signatures (copy from `PdfLibrary/Rendering/IRenderTarget.cs` — there are 19; `PaintShading`/`FillPathWithShadingPattern` may be default-interface no-ops you can omit, others you must implement). Implement every required member (stub the not-yet ones). Match `PdfGraphicsState` property names by reading the SVG target.

- [ ] **Step 6: Run to verify pass, commit**

Run the lifecycle tests (PASS) then full suite on Windows. Commit:
```bash
git add PdfLibrary.Rendering.Wpf/ PdfLibrary.Tests/Rendering/Wpf/ <the .slnx>
git commit -m "feat(wpf): WpfRenderTarget package scaffold — lifecycle + STA test harness"
```

---

## Task 2: Path operations (fill / stroke / fill+stroke)

**Files:**
- Modify: `WpfRenderTarget.cs` (`FillPath`/`StrokePath`/`FillAndStrokePath` + a `StreamGeometry` builder)
- Test: `WpfRenderTargetTests.cs`

**Background:** Build `StreamGeometry` from `IPathBuilder.Segments`, fill with a `SolidColorBrush` (color via `PdfColorToRgb`, opacity from state alpha), stroke with a `Pen` whose thickness is the **CTM-scaled** `LineWidth` and whose `DashStyle` is the CTM-scaled dashes expressed in **multiples of thickness** (WPF convention).

**Interfaces:**
- Consumes: `PdfColorToRgb.ToRgb(IReadOnlyList<double>, string?)→(byte,byte,byte)`; `state.ResolvedFillColor`/`ResolvedFillColorSpace`/`FillAlpha`/`ResolvedStrokeColor`/`ResolvedStrokeColorSpace`/`StrokeAlpha`/`LineWidth`/`LineCap`/`LineJoin`/`MiterLimit`/`DashPattern`/`DashPhase`/`Ctm`; `path.Segments` (`MoveToSegment`/`LineToSegment`/`CurveToSegment`/`ClosePathSegment`), `path.IsEmpty`.
- Produces: working `FillPath`/`StrokePath`/`FillAndStrokePath`; private `StreamGeometry BuildGeometry(IPathBuilder, bool evenOdd)`.

- [ ] **Step 1: Write the failing test**

Render a filled red rectangle to a `RenderTargetBitmap` and assert a center pixel is red. (RGBA path is unit-square independent — use a path in already-initial-transformed space; simplest is to fill a rectangle in PDF coords and read back.)
```csharp
[Fact]
public void FillPath_RendersFilledRegion()
{
    byte[] px = Sta.Run(() =>
    {
        var t = new WpfRenderTarget();
        t.BeginPage(1, 100, 100, 1.0, 0, 0, 0);
        var pb = new PathBuilder();                    // confirm the concrete IPathBuilder in core
        pb.MoveTo(10, 10); pb.LineTo(90, 10); pb.LineTo(90, 90); pb.LineTo(10, 90); pb.Close();
        var state = TestState.WithFill(new double[]{1,0,0}, "DeviceRGB"); // red; see SVG tests for state setup
        t.FillPath(pb, state, evenOdd: false);
        t.EndPage();
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(100, 100, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(t.Visual);
        var buf = new byte[100 * 100 * 4];
        rtb.CopyPixels(buf, 100 * 4, 0);
        return buf;
    });
    int i = (50 * 100 + 50) * 4;                        // center pixel, BGRA
    Assert.True(px[i + 2] > 200 && px[i + 1] < 60 && px[i] < 60);  // R high, G/B low
}
```
> **Verify-as-you-go:** the concrete `IPathBuilder` implementation name in core (the SVG tests construct one — reuse that), and how the SVG/Skia tests build a `PdfGraphicsState` with a fill color (reuse their helper rather than inventing `TestState`).

- [ ] **Step 2: Run to verify it fails** — `--filter "FullyQualifiedName~WpfRenderTargetTests.FillPath_RendersFilledRegion"`, expect FAIL (empty FillPath stub draws nothing → center not red).

- [ ] **Step 3: Implement**

```csharp
private static StreamGeometry BuildGeometry(IPathBuilder path, bool evenOdd)
{
    var sg = new StreamGeometry { FillRule = evenOdd ? FillRule.EvenOdd : FillRule.Nonzero };
    using StreamGeometryContext ctx = sg.Open();
    bool open = false;
    foreach (PathSegment s in path.Segments)
        switch (s)
        {
            case MoveToSegment m: ctx.BeginFigure(new Point(m.X, m.Y), isFilled: true, isClosed: false); open = true; break;
            case LineToSegment l: ctx.LineTo(new Point(l.X, l.Y), isStroked: true, isSmoothJoin: false); break;
            case CurveToSegment c: ctx.BezierTo(new Point(c.X1, c.Y1), new Point(c.X2, c.Y2), new Point(c.X3, c.Y3), isStroked: true, isSmoothJoin: false); break;
            case ClosePathSegment: /* close current figure */ break;
        }
    return sg;
}
```
(For `ClosePathSegment`: WPF closes a figure via the `isClosed` arg of the NEXT `BeginFigure`, or by not reopening — simplest faithful approach: track and re-`BeginFigure` with `isClosed: true`. Match the SVG target's `Z` semantics; verify by the fill test.)

```csharp
public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
{
    if (path.IsEmpty || _dc is null) return;
    (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
    var brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = state.FillAlpha };
    brush.Freeze();
    _dc.DrawGeometry(brush, null, BuildGeometry(path, evenOdd));
}

public void StrokePath(IPathBuilder path, PdfGraphicsState state)
{
    if (path.IsEmpty || _dc is null) return;
    (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
    double ctmScale = Math.Sqrt(Math.Abs(state.Ctm.M11 * state.Ctm.M22 - state.Ctm.M12 * state.Ctm.M21));
    double width = Math.Max(state.LineWidth * ctmScale, 0.1);
    var brush = new SolidColorBrush(Color.FromRgb(r, g, b)) { Opacity = state.StrokeAlpha }; brush.Freeze();
    var pen = new Pen(brush, width)
    {
        StartLineCap = Cap(state.LineCap), EndLineCap = Cap(state.LineCap),
        LineJoin = Join(state.LineJoin), MiterLimit = state.MiterLimit
    };
    if (state.DashPattern is { Length: > 0 })
        // WPF dashes are multiples of thickness; PDF dashes are user-space → CTM-scale then divide by width.
        pen.DashStyle = new DashStyle(state.DashPattern.Select(d => d * ctmScale / width), state.DashPhase * ctmScale / width);
    pen.Freeze();
    _dc.DrawGeometry(null, pen, BuildGeometry(path, evenOdd: false));
}

public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
{ FillPath(path, state, evenOdd); StrokePath(path, state); }

private static PenLineCap Cap(int c) => c switch { 1 => PenLineCap.Round, 2 => PenLineCap.Square, _ => PenLineCap.Flat };
private static PenLineJoin Join(int j) => j switch { 1 => PenLineJoin.Round, 2 => PenLineJoin.Bevel, _ => PenLineJoin.Miter };
```

- [ ] **Step 4: Run to verify pass** (fill test green; add a stroke-width test asserting a thin line under a scaling CTM is the CTM-scaled thickness, mirroring the SVG `StrokePath_ScalesLineWidthByCtm` test). Full suite.

- [ ] **Step 5: Commit** — `feat(wpf): path fill/stroke with CTM-scaled width + even-odd`

---

## Task 3: Clipping + save/restore + CTM

**Files:** Modify `WpfRenderTarget.cs` (`SetClippingPath`/`SaveState`/`RestoreState`); Test: `WpfRenderTargetTests.cs`

**Background:** Clip via `DrawingContext.PushClip(Geometry)` (counts as a push); `SaveState` records the current push depth; `RestoreState` pops back to it. (The root initial transform is pushed once in `BeginPage` and popped in `EndPage`; clips push/pop within.)

**Interfaces:** Consumes the push-depth fields from Task 1. Produces working clip + state nesting.

- [ ] **Step 1: Failing test** — fill a 100×100 region but first clip to the left half; assert a right-half pixel is background (unpainted) and a left-half pixel is filled. Wrap in `Sta.Run`.

- [ ] **Step 2: Verify fail** (stubs no-op → whole region filled → right-half pixel also filled).

- [ ] **Step 3: Implement**
```csharp
public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
{
    if (path.IsEmpty || _dc is null) return;
    _dc.PushClip(BuildGeometry(path, evenOdd));
    _pushDepth++;
}

public void SaveState() => _saveStack.Push(_pushDepth);

public void RestoreState()
{
    if (_dc is null || _saveStack.Count == 0) return;
    int target = _saveStack.Pop();
    while (_pushDepth > target) { _dc.Pop(); _pushDepth--; }
}
```
> Note: PDF `q`/`Q` also save/restore the CTM and graphics state, but in this geometry SPI paths arrive pre-baked and `ApplyCtm` tracks `_ctm` for images only — so `SaveState`/`RestoreState` here manage only the WPF push stack (clips). Confirm this matches how the SVG target handles it (it pushes/pops `<g clip-path>` groups by depth — same model).

- [ ] **Step 4: Verify pass** + full suite. **Step 5: Commit** — `feat(wpf): clipping + save/restore push-stack`

---

## Task 4: Image drawing

**Files:** Modify `WpfRenderTarget.cs` (`DrawImage`); Test: `WpfRenderTargetTests.cs`

**Background:** Decode via `PdfImageToRgba.ToRgba` → RGBA8888; swap R↔B → BGRA; build a `BitmapSource`; draw in the unit square with Y-flip + `state.Ctm` (same placement as the SVG/Skia targets). Map `AlphaMode` → pixel format.

**Interfaces:** Consumes `PdfImageToRgba.ToRgba(image, _document, …)→RgbaImage?{Rgba,Width,Height,Alpha}`, `AlphaMode`, `state.Ctm`. Produces working `DrawImage`.

- [ ] **Step 1: Failing test** — build/load a PDF with a small known image (reuse an image fixture from the existing image-render tests, or a tiny solid-color image), render via `WpfRenderTarget`, assert a pixel matches the image color (BGRA). Wrap in `Sta.Run`.
> Verify-as-you-go: how to obtain a `PdfImage` in a test (the existing `ImageSamplingTests`/`Jp2CmykRenderTests` load one — reuse that fixture + access path).

- [ ] **Step 2: Verify fail** (empty DrawImage → no image pixels).

- [ ] **Step 3: Implement**
```csharp
public void DrawImage(PdfImage image, PdfGraphicsState state)
{
    if (_dc is null) return;
    PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, _document);
    if (decoded is not { } img || img.Rgba.Length == 0) return;

    bool premul = img.Alpha == AlphaMode.Premultiplied;
    byte[] bgra = new byte[img.Rgba.Length];
    for (int i = 0; i < img.Rgba.Length; i += 4)
    {
        byte r = img.Rgba[i], g = img.Rgba[i + 1], b = img.Rgba[i + 2], a = img.Rgba[i + 3];
        if (premul) { r = (byte)(r * a / 255); g = (byte)(g * a / 255); b = (byte)(b * a / 255); }
        bgra[i] = b; bgra[i + 1] = g; bgra[i + 2] = r; bgra[i + 3] = a;
    }
    PixelFormat fmt = premul ? PixelFormats.Pbgra32 : PixelFormats.Bgra32;
    var bs = System.Windows.Media.Imaging.BitmapSource.Create(
        img.Width, img.Height, 96, 96, fmt, null, bgra, img.Width * 4);
    bs.Freeze();

    // Unit-square placement: flip Y (PDF image rows top-first), then state.Ctm; root applies initial transform.
    Matrix3x2 c = state.Ctm;
    var m = new Matrix(c.M11, c.M12, c.M21, c.M22, c.M31, c.M32);
    _dc.PushTransform(new MatrixTransform(m));
    _dc.PushTransform(new MatrixTransform(1, 0, 0, -1, 0, 1));   // translate(0,1) scale(1,-1)
    _dc.DrawImage(bs, new Rect(0, 0, 1, 1));
    _dc.Pop(); _dc.Pop();
}
```

- [ ] **Step 4: Verify pass** + full suite. **Step 5: Commit** — `feat(wpf): DrawImage via PdfImageToRgba (RGBA->BGRA, AlphaMode->pixel format)`

---

## Task 5: Approximated members + `RenderToDrawing` extension

**Files:** Modify `WpfRenderTarget.cs` (tiling pattern / soft mask / any shading members); Create `WpfPageExtensions.cs`; Test: `WpfRenderTargetTests.cs`

**Background:** Match the SVG target's approximations: tiling pattern → solid fill (`FillPath`); soft mask → render unmasked (no-op `RenderSoftMask`/`ClearSoftMask`); shading members (if present and not default no-ops) → no-op or a single gradient fill. Add the page convenience that runs the renderer over a page.

**Interfaces:** Produces `public static DrawingGroup RenderToDrawing(this PdfPage page, double scale = 1.0)` (and/or `(PdfDocument, pageIndex)`), mirroring `SvgPageExtensions.RenderToSvg`.

- [ ] **Step 1: Failing test** — `page.RenderToDrawing(1.0)` on a simple built PDF returns a non-null, non-empty `DrawingGroup`; assert via `Sta.Run`. (Reuse the page-rendering harness the SVG `RenderToSvg` test uses — confirm how it drives the content processor over a page.)

- [ ] **Step 2: Verify fail.**

- [ ] **Step 3: Implement** `FillPathWithTilingPattern` → `FillPath(path, state, evenOdd)`; `RenderSoftMask`/`ClearSoftMask` no-ops; and `WpfPageExtensions.RenderToDrawing` — copy the SVG extension's structure (create the target, run the same page→content-processor pipeline `RenderToSvg` uses, return `target.Drawing`).
> Verify-as-you-go: read `PdfLibrary.Rendering.Svg/SvgPageExtensions.cs` for the exact pipeline call (the content processor / renderer entry point that walks the page into an `IRenderTarget`) and replicate it for WPF.

- [ ] **Step 4: Verify pass** + full suite. **Step 5: Commit** — `feat(wpf): tiling/softmask approximations + RenderToDrawing page extension`

---

## Task 6: Verification

**Files:** none.

- [ ] **Step 1: Build + test + Skia-free**

Run (on Windows): `dotnet build PdfLibrary.Rendering.Wpf/PdfLibrary.Rendering.Wpf.csproj -c Release --nologo` (0W/0E); `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo` (PASS, incl. all WPF tests); `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary.Rendering.Wpf --include=*.cs` (no output).

- [ ] **Step 2: No commit.**

## Self-Review Notes

- **Spec coverage:** delivers Component 2's render target (`PdfLibrary.Rendering.Wpf`), consuming D2a (`PageTransform`, `PdfImageToRgba`/`AlphaMode`). The vector `DrawingGroup`/`DrawingVisual` is the artifact D3's viewer hosts.
- **Coordinate + scalar-measure contract** carried from the renderer lessons (CTM-scaled stroke width/dashes; even-odd; image Y-flip + CTM; initial transform via `PageTransform`).
- **Windows-only** by nature (WPF). Tests run STA on Windows; cross-platform CI runs everything else as before.

## Out of scope → Plan D3

- Rewire `PdfLibrary.Wpf.Viewer` onto `WpfRenderTarget` (host the `DrawingVisual`, drop the Skia ref), native form-control overlay via D1 (`PdfFieldWidget`/`PageGeometry`), write-back + Save/Flatten, drop `Generator`'s Skia dep.
