# Renderer SPI — Plan B3a: Core Text-Pipeline Primitives

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the three SkiaSharp-free, testable-in-isolation primitives the core embedded-text pipeline (Plan B3b) needs: a path transform, a glyph-path service (cache + outline→path), and the glyph-placement matrix builder — including the fidelity-critical Y-flip compensation.

**Architecture:** Each primitive is the core, SkiaSharp-free equivalent of a piece of the SkiaSharp `TextRenderer`: (1) `IPathBuilder.Transform(Matrix3x2)` replaces the per-glyph `SKCanvas.SetMatrix`; (2) `GlyphPathService` replaces `GetCachedGlyphPath` (cache of glyph-space `IPathBuilder`, built via the existing `GlyphOutlineToPath`); (3) `GlyphPlacement` replaces `RenderGlyph`'s matrix build + `CreateYFlipCompensatedMatrix`, producing a glyph→user `Matrix3x2`. None is wired into rendering yet — B3b consumes them and flips `PdfRenderer.OnShowText` from `DrawText` to `FillPath`/`StrokePath`.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, xUnit. No new package dependencies (the cache uses a thread-safe `ConcurrentDictionary`, not `MemoryCache`, to avoid adding a core dependency).

## Global Constraints

- Core `PdfLibrary` must remain SkiaSharp-free (no `using SkiaSharp`, no package ref).
- Multi-target `net8.0;net9.0;net10.0`.
- New types are `internal` (consumed only by the core text pipeline in B3b), namespace `PdfLibrary.Rendering`; tests in `PdfLibrary.Tests/Rendering/`.
- `IPathBuilder.Transform` is added to the public interface — a 2.0 breaking change, acceptable on `skia-v4`.
- xUnit via global usings (don't add `using Xunit;` unless the build complains).
- These primitives must reproduce the SkiaSharp `TextRenderer` math EXACTLY — they are ports, not redesigns. The 90 render tests (gated in B3b) are the ultimate check; here the unit tests pin the math directly.
- The full suite (currently 1254 on the CI filter) must stay green after every task.

---

## File Structure

- `PdfLibrary/Rendering/IPathBuilder.cs` (modify) + `PathBuilder.cs` (modify) — add `Transform`.
- `PdfLibrary/Rendering/GlyphPathService.cs` (create, internal) — glyph-space `IPathBuilder` cache + builder.
- `PdfLibrary/Rendering/GlyphPlacement.cs` (create, internal) — the glyph→user `Matrix3x2` (incl. Y-flip compensation).
- `PdfLibrary.Tests/Rendering/PathBuilderTransformTests.cs` (create)
- `PdfLibrary.Tests/Rendering/GlyphPathServiceTests.cs` (create)
- `PdfLibrary.Tests/Rendering/GlyphPlacementTests.cs` (create)

---

## Task 1: `IPathBuilder.Transform(Matrix3x2)`

**Files:**
- Modify: `PdfLibrary/Rendering/IPathBuilder.cs`, `PdfLibrary/Rendering/PathBuilder.cs`
- Test: `PdfLibrary.Tests/Rendering/PathBuilderTransformTests.cs`

**Background:** B3b positions a cached glyph-space path into user space by applying the glyph→user `Matrix3x2`, instead of the SkiaSharp path's `SKCanvas.SetMatrix`. This returns a NEW path (the cached one must stay immutable and reusable). Points transform by `System.Numerics.Vector2.Transform(point, matrix)` — i.e. `(x*M11 + y*M21 + M31, x*M12 + y*M22 + M32)`.

**Interfaces:**
- Produces: `IPathBuilder IPathBuilder.Transform(Matrix3x2 matrix)` — returns a new path with every segment point transformed; the receiver is unchanged.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/PathBuilderTransformTests.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PathBuilderTransformTests
{
    [Fact]
    public void Transform_AppliesMatrixToEverySegment_AndLeavesOriginalUnchanged()
    {
        var src = new PathBuilder();
        src.MoveTo(1, 0);
        src.LineTo(0, 1);
        src.CurveTo(1, 1, 2, 2, 3, 3);
        src.ClosePath();

        // scale x2, translate (+10, +20)
        var m = new Matrix3x2(2, 0, 0, 2, 10, 20);
        IPathBuilder dst = ((IPathBuilder)src).Transform(m);

        IReadOnlyList<PathSegment> s = dst.Segments;
        Assert.Equal(new MoveToSegment(12, 20), s[0]);   // (1,0) -> (2+10, 0+20)
        Assert.Equal(new LineToSegment(10, 22), s[1]);   // (0,1) -> (0+10, 2+20)
        Assert.Equal(new CurveToSegment(12, 22, 14, 24, 16, 26), s[2]);
        Assert.IsType<ClosePathSegment>(s[3]);

        // original untouched
        Assert.Equal(new MoveToSegment(1, 0), src.Segments[0]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PathBuilderTransformTests"`
Expected: FAIL — `IPathBuilder` has no `Transform`.

- [ ] **Step 3: Implement**

In `PdfLibrary/Rendering/IPathBuilder.cs`, add to the interface body:

```csharp
    /// <summary>
    /// Returns a new path with every segment point transformed by <paramref name="matrix"/>.
    /// The receiver is unchanged (so a cached path can be positioned without being mutated).
    /// </summary>
    IPathBuilder Transform(System.Numerics.Matrix3x2 matrix);
```

In `PdfLibrary/Rendering/PathBuilder.cs`, add (and `using System.Numerics;` at the top if not present):

```csharp
    public IPathBuilder Transform(Matrix3x2 matrix)
    {
        var result = new PathBuilder();
        foreach (PathSegment seg in _segments)
        {
            switch (seg)
            {
                case MoveToSegment m:
                {
                    Vector2 p = Vector2.Transform(new Vector2((float)m.X, (float)m.Y), matrix);
                    result.MoveTo(p.X, p.Y);
                    break;
                }
                case LineToSegment l:
                {
                    Vector2 p = Vector2.Transform(new Vector2((float)l.X, (float)l.Y), matrix);
                    result.LineTo(p.X, p.Y);
                    break;
                }
                case CurveToSegment c:
                {
                    Vector2 p1 = Vector2.Transform(new Vector2((float)c.X1, (float)c.Y1), matrix);
                    Vector2 p2 = Vector2.Transform(new Vector2((float)c.X2, (float)c.Y2), matrix);
                    Vector2 p3 = Vector2.Transform(new Vector2((float)c.X3, (float)c.Y3), matrix);
                    result.CurveTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
                    break;
                }
                case ClosePathSegment:
                    result.ClosePath();
                    break;
            }
        }
        return result;
    }
```

(Note: `Vector2.Transform(v, m)` computes `v * m` in row-vector form — exactly the point transform PDF/`Matrix3x2` use. Coordinates are `double` in `PathSegment` but `Vector2` is `float`; this matches the SkiaSharp path which is also `float` per point, preserving pixel parity.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PathBuilderTransformTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/IPathBuilder.cs PdfLibrary/Rendering/PathBuilder.cs PdfLibrary.Tests/Rendering/PathBuilderTransformTests.cs
git commit -m "feat(render): IPathBuilder.Transform(Matrix3x2) for positioning cached glyph paths"
```

---

## Task 2: `GlyphPlacement` — the glyph→user matrix (with Y-flip compensation)

**Files:**
- Create: `PdfLibrary/Rendering/GlyphPlacement.cs`
- Test: `PdfLibrary.Tests/Rendering/GlyphPlacementTests.cs`

**Background:** Faithful port of `TextRenderer.RenderGlyph`'s matrix build (lines 792–804) + `CreateYFlipCompensatedMatrix` (lines 908–952), producing a `Matrix3x2` (glyph→user) instead of an `SKMatrix`. The SkiaSharp version maps the resulting `SKMatrix` fields as `ScaleX=M11, SkewY=M12, SkewX=±M21, ScaleY=∓M22, TransX=M31, TransY=M32`; expressed as a `Matrix3x2(M11, M12, M21, M22, M31, M32)` this is: horizontal text negates `M22`; vertical text (|rotation|≈90°) negates `M21`.

**Interfaces:**
- Produces:
  - `static Matrix3x2 GlyphPlacement.GlyphToUser(PdfGraphicsState state, double currentX, float tHs)` — builds `Translation(currentX,0) × (textState × TextMatrix)` then applies Y-flip compensation.
  - `static Matrix3x2 GlyphPlacement.YFlipCompensate(Matrix3x2 m)` — the compensation step (exposed for direct testing).

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/GlyphPlacementTests.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphPlacementTests
{
    [Fact]
    public void YFlipCompensate_Horizontal_NegatesM22()
    {
        var m = new Matrix3x2(2, 0, 0, 3, 5, 7); // rotation 0 -> horizontal
        Matrix3x2 r = GlyphPlacement.YFlipCompensate(m);
        Assert.Equal(2, r.M11, 4);
        Assert.Equal(0, r.M12, 4);
        Assert.Equal(0, r.M21, 4);
        Assert.Equal(-3, r.M22, 4);   // horizontal -> negate M22
        Assert.Equal(5, r.M31, 4);
        Assert.Equal(7, r.M32, 4);
    }

    [Fact]
    public void YFlipCompensate_Vertical_NegatesM21()
    {
        // 90deg rotation: M11=0, M12=1, M21=-1, M22=0
        var m = new Matrix3x2(0, 1, -1, 0, 0, 0);
        Matrix3x2 r = GlyphPlacement.YFlipCompensate(m);
        Assert.Equal(0, r.M11, 4);
        Assert.Equal(1, r.M12, 4);
        Assert.Equal(1, r.M21, 4);    // vertical -> negate M21 (was -1)
        Assert.Equal(0, r.M22, 4);
    }

    [Fact]
    public void GlyphToUser_AppliesTextStateTextMatrixAndTranslation()
    {
        var state = new PdfGraphicsState();
        state.SetTextMatrix(1, 0, 0, 1, 100, 200); // identity orientation, origin (100,200)
        state.TextRise = 0;
        // tHs = 1 (100% horizontal scaling)
        Matrix3x2 r = GlyphPlacement.GlyphToUser(state, currentX: 10, tHs: 1f);
        // glyph origin (0,0) -> translation(10,0) then TextMatrix -> (110, 200); Y not flipped at origin
        Vector2 origin = Vector2.Transform(Vector2.Zero, r);
        Assert.Equal(110, origin.X, 3);
        Assert.Equal(200, origin.Y, 3);
    }
}
```

(If `PdfGraphicsState.SetTextMatrix` has a different signature, check `PdfGraphicsState.cs` and adjust the test setup — the point is an identity-orientation text matrix translated to (100,200).)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphPlacementTests"`
Expected: FAIL — `GlyphPlacement` does not exist.

- [ ] **Step 3: Implement**

Create `PdfLibrary/Rendering/GlyphPlacement.cs`:

```csharp
using System.Numerics;
using PdfLibrary.Content;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds the glyph-space → user-space transform for a single glyph (SkiaSharp-free port of
/// TextRenderer.RenderGlyph's matrix build + CreateYFlipCompensatedMatrix). The page canvas
/// applies the CTM separately, so this matrix carries only the text-space positioning.
/// </summary>
internal static class GlyphPlacement
{
    public static Matrix3x2 GlyphToUser(PdfGraphicsState state, double currentX, float tHs)
    {
        var tRise = (float)state.TextRise;

        // textState: horizontal scaling on X, text rise on the translation row.
        var textStateMatrix = new Matrix3x2(
            tHs, 0,
            0, 1,
            0, tRise);

        Matrix3x2 glyphMatrix = textStateMatrix * state.TextMatrix;
        Matrix3x2 translationMatrix = Matrix3x2.CreateTranslation((float)currentX, 0);
        Matrix3x2 fullGlyphMatrix = translationMatrix * glyphMatrix;

        return YFlipCompensate(fullGlyphMatrix);
    }

    /// <summary>
    /// Compensates for the canvas's global Y-flip. Horizontal text negates M22; vertical text
    /// (|rotation| near 90deg) negates M21 instead. (Port of CreateYFlipCompensatedMatrix.)
    /// </summary>
    public static Matrix3x2 YFlipCompensate(Matrix3x2 m)
    {
        double rotationDeg = System.Math.Atan2(m.M12, m.M11) * (180.0 / System.Math.PI);
        while (rotationDeg > 180) rotationDeg -= 360;
        while (rotationDeg < -180) rotationDeg += 360;

        bool isVertical = System.Math.Abs(System.Math.Abs(rotationDeg) - 90) < 45;

        return isVertical
            ? new Matrix3x2(m.M11, m.M12, -m.M21, m.M22, m.M31, m.M32)
            : new Matrix3x2(m.M11, m.M12, m.M21, -m.M22, m.M31, m.M32);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphPlacementTests"`
Expected: PASS (3 tests). If the `GlyphToUser` test fails on the text-matrix setup, inspect `PdfGraphicsState.TextMatrix`/`SetTextMatrix` and fix the test's matrix construction (not the implementation, which mirrors the SkiaSharp original exactly).

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/GlyphPlacement.cs PdfLibrary.Tests/Rendering/GlyphPlacementTests.cs
git commit -m "feat(render): GlyphPlacement — glyph->user matrix with Y-flip compensation (core port)"
```

---

## Task 3: `GlyphPathService` — cached glyph-space paths

**Files:**
- Create: `PdfLibrary/Rendering/GlyphPathService.cs`
- Test: `PdfLibrary.Tests/Rendering/GlyphPathServiceTests.cs`

**Background:** Faithful port of `TextRenderer.GetCachedGlyphPath` (lines 748–783). Builds a glyph-space `IPathBuilder` via the existing `GlyphOutlineToPath` (CFF/Type1 direct → `FromCff`; else `FromTrueType`), and caches it. Cache key matches the original: `{RuntimeHelpers.GetHashCode(embeddedMetrics)}_{glyphId}_{(int)(fontSize*10)}` (object identity avoids subset collisions; 0.1pt size precision). Uses a thread-safe `ConcurrentDictionary` (the original's `MemoryCache` eviction is a memory optimization, not behavior; a plain bounded cache keeps the core dependency-free — start unbounded, note the eviction TODO).

**Interfaces:**
- Consumes: `GlyphOutlineToPath.FromTrueType`/`FromCff` (B2), `EmbeddedFontMetrics` glyph-outline API, `PdfLibrary.Fonts.Embedded.GlyphOutline`.
- Produces: `IPathBuilder GlyphPathService.GetGlyphPath(EmbeddedFontMetrics metrics, ushort glyphId, float fontSize, GlyphOutline ttOutline, string? resolvedGlyphName)` — returns the cached glyph-space path (caller transforms a copy via `Transform`, never mutates this one).

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/GlyphPathServiceTests.cs`:

```csharp
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphPathServiceTests
{
    private static EmbeddedFontMetrics PixelMetrics() =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf")));

    [Fact]
    public void GetGlyphPath_ForRealTrueTypeGlyph_ReturnsNonEmptyPath_AndCaches()
    {
        EmbeddedFontMetrics m = PixelMetrics();
        Assert.True(m.IsValid);
        ushort gid = m.GetGlyphId('A');
        Assert.NotEqual(0, gid);
        GlyphOutline? outline = m.GetGlyphOutline(gid);
        Assert.NotNull(outline);

        var service = new GlyphPathService();
        IPathBuilder p1 = service.GetGlyphPath(m, gid, fontSize: 100, outline!, resolvedGlyphName: null);
        IPathBuilder p2 = service.GetGlyphPath(m, gid, fontSize: 100, outline!, resolvedGlyphName: null);

        Assert.False(p1.IsEmpty);
        Assert.Same(p1, p2); // cached: same instance back
    }
}
```

(If `EmbeddedFontMetrics` has no public `byte[]` constructor or `GetGlyphId(char)` differs, inspect `EmbeddedFontMetrics.cs` and adjust the test setup accordingly — the production code below depends only on the methods listed in the Interfaces block.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphPathServiceTests"`
Expected: FAIL — `GlyphPathService` does not exist.

- [ ] **Step 3: Implement**

Create `PdfLibrary/Rendering/GlyphPathService.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using PdfLibrary.Fonts.Embedded;
using CffGlyphOutline = FontParser.Tables.Cff.GlyphOutline;
using GlyphOutline = PdfLibrary.Fonts.Embedded.GlyphOutline;

namespace PdfLibrary.Rendering;

/// <summary>
/// Builds and caches glyph-space <see cref="IPathBuilder"/> paths for embedded-font glyphs
/// (SkiaSharp-free port of TextRenderer.GetCachedGlyphPath). The cached path is canonical and
/// immutable — callers position a copy via <see cref="IPathBuilder.Transform"/>, never mutate it.
/// </summary>
internal sealed class GlyphPathService
{
    private readonly ConcurrentDictionary<string, IPathBuilder> _cache = new();

    public IPathBuilder GetGlyphPath(EmbeddedFontMetrics metrics, ushort glyphId, float fontSize,
        GlyphOutline ttOutline, string? resolvedGlyphName)
    {
        // Identity-based key: different subsets can share a BaseFont; 0.1pt size precision.
        int fontId = RuntimeHelpers.GetHashCode(metrics);
        var roundedSize = (int)(fontSize * 10);
        var key = $"{fontId}_{glyphId}_{roundedSize}";

        if (_cache.TryGetValue(key, out IPathBuilder? cached))
            return cached;

        IPathBuilder path = Build(metrics, glyphId, fontSize, ttOutline, resolvedGlyphName);
        return _cache.GetOrAdd(key, path);
    }

    private static IPathBuilder Build(EmbeddedFontMetrics metrics, ushort glyphId, float fontSize,
        GlyphOutline ttOutline, string? resolvedGlyphName)
    {
        ushort upm = metrics.UnitsPerEm;

        if (metrics.IsCffFont)
        {
            CffGlyphOutline? cff = metrics.GetCffGlyphOutlineDirect(glyphId);
            return cff is not null
                ? GlyphOutlineToPath.FromCff(cff, fontSize, upm)
                : GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
        }

        if (metrics.IsType1Font && resolvedGlyphName is not null)
        {
            CffGlyphOutline? t1 = metrics.GetType1GlyphOutlineDirect(resolvedGlyphName);
            return t1 is not null
                ? GlyphOutlineToPath.FromCff(t1, fontSize, upm)
                : GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
        }

        return GlyphOutlineToPath.FromTrueType(ttOutline, fontSize, upm);
    }
}
```

(Note: the original capped its cache via `MemoryCache` (5000 entries, 5-min sliding). This uses an unbounded `ConcurrentDictionary` to avoid a core package dependency; a bounded eviction policy is a follow-up — fine for B3a since nothing renders through it yet, and B3b can revisit if memory matters. No fill rule is stored on the path: glyphs fill even-odd via `FillPath(..., evenOdd: true)` in B3b.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~GlyphPathServiceTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/GlyphPathService.cs PdfLibrary.Tests/Rendering/GlyphPathServiceTests.cs
git commit -m "feat(render): GlyphPathService — cached glyph-space IPathBuilder via GlyphOutlineToPath"
```

---

## Task 4: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Full suite (CI filter)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`
Expected: PASS, 0 failures (≥ 1254 + the new tests).

- [ ] **Step 2: Core still SkiaSharp-free**

Run: `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/`
Expected: no output.

- [ ] **Step 3: Release builds warning-free (core)**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: No commit (verification only).**

---

## Self-Review Notes

- **Spec coverage:** these are the SkiaSharp-free primitives for the design's "thin-target text" — the path positioning (`Transform`), the glyph cache (`GlyphPathService` over `GlyphOutlineToPath`), and the glyph-placement matrix (`GlyphPlacement`, with the fidelity-critical Y-flip). All ported verbatim from the SkiaSharp `TextRenderer`.
- **No live-path change:** nothing wires these into rendering; existing behavior and the suite are unaffected until B3b.
- **Fidelity:** `GlyphPlacement` and `Transform` reproduce the exact matrix math the SkiaSharp `RenderGlyph` does (textState × TextMatrix × translation, Y-flip via M22/M21 negation, point transform via row-vector `Matrix3x2`). The unit tests pin the math; B3b's 90-render-test gate confirms end-to-end pixel parity.

## Out of scope → Plan B3b (the live flip — high risk, 90-render-test gated)

- `CoreTextRenderer` (internal): port `TryRenderWithGlyphOutlines` + `ResolveGlyphId` + `AdvancePosition`; per glyph: resolve id → `GlyphPathService.GetGlyphPath` → `GlyphPlacement.GlyphToUser` → `path.Transform(...)` → `IRenderTarget.FillPath(userPath, state, evenOdd: true)` / `StrokePath`. Thread the Tr render modes (incl. the stroke-width-in-user-space and faux-bold logic from `RenderGlyph` lines 843–904), and the em-dash + ligature paths.
- Wire `PdfRenderer.OnShowText` / `OnShowTextWithPositioning` to call `CoreTextRenderer` for embedded fonts; keep `_target.DrawText` for the non-embedded fallback (moved to core in Plan B3c).
- Gate hard on the 90 render tests staying pixel-identical (a wrong matrix split shifts/flips ALL embedded text). Add a `MockRenderTarget`-based test asserting `FillPath` is emitted per visible glyph.

### B3b critical constraints (from B3a's final review — these are the *only* paths to a regression; B3a itself is proven exact)

1. **Pass `evenOdd: true` at EVERY glyph `FillPath` call site.** `PathBuilder` carries no fill rule; the original set `FillType=EvenOdd` on the cached `SKPath` (`TextRenderer.cs:780`). Glyph fill is a NEW call site distinct from the operator-driven fill (which forwards the operator's rule). Default NonZeroWinding mis-fills glyphs with overlapping/same-direction contours. (EvenOdd is also winding-insensitive, so it's robust to whatever flip `GlyphOutlineToPath` applies.)
2. **The render target's CTM must carry the same global page Y-flip the SkiaSharp canvas applied in `BeginPage`, and nothing more.** `GlyphPlacement.YFlipCompensate` exists *only* to counter that page flip. A target that applies a different flip / none / its own winding override breaks every glyph.
3. **`GlyphOutlineToPath` geometry must equal the old `GlyphToSKPathConverter` (scale + Y-flip).** The unit tests assert non-empty, not equivalence. The 90 render tests are the backstop at the flip; consider a focused glyph-render comparison earlier to fail fast.
4. **CFF/Type1 are the COMMON case, not edge.** Most embedded PDF fonts are CFF/OpenType-CFF or Type1; `GlyphPathService`'s CFF/Type1 branches are untested (PublicPixel is TrueType). Add a CFF and a Type1 fixture before/with the flip.
5. **Stroke (Tr 1/2) + faux-bold widths must be recomputed in user/device space.** The originals derive width from `combinedScale = sqrt(|ScaleX·ScaleY − SkewX·SkewY|)` of the glyph→device matrix (`TextRenderer.cs:820-823, 855-858, 881-891`). Once `Transform` bakes glyph→user into the geometry and the target applies CTM internally, there is no single `combined` matrix at draw time — derive from `target CTM scale × GlyphToUser scale`, or stroke in user space.
6. **The cache hands out a shared mutable instance — treat as read-only.** `.Transform(m)` returns a fresh copy (safe); direct mutation corrupts the cache cross-thread. (`Transform` reintroduces the per-glyph allocation the original avoided — an accepted cost of the Skia-free design.)

## Out of scope → Plan B3c

- Core fallback path (non-embedded fonts) via `SystemFontLocator.GetFontData` (Plan A) → `SfntFont`/`EmbeddedFontMetrics` (Plan B/B2) → outlines → paths, including the style-driven `.ttc` face picker and DejaVu width-fixup.
- Remove `DrawText`/`MeasureTextWidth` from `IRenderTarget`; delete the Skia `TextRenderer` + `GlyphToSKPathConverter`; update `SkiaSharpRenderTargetForPattern`. `MeasureTextWidth` has no callers (a dead `// TODO` in `PdfRenderer`), so its removal is free.
