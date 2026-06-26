# Renderer SPI — Plan C: SVG Export Target, Adapter Slim, SPI Docs

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the geometry-only `IRenderTarget` SPI is genuinely renderer-agnostic by building a **SkiaSharp-free SVG export target** in its own package, slim the SkiaSharp adapter, and document the SPI for third-party implementers.

**Architecture:** A new `PdfLibrary.Rendering.Svg` package implements `IRenderTarget` by emitting SVG — paths become `<path d=…>`, the page's initial transform becomes the root `<g transform="matrix(…)">`, clipping becomes nested `<clipPath>`/`<g clip-path>`, and images embed as data URIs. It depends only on `PdfLibrary` core (no SkiaSharp), demonstrating an independent backend. A small core `PdfColorToRgb` helper makes color resolution available to any target. The SkiaSharp adapter drops its now-dead `Microsoft.Extensions.Caching.Memory` dependency.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, `Wacton.Unicolour` (existing core dep, for Lab), xUnit.

## Global Constraints

- **`PdfLibrary.Rendering.Svg` is SkiaSharp-free** (no `SkiaSharp` package ref, no `using SkiaSharp`) — that is the whole point. It references only `PdfLibrary`. Multi-target net8.0/9.0/10.0.
- **Coordinate contract:** paths arrive at `FillPath`/`StrokePath`/`SetClippingPath` in **CTM-baked PDF user space (Y-up)**. The target applies ONLY the page **initial transform** (Y-flip + render scale + crop offset + rotation), expressed as the root `<g transform>`. Do NOT re-apply the CTM to paths. `ApplyCtm` only matters for `DrawImage` (track `_currentCtm`; images use `state.Ctm`).
- **Initial transform (rotation 0):** `matrix(scale, 0, 0, -scale, -cropOffsetX*scale, (cropOffsetY+height)*scale)`. Rotation 90/180/270 insert a rotation + translation (see Task 3).
- All numbers formatted with `CultureInfo.InvariantCulture` (SVG uses `.` decimals).
- Existing SkiaSharp render tests stay green (the adapter slim must not change rendering).

## File Structure

- `PdfLibrary/Rendering/PdfColorToRgb.cs` (create) — Skia-free `(List<double>, string) → (byte,byte,byte)`.
- `PdfLibrary.Rendering.Svg/PdfLibrary.Rendering.Svg.csproj` (create) — new package, refs `PdfLibrary`.
- `PdfLibrary.Rendering.Svg/SvgRenderTarget.cs` (create) — the `IRenderTarget`.
- `PdfLibrary.Rendering.Svg/SvgPageExtensions.cs` (create) — `page.RenderToSvg()` convenience.
- `PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj` (modify) — drop dead dep.
- `Docs/RendererSpi.md` (create) — SPI implementer guide.
- Tests: `PdfLibrary.Tests/Rendering/PdfColorToRgbTests.cs`, `PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs`.

---

## Task 1: Slim the SkiaSharp adapter

**Files:** Modify `PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj`

**Background:** `Microsoft.Extensions.Caching.Memory` was the deleted `TextRenderer`'s glyph-cache dependency. Grep confirms zero `MemoryCache`/`IMemoryCache` uses remain in the adapter (B3c map §6). Remove the `<PackageReference>`.

- [ ] **Step 1: Confirm it's dead, then remove**

Run: `grep -rn "MemoryCache\|IMemoryCache\|Microsoft.Extensions.Caching" PdfLibrary.Rendering.SkiaSharp --include=*.cs | grep -v /obj/`
Expected: no output. Then delete the `<PackageReference Include="Microsoft.Extensions.Caching.Memory" ... />` line from the csproj.

- [ ] **Step 2: Build + test the adapter**

Run: `dotnet build PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj -c Release --nologo` (expect 0W/0E) and `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLibrary.Tests.Rendering&Category!=LocalOnly" --nologo` (expect PASS — rendering unchanged).

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj
git commit -m "build(skia): drop dead Microsoft.Extensions.Caching.Memory dep (was TextRenderer's, deleted in B3c)"
```

---

## Task 2: Core `PdfColorToRgb` helper

**Files:**
- Create: `PdfLibrary/Rendering/PdfColorToRgb.cs`
- Test: `PdfLibrary.Tests/Rendering/PdfColorToRgbTests.cs`

**Background:** Render targets read `state.ResolvedFillColor` (a `List<double>` of 0–1 components) + `state.ResolvedFillColorSpace` (a name) and need an RGB triple. The SkiaSharp `ColorConverter` does this with pure math (returns `SKColor`); this is the Skia-free core equivalent so any target shares it. Formulas from B3c/C map.

**Interfaces:**
- Produces: `public static class PdfColorToRgb` with `static (byte R, byte G, byte B) ToRgb(IReadOnlyList<double> components, string? colorSpace)` and `static byte AlphaByte(double alpha)`. **Public** — it's a reusable helper for third-party target authors (the SVG package is a separate assembly and needs it), part of the 2.0 SPI surface.

- [ ] **Step 1: Write the failing test**

```csharp
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PdfColorToRgbTests
{
    [Fact] public void Gray() => Assert.Equal(((byte)128,(byte)128,(byte)128),
        Round(PdfColorToRgb.ToRgb([0.5], "DeviceGray")));

    [Fact] public void Rgb() => Assert.Equal(((byte)255,(byte)0,(byte)0),
        PdfColorToRgb.ToRgb([1.0, 0.0, 0.0], "DeviceRGB"));

    [Fact] public void Cmyk_PureCyan() // c=1,m=0,y=0,k=0 → (0,255,255)
        => Assert.Equal(((byte)0,(byte)255,(byte)255), PdfColorToRgb.ToRgb([1,0,0,0], "DeviceCMYK"));

    [Fact] public void Cmyk_Black() // k=1 → (0,0,0)
        => Assert.Equal(((byte)0,(byte)0,(byte)0), PdfColorToRgb.ToRgb([0,0,0,1], "DeviceCMYK"));

    [Fact] public void UnknownThreeComps_TreatedAsRgb()
        => Assert.Equal(((byte)0,(byte)255,(byte)0), PdfColorToRgb.ToRgb([0,1,0], "Separation"));

    [Fact] public void Empty_DefaultsBlack()
        => Assert.Equal(((byte)0,(byte)0,(byte)0), PdfColorToRgb.ToRgb([], "DeviceRGB"));

    [Fact] public void Alpha() => Assert.Equal((byte)128, PdfColorToRgb.AlphaByte(0.5));

    private static (byte,byte,byte) Round((byte r, byte g, byte b) c) => (c.r, c.g, c.b);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfColorToRgbTests"`
Expected: FAIL — `PdfColorToRgb` does not exist. (`0.5*255 = 127.5` → confirm whether `ToRgb` rounds or truncates; the test expects 128 = rounding. Implement with `Math.Round`. If you prefer truncation to match `ColorConverter` exactly, read `ColorConverter.cs:19-125` and align the test to it — pick one and be consistent.)

- [ ] **Step 3: Implement**

Create `PdfLibrary/Rendering/PdfColorToRgb.cs`:

```csharp
namespace PdfLibrary.Rendering;

/// <summary>
/// SkiaSharp-free resolution of a PDF device-color component list to an RGB triple, for render
/// targets. Mirrors the SkiaSharp ColorConverter math; Lab uses Wacton.Unicolour (a core dep).
/// Public so third-party IRenderTarget authors (in other assemblies) can reuse it.
/// </summary>
public static class PdfColorToRgb
{
    public static (byte R, byte G, byte B) ToRgb(IReadOnlyList<double> components, string? colorSpace)
    {
        if (components is null || components.Count == 0) return (0, 0, 0);

        switch (colorSpace)
        {
            case "DeviceGray" or "CalGray" when components.Count >= 1:
            {
                byte g = Channel(components[0]);
                return (g, g, g);
            }
            case "DeviceRGB" or "CalRGB" when components.Count >= 3:
                return (Channel(components[0]), Channel(components[1]), Channel(components[2]));
            case "DeviceCMYK" when components.Count >= 4:
                return Cmyk(components[0], components[1], components[2], components[3]);
            case "Lab" when components.Count >= 3:
                return Lab(components[0], components[1], components[2]);
        }

        // Unknown space: infer from component count (matches ColorConverter's fallback).
        return components.Count switch
        {
            >= 4 => Cmyk(components[0], components[1], components[2], components[3]),
            >= 3 => (Channel(components[0]), Channel(components[1]), Channel(components[2])),
            _ => Mono(components[0])
        };
    }

    public static byte AlphaByte(double alpha) => (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);

    private static byte Channel(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
    private static (byte, byte, byte) Mono(double v) { byte g = Channel(v); return (g, g, g); }

    private static (byte, byte, byte) Cmyk(double c, double m, double y, double k) =>
        (Channel(1 - Math.Min(1, c * (1 - k) + k)),
         Channel(1 - Math.Min(1, m * (1 - k) + k)),
         Channel(1 - Math.Min(1, y * (1 - k) + k)));

    private static (byte, byte, byte) Lab(double l, double a, double b)
    {
        // PDF Lab components arrive already in CIE-Lab ranges (L 0-100, a/b signed).
        var lab = new Wacton.Unicolour.Unicolour(Wacton.Unicolour.ColourSpace.Lab, l, a, b);
        (double r, double g, double bl) = lab.Rgb.Byte255.Triplet.Tuple switch { var t => (t.Item1, t.Item2, t.Item3) };
        return ((byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(bl, 0, 255));
    }
}
```

> **Verify-as-you-go:** the `Wacton.Unicolour` API (`Unicolour`, `ColourSpace.Lab`, `.Rgb.Byte255`) — confirm against how `ColorConverter.cs` already calls Unicolour for Lab and mirror it exactly (the version is pinned in `PdfLibrary.csproj`). If the Lab call shape differs, copy `ColorConverter`'s Lab branch verbatim. The Lab test is not in Step 1, so a shape mismatch only surfaces at build — fix it by matching `ColorConverter`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfColorToRgbTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/PdfColorToRgb.cs PdfLibrary.Tests/Rendering/PdfColorToRgbTests.cs
git commit -m "feat(render): PdfColorToRgb — SkiaSharp-free device-color resolution for render targets"
```

---

## Task 3: `PdfLibrary.Rendering.Svg` project + geometry core

**Files:**
- Create: `PdfLibrary.Rendering.Svg/PdfLibrary.Rendering.Svg.csproj`, `SvgRenderTarget.cs`, `SvgPageExtensions.cs`
- Test: `PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs`

**Background:** The new package implementing `IRenderTarget` by writing SVG. This task does the page frame (root initial-transform `<g>`), path fill/stroke with style, the transform/state bookkeeping, and the convenience `RenderToSvg()`. Clip + images come in Task 4. `PdfColorToRgb` is `internal` to `PdfLibrary` — expose it to this package via `InternalsVisibleTo`, OR make it `public` (decide in Step 3; `public` is simpler since it's a legitimately reusable helper for target authors — prefer `public`).

**Interfaces:**
- Consumes: `IRenderTarget`, `IPathBuilder`/`PathSegment`, `PdfGraphicsState`, `PdfColorToRgb`.
- Produces: `public sealed class SvgRenderTarget : IRenderTarget` with `string GetSvg()`; `public static class SvgPageExtensions { static string RenderToSvg(this PdfPage page, double scale = 1.0); }`.

- [ ] **Step 1: Create the project + add to solution**

Create `PdfLibrary.Rendering.Svg/PdfLibrary.Rendering.Svg.csproj` mirroring the multi-target + nullable settings of `PdfLibrary.Rendering.SkiaSharp.csproj` but with NO SkiaSharp:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\PdfLibrary\PdfLibrary.csproj" />
  </ItemGroup>
</Project>
```
Add it to the solution: `dotnet sln add PdfLibrary.Rendering.Svg/PdfLibrary.Rendering.Svg.csproj`. Add a `ProjectReference` to it from `PdfLibrary.Tests.csproj`.

- [ ] **Step 2: Write the failing test**

Create `PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs`:

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Svg;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class SvgRenderTargetTests
{
    [Fact]
    public void RenderToSvg_FilledRectangle_EmitsPathWithRootTransform()
    {
        // A page that fills a red rectangle. Expect an <svg>, a root <g transform="matrix(...)">,
        // and a <path> with the red fill.
        byte[] pdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddRectangle(100, 100, 200, 150, fill: "#FF0000"))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        string svg = doc.GetPage(0)!.RenderToSvg();

        Assert.Contains("<svg", svg);
        Assert.Contains("transform=\"matrix(", svg);
        Assert.Contains("<path", svg);
        Assert.Contains("fill=\"rgb(255,0,0)\"", svg);
        Assert.Contains("</svg>", svg);
    }
}
```

> If `PdfDocumentBuilder`'s page API has no `AddRectangle(x,y,w,h,fill:)`, use whatever fill-a-rectangle/path call it does expose (read `PdfLibrary/Builder/Page/PdfPathBuilder.cs` — it has path + fill ops). The assertion targets are the SVG structure, not the builder call.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SvgRenderTargetTests"`
Expected: FAIL — `SvgRenderTarget`/`RenderToSvg` don't exist.

- [ ] **Step 4: Implement `SvgRenderTarget` (geometry) + `SvgPageExtensions`**

Create `PdfLibrary.Rendering.Svg/SvgRenderTarget.cs`. Key points: accumulate into a `StringBuilder`; format numbers invariant; build the initial transform per rotation; emit paths from `Segments`. (Clip/image/softmask/pattern members are stubbed here and completed in Task 4 — stub them as no-ops that don't throw.)

```csharp
using System.Globalization;
using System.Numerics;
using System.Text;
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;

namespace PdfLibrary.Rendering.Svg;

/// <summary>
/// A SkiaSharp-free <see cref="IRenderTarget"/> that renders a PDF page to SVG. Demonstrates the
/// geometry-only render SPI: paths arrive in CTM-baked PDF user space and are emitted verbatim as
/// SVG path data; the page initial-transform (Y-flip + scale + crop + rotation) is the root group.
/// </summary>
public sealed class SvgRenderTarget : IRenderTarget
{
    private readonly StringBuilder _sb = new();
    private int _clipDepth;                 // open <g clip-path> groups
    private readonly Stack<int> _saveStack = new();
    private int _clipId;
    private Matrix3x2 _currentCtm = Matrix3x2.Identity;
    private (int w, int h, double scale) _dims;

    public int CurrentPageNumber { get; private set; }

    public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
        double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0)
    {
        CurrentPageNumber = pageNumber;
        _clipDepth = 0; _clipId = 0; _saveStack.Clear(); _currentCtm = Matrix3x2.Identity;

        double pw = (rotation is 90 or 270 ? height : width) * scale;
        double ph = (rotation is 90 or 270 ? width : height) * scale;
        _dims = ((int)Math.Ceiling(pw), (int)Math.Ceiling(ph), scale);

        Matrix3x2 init = InitialTransform(width, height, scale, cropOffsetX, cropOffsetY, rotation);

        _sb.Append(F($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{pw}\" height=\"{ph}\" "))
           .Append(F($"viewBox=\"0 0 {pw} {ph}\">"))
           .Append(F($"<g transform=\"matrix({init.M11},{init.M12},{init.M21},{init.M22},{init.M31},{init.M32})\">"));
    }

    public void EndPage()
    {
        while (_clipDepth-- > 0) _sb.Append("</g>");   // close any dangling clip groups
        _sb.Append("</g></svg>");
    }

    public void Clear() { _sb.Clear(); _clipDepth = 0; _saveStack.Clear(); CurrentPageNumber = 0; }

    public string GetSvg() => _sb.ToString();

    public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedFillColor, state.ResolvedFillColorSpace);
        _sb.Append(F($"<path d=\"{D(path)}\" fill=\"rgb({r},{g},{b})\" fill-opacity=\"{state.FillAlpha}\" "))
           .Append(evenOdd ? "fill-rule=\"evenodd\"/>" : "fill-rule=\"nonzero\"/>");
    }

    public void StrokePath(IPathBuilder path, PdfGraphicsState state)
    {
        if (path.IsEmpty) return;
        (byte r, byte g, byte b) = PdfColorToRgb.ToRgb(state.ResolvedStrokeColor, state.ResolvedStrokeColorSpace);
        _sb.Append(F($"<path d=\"{D(path)}\" fill=\"none\" stroke=\"rgb({r},{g},{b})\" "))
           .Append(F($"stroke-opacity=\"{state.StrokeAlpha}\" stroke-width=\"{Math.Max(state.LineWidth, 0.1)}\" "))
           .Append(F($"stroke-linecap=\"{Cap(state.LineCap)}\" stroke-linejoin=\"{Join(state.LineJoin)}\"/>"));
    }

    public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        FillPath(path, state, evenOdd);
        StrokePath(path, state);
    }

    public void ApplyCtm(Matrix3x2 ctm) => _currentCtm = ctm;
    public void OnGraphicsStateChanged(PdfGraphicsState state) { }
    public (int width, int height, double scale) GetPageDimensions() => _dims;

    // ---- stubs completed in Task 4 ----
    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
    public void DrawImage(PdfImage image, PdfGraphicsState state) { }
    public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
        PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent)
        => FillPath(path, state, evenOdd); // approximate as solid fill (Task 4 adds a comment marker)
    public void SaveState() => _saveStack.Push(_clipDepth);
    public void RestoreState() { if (_saveStack.Count > 0) { int target = _saveStack.Pop(); while (_clipDepth > target) { _sb.Append("</g>"); _clipDepth--; } } }
    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }
    public void ClearSoftMask() { }

    // ---- helpers ----
    private static string F(FormattableString s) => FormattableString.Invariant(s);

    private static string D(IPathBuilder path)
    {
        var sb = new StringBuilder();
        foreach (PathSegment s in path.Segments)
            sb.Append(s switch
            {
                MoveToSegment m => F($"M{m.X} {m.Y} "),
                LineToSegment l => F($"L{l.X} {l.Y} "),
                CurveToSegment c => F($"C{c.X1} {c.Y1} {c.X2} {c.Y2} {c.X3} {c.Y3} "),
                ClosePathSegment => "Z ",
                _ => ""
            });
        return sb.ToString().TrimEnd();
    }

    private static Matrix3x2 InitialTransform(double width, double height, double scale,
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
        double rad = -rotation * Math.PI / 180.0;
        return Matrix3x2.CreateTranslation((float)-cropX, (float)-cropY)
             * Matrix3x2.CreateRotation((float)rad)
             * Matrix3x2.CreateTranslation(tx, ty)
             * Matrix3x2.CreateScale((float)scale, (float)-scale)
             * Matrix3x2.CreateTranslation(0, (float)(finalHeight * scale));
    }

    private static string Cap(int c) => c switch { 1 => "round", 2 => "square", _ => "butt" };
    private static string Join(int j) => j switch { 1 => "round", 2 => "bevel", _ => "miter" };
}
```

Create `PdfLibrary.Rendering.Svg/SvgPageExtensions.cs`:
```csharp
using PdfLibrary.Document;

namespace PdfLibrary.Rendering.Svg;

public static class SvgPageExtensions
{
    /// <summary>Render a page to a standalone SVG document string.</summary>
    public static string RenderToSvg(this PdfPage page, double scale = 1.0)
    {
        var target = new SvgRenderTarget();
        page.Render(target);   // page.Render drives the IRenderTarget; BeginPage supplies dims/scale
        return target.GetSvg();
    }
}
```

> **Verify:** how `PdfPage.Render(target)` obtains `scale`/dimensions for `BeginPage` — if `Render` doesn't take a scale, the page's own dimensions drive `BeginPage` and the `scale` param here may be unused (or there's a `Render` overload). Read `PdfPage.Render` (it constructs the renderer that calls `BeginPage`) and wire `scale` through if supported; otherwise drop the `scale` parameter. `PdfColorToRgb` is `public` (Task 2), so it's directly usable from this separate package.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SvgRenderTargetTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add PdfLibrary.Rendering.Svg/ PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs PdfLibrary.Tests/PdfLibrary.Tests.csproj *.sln*
git commit -m "feat(svg): SkiaSharp-free SvgRenderTarget — page frame + path fill/stroke + RenderToSvg()"
```

---

## Task 4: SVG clip stack + images (JPEG passthrough)

**Files:**
- Modify: `PdfLibrary.Rendering.Svg/SvgRenderTarget.cs`
- Test: `PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs`

**Background:** Complete the stubbed members. **Clip:** `SetClippingPath` emits a `<clipPath id="cN"><path .../></clipPath>` def and opens a `<g clip-path="url(#cN)">`; the `SaveState`/`RestoreState` depth stack (already implemented in Task 3) closes those groups at the matching `Q`. **Images:** embed `DCTDecode` (JPEG) streams directly as `data:image/jpeg;base64,…` (cheap, no decode); non-JPEG images draw a placeholder `<rect>` (the full `PdfImage→RGBA` + PNG path is deferred — see Out of Scope). The `<image>` is a 1×1 unit square transformed by `state.Ctm * initialTransform` plus a unit Y-flip (PDF image rows are top-first). **Soft mask / tiling pattern:** keep as no-ops (content renders unmasked/unpatterned) but emit an SVG comment noting the omission.

- [ ] **Step 1: Write the failing tests**

Add to `SvgRenderTargetTests.cs`:
```csharp
[Fact]
public void RenderToSvg_ClippedContent_EmitsClipPathGroup()
{
    byte[] pdf = PdfDocumentBuilder.Create()
        .AddPage(p => p.AddClippedRectangle(/* clip + fill — see note */))
        .ToByteArray();
    using var ms = new MemoryStream(pdf);
    using PdfDocument doc = PdfDocument.Load(ms);
    string svg = doc.GetPage(0)!.RenderToSvg();
    Assert.Contains("<clipPath id=", svg);
    Assert.Contains("clip-path=\"url(#", svg);
}

[Fact]
public void RenderToSvg_JpegImage_EmitsJpegDataUri()
{
    // A page with a JPEG (DCTDecode) image. Use a fixture PDF with a JPEG, or the builder's image API.
    string pdfPath = FindRepoFile(/* a repo PDF that embeds a JPEG image */);
    using PdfDocument doc = PdfDocument.Load(pdfPath);
    string svg = doc.GetPage(0)!.RenderToSvg();
    Assert.Contains("<image", svg);
    Assert.Contains("data:image/jpeg;base64,", svg);
}
```

> Clip + image test setup is the fiddly part. For clip: if the builder has no clip op, build a tiny raw PDF whose content stream has `… W n …` around a fill, or skip the builder and hand-craft the content. For the JPEG image: search the repo's `PDFs/` tree for a PDF with a `DCTDecode` image (e.g. under `PDFs/PDF Standards/Compression/JPEG/`) and reuse the `FindRepoFile` helper from `CoreTextRendererTests`. If neither is feasible, assert at the `SvgRenderTarget` level directly (call `SetClippingPath`/`DrawImage` with a constructed `IPathBuilder`/`PdfImage` and inspect `GetSvg()`), which is a legitimate unit test of the target.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SvgRenderTargetTests"`
Expected: the two new tests FAIL (clip/image stubs are no-ops).

- [ ] **Step 3: Implement clip + images**

Replace the Task-3 stubs:
```csharp
    public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
    {
        if (path.IsEmpty) return;
        int id = ++_clipId;
        string rule = evenOdd ? " clip-rule=\"evenodd\"" : "";
        _sb.Append(F($"<clipPath id=\"c{id}\"><path d=\"{D(path)}\"{rule}/></clipPath>"))
           .Append(F($"<g clip-path=\"url(#c{id})\">"));
        _clipDepth++;
    }

    public void DrawImage(PdfImage image, PdfGraphicsState state)
    {
        // image space is the unit square at origin; flip Y within it (PDF image rows are top-first),
        // then place by state.Ctm. The root <g> applies the page initial transform on top.
        Matrix3x2 m = state.Ctm;
        string xform = F($"matrix({m.M11},{m.M12},{m.M21},{m.M22},{m.M31},{m.M32}) translate(0,1) scale(1,-1)");

        byte[] encoded = image.GetEncodedData();
        bool isJpeg = encoded.Length > 3 && encoded[0] == 0xFF && encoded[1] == 0xD8 && encoded[2] == 0xFF;
        if (isJpeg)
        {
            string b64 = Convert.ToBase64String(encoded);
            _sb.Append(F($"<image transform=\"{xform}\" width=\"1\" height=\"1\" preserveAspectRatio=\"none\" "))
               .Append(F($"href=\"data:image/jpeg;base64,{b64}\"/>"));
        }
        else
        {
            // Non-JPEG images need the PdfImage->RGBA + PNG path (deferred). Placeholder for now.
            _sb.Append("<!-- non-JPEG image omitted (RGBA path deferred) -->")
               .Append(F($"<rect transform=\"{xform}\" width=\"1\" height=\"1\" fill=\"#cccccc\" fill-opacity=\"0.3\"/>"));
        }
    }

    public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent)
        => _sb.Append(F($"<!-- soft mask '{maskSubtype}' not applied (deferred) -->"));
    public void ClearSoftMask() { }
```
(Remove the duplicate stub definitions from Task 3. `FillPathWithTilingPattern` is not in the interface as a default — it must be implemented; stub it to just fill with the path's fill color and emit a comment: call `FillPath(path, state, evenOdd)` + `_sb.Append("<!-- tiling pattern approximated as solid fill -->")`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SvgRenderTargetTests"`
Expected: PASS (all SVG tests).

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary.Rendering.Svg/SvgRenderTarget.cs PdfLibrary.Tests/Rendering/SvgRenderTargetTests.cs
git commit -m "feat(svg): clip-path stack + JPEG image passthrough (non-JPEG/softmask/pattern deferred)"
```

---

## Task 5: SPI implementer docs + verification

**Files:**
- Create: `Docs/RendererSpi.md`
- Verification (no code)

**Background:** Document `IRenderTarget` for third-party implementers, using `SvgRenderTarget` as the worked example. This is what someone reads to write a WPF/Avalonia/PDF-export backend.

- [ ] **Step 1: Write `Docs/RendererSpi.md`**

Cover, concretely:
- **The model:** the core flattens everything (incl. text → glyph outline paths) to geometry; a target implements `IRenderTarget` (19 members, 2 default no-ops) and never touches fonts/SkiaSharp.
- **The coordinate contract (the critical section):** paths arrive in CTM-baked PDF user space (Y-up); the target applies ONLY the page initial transform; give the exact `BeginPage`→initial-transform matrix formula (rotation 0 + the 90/180/270 variants); explain `ApplyCtm` is for `DrawImage` only.
- **Per-member contract table:** what each of the 19 members must do (from the C map §3), and which 2 are default no-ops.
- **Colors:** `ResolvedFillColor`/`ResolvedFillColorSpace` → RGB via `PdfColorToRgb`; alpha via `FillAlpha`/`StrokeAlpha`.
- **Paths:** `IPathBuilder.Segments` → the four `PathSegment` records; even-odd vs nonzero.
- **Images:** `DrawImage` draws a unit square transformed by `state.Ctm`; the `PdfImage` API; note the Y-flip and the RGBA-vs-JPEG choice.
- **The example:** point at `PdfLibrary.Rendering.Svg.SvgRenderTarget` as a complete ~250-line worked reference, and `RenderToSvg()` as the usage.
- **What's deferred in the SVG example** (so readers know the SPI supports more than the sample shows): full image RGBA, shadings, tiling patterns, soft masks.

- [ ] **Step 2: Verify the whole branch**

Run:
- `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo` — PASS (SkiaSharp render tests unchanged + new color/SVG tests).
- `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary.Rendering.Svg --include=*.cs | grep -v /obj/` — **no output** (the SVG package is SkiaSharp-free).
- `dotnet build PdfLibrary.Rendering.Svg/PdfLibrary.Rendering.Svg.csproj -c Release --nologo` — 0W/0E.
- `dotnet build PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj -c Release --nologo` — 0W/0E (slim still builds).

- [ ] **Step 3: Visual self-check (controller-run, not a test)**

Render a real multi-element PDF to SVG (`page.RenderToSvg()`), write it to a file, and open it in a browser. Confirm vector content + text (as filled glyph paths) + JPEG images render correctly and positioned right (especially text inside `cm`-transformed figures — the coordinate contract). Note any gross issue.

- [ ] **Step 4: Commit the docs**

```bash
git add Docs/RendererSpi.md
git commit -m "docs: renderer SPI implementer guide (coordinate contract + per-member, SVG worked example)"
```

---

## Self-Review Notes

- **Spec coverage:** independent SkiaSharp-free `IRenderTarget` (the agnostic proof) + adapter slim + SPI docs — all three Plan C deliverables.
- **Coordinate contract honored:** root `<g>` = initial transform; paths emitted verbatim (CTM pre-baked); `ApplyCtm` tracked only for images.
- **Scope honesty:** the SVG target is vector-complete (paths/clip/state/color/text-as-paths) + JPEG images; non-JPEG images, shadings, tiling patterns, and soft masks are deferred/approximated and clearly marked (in code comments, the docs, and Out of Scope). That's enough to PROVE the SPI without the heavy shared RGBA work.

## Out of scope / deferred

- **`PdfImage → RGBA` core helper + PNG encoder** (full image support for non-JPEG images, and the basis for any raster non-Skia target incl. WPF). ~350–400 lines hoisted from `ImageRenderer.CreateBitmapFromPdfImage` + a Skia-free PNG encoder. The single biggest follow-up; shared across all non-Skia targets.
- **Shadings** (`PaintShading`/`FillPathWithShadingPattern`) → SVG `<linearGradient>`/`<radialGradient>`; **tiling patterns** → SVG `<pattern>`; **soft masks** → SVG `<mask>`.
- **WPF render target** (de-Skias the viewer) — builds on the RGBA helper above; see the SPI-2.0 design notes.
- **B3c carryover** (unchanged): bundle a libre last-resort font for fontless hosts; hoist `SystemFontLocator` to document scope.
