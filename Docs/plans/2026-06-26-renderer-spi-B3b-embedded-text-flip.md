# Renderer SPI — Plan B3b: Embedded-Text Live Flip

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move embedded-font glyph rendering into a SkiaSharp-free core `CoreTextRenderer` and flip `PdfRenderer.OnShowText`/`OnShowTextWithPositioning` to emit `FillPath`/`StrokePath` instead of `DrawText` — staying pixel-identical (90 render tests gate it). Non-embedded fonts still route through `DrawText` (moved core-side in B3c).

**Architecture:** `CoreTextRenderer` (core, internal) ports `TextRenderer.TryRenderWithGlyphOutlines` + `ResolveGlyphId` + `AdvancePosition` + the render-mode/em-dash logic, but instead of building an `SKPath` and pushing a canvas matrix it composes the B3a primitives: `GlyphPathService.GetGlyphPath` (cached glyph-space path) → `GlyphPlacement.GlyphToUser` (the Y-flip-compensated glyph→user `Matrix3x2`) → `IPathBuilder.Transform` (positions a copy) → `IRenderTarget.FillPath(userPath, state, evenOdd: true)` / `StrokePath`. The composition was *proven* equivalent to the original `combined = canvasMatrix × skGlyphMatrix; DrawPath` in B3a's final review. `PdfRenderer` tries `CoreTextRenderer.Render`; if the font has no valid embedded metrics it returns `false` and `PdfRenderer` falls back to `_target.DrawText`.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, xUnit. No new dependencies.

## Global Constraints (copied from B3a's final review — the ONLY regression paths)

- **Glyph fills MUST use `evenOdd: true`** at every glyph `FillPath` call (the original set `FillType=EvenOdd` on the cached path; `PathBuilder` carries no fill rule).
- **The render target's CTM already carries the page Y-flip** (applied in `BeginPage`); `GlyphPlacement.YFlipCompensate` exists only to counter that. Do not double-flip.
- **Stroke (Tr 1/2/5/6):** call `StrokePath(userPath, state)` — the target's canvas applies page-scale × CTM, giving the same device stroke width the original computed via `canvasScale`. (The 0.5-device-px floor is the target's responsibility.)
- **Faux-bold:** stroke the fill outline with the FILL color at width `FontSize * 0.04 * glyphToUserScale` (user space). The `0.5`-device floor is dropped (rare; sub-pixel; gate on tests).
- **Tr 4–7 clip is NOT implemented** today — keep it that way (4/6 fill, 5/6 stroke, no clip) to stay pixel-identical.
- Core `PdfLibrary` stays SkiaSharp-free; multi-target net8/9/10.
- **THE GATE: the 90 rendering tests (`PdfLibrary.Tests/Rendering/...`) must stay pixel-identical.** A wrong matrix/fill-rule shifts, flips, or solid-fills ALL embedded text. The full suite (1259 on the CI filter) must stay green.

---

## File Structure

- `PdfLibrary/Rendering/CoreTextRenderer.cs` (create, internal) — the embedded-text pipeline.
- `PdfLibrary/Rendering/PdfRenderer.cs` (modify) — instantiate `CoreTextRenderer`; replace the two `_target.DrawText` calls with try-core-then-fallback.
- `PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs` (create) — `MockRenderTarget`-based.

---

## Task 1: `CoreTextRenderer` — the core embedded-text pipeline

**Files:**
- Create: `PdfLibrary/Rendering/CoreTextRenderer.cs`
- Test: `PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs`

**Background:** Faithful port of `TextRenderer.TryRenderWithGlyphOutlines` (472–601), `ResolveGlyphId` (603–702), `RenderEmDashFallback` (704–744), and `AdvancePosition` (964–973), with `RenderGlyph`'s draw replaced by the B3a primitives. `ResolveGlyphId` is pure core-type logic and ports verbatim. **Verify** the exact `PdfFont`/`EmbeddedFontMetrics`/`PdfGraphicsState` member names against the source as you go (they are all core types the SkiaSharp `TextRenderer` already calls — the brief lists them) and adjust only if a name differs.

**Interfaces:**
- Consumes: `GlyphPathService`, `GlyphPlacement.GlyphToUser`, `IPathBuilder.Transform`, `IRenderTarget.FillPath/StrokePath`, and core font types (`PdfFont.GetEmbeddedMetrics/GetDescriptor/Encoding/BaseFont`, `EmbeddedFontMetrics`, `Type0Font`, `CidFont`, `GlyphList`, `PdfGraphicsState`).
- Produces: `internal sealed class CoreTextRenderer` with `CoreTextRenderer(IRenderTarget target, GlyphPathService glyphPaths)` and `bool Render(string text, List<double> glyphWidths, PdfGraphicsState state, PdfFont? font, List<int>? charCodes)` — returns `false` (no valid embedded metrics) to signal the caller to fall back.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs`. It renders real embedded-font text through a `MockRenderTarget` that records `FillPath` calls and asserts glyphs were emitted. (If `MockRenderTarget` does not already record `FillPath`, extend it minimally to capture the calls — see the note after the code.)

```csharp
using PdfLibrary.Content;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

public class CoreTextRendererTests
{
    [Fact]
    public void Render_EmbeddedFontText_EmitsFillPathPerVisibleGlyph()
    {
        // Build a one-page PDF that draws "AB" with the embedded PublicPixel font, load it,
        // and run a page render through a recording target; assert the core text path fired.
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

        byte[] pdf = PdfDocumentBuilder.Create()
            .LoadFont(fontBytes, "Pixel")
            .AddPage(p => p.AddText("AB", 100, 700, "Pixel", 24))
            .ToByteArray();

        using var ms = new MemoryStream(pdf);
        using PdfDocument doc = PdfDocument.Load(ms);

        var target = new RecordingRenderTarget();
        doc.Pages[0].Render(target);

        // Two visible glyphs (A, B) → at least two glyph FillPath calls, all even-odd, non-empty.
        Assert.True(target.FillPaths.Count >= 2, $"expected >=2 fills, got {target.FillPaths.Count}");
        Assert.All(target.FillPaths, f =>
        {
            Assert.True(f.EvenOdd);
            Assert.False(f.Path.IsEmpty);
        });
    }

    // Minimal recording target — captures FillPath; everything else is a no-op/stub.
    private sealed class RecordingRenderTarget : IRenderTarget
    {
        public List<(IPathBuilder Path, bool EvenOdd)> FillPaths { get; } = [];
        public int CurrentPageNumber { get; private set; }

        public void BeginPage(int pageNumber, double width, double height, double scale = 1.0,
            double cropOffsetX = 0, double cropOffsetY = 0, int rotation = 0) => CurrentPageNumber = pageNumber;
        public void EndPage() { }
        public void Clear() { }
        public void FillPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd)
            => FillPaths.Add((path.Clone(), evenOdd));
        public void StrokePath(IPathBuilder path, PdfGraphicsState state) { }
        public void FillAndStrokePath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void FillPathWithTilingPattern(IPathBuilder path, PdfGraphicsState state, bool evenOdd,
            PdfTilingPattern pattern, Action<IRenderTarget> renderPatternContent) { }
        public void SetClippingPath(IPathBuilder path, PdfGraphicsState state, bool evenOdd) { }
        public void DrawText(string text, List<double> glyphWidths, PdfGraphicsState state,
            PdfFont? font, List<int>? charCodes = null) { }
        public float MeasureTextWidth(string text, PdfGraphicsState state, PdfFont font) => 0;
        public void DrawImage(PdfImage image, PdfGraphicsState state) { }
        public void SaveState() { }
        public void RestoreState() { }
        public void ApplyCtm(System.Numerics.Matrix3x2 ctm) { }
        public void OnGraphicsStateChanged(PdfGraphicsState state) { }
        public void RenderSoftMask(string maskSubtype, Action<IRenderTarget> renderMaskContent) { }
        public void ClearSoftMask() { }
        public (int width, int height, double scale) GetPageDimensions() => (600, 800, 1.0);
    }
}
```

> If `IRenderTarget` has members not stubbed above (the interface has default-no-op `PaintShading`/`FillPathWithShadingPattern`), the compiler will tell you; add the missing stub. Read `IRenderTarget.cs` to confirm the exact member set.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CoreTextRendererTests"`
Expected: FAIL — `CoreTextRenderer` does not exist AND the render still routes through `DrawText` (so `FillPaths` would be empty even once it compiles). Both confirm RED.

- [ ] **Step 3: Write the implementation**

Create `PdfLibrary/Rendering/CoreTextRenderer.cs`. (`ResolveGlyphId`'s body is transcribed verbatim from `TextRenderer.cs:603-702` — keep it exactly; it is pure font logic.)

```csharp
using System.Numerics;
using PdfLibrary.Content;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Rendering;

/// <summary>
/// Renders embedded-font text by resolving each glyph to a path and emitting FillPath/StrokePath
/// to the render target — the SkiaSharp-free core replacement for the Skia TextRenderer's embedded
/// path. Returns false from Render when the font has no valid embedded metrics so the caller can
/// fall back to the (still Skia-side) DrawText path for non-embedded fonts.
/// </summary>
internal sealed class CoreTextRenderer(IRenderTarget target, GlyphPathService glyphPaths)
{
    public bool Render(string text, List<double> glyphWidths, PdfGraphicsState state,
        PdfFont? font, List<int>? charCodes)
    {
        if (font is null) return false;
        try
        {
            EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
            if (metrics is not { IsValid: true }) return false;

            bool applyBold = ShouldApplyFauxBold(font);
            var tHs = (float)state.HorizontalScaling / 100f;
            double currentX = 0;
            int loopCount = charCodes?.Count ?? text.Length;

            for (var i = 0; i < loopCount; i++)
            {
                ushort charCode = charCodes is not null && i < charCodes.Count
                    ? (ushort)charCodes[i]
                    : text[i];

                ushort glyphId = ResolveGlyphId(metrics, font, charCode, out string? resolvedGlyphName);

                if (glyphId == 0) { Advance(ref currentX, glyphWidths, i, state); continue; }

                GlyphOutline? outline = metrics.IsType1Font && resolvedGlyphName is not null
                    ? metrics.GetGlyphOutlineByName(resolvedGlyphName)
                    : metrics.GetGlyphOutline(glyphId);

                if (outline is null)
                {
                    if (charCode == 151 && i < glyphWidths.Count && glyphWidths[i] > 0.1)
                        RenderEmDash(glyphWidths[i], state, currentX, tHs);
                    Advance(ref currentX, glyphWidths, i, state);
                    continue;
                }
                if (outline.IsEmpty) { Advance(ref currentX, glyphWidths, i, state); continue; }

                IPathBuilder glyphSpace =
                    glyphPaths.GetGlyphPath(metrics, glyphId, (float)state.FontSize, outline, resolvedGlyphName);
                Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs);
                IPathBuilder userPath = glyphSpace.Transform(toUser);

                EmitGlyph(userPath, state, toUser, applyBold);
                Advance(ref currentX, glyphWidths, i, state);
            }

            return true;
        }
        catch
        {
            // Any failure: report not-handled so the caller falls back to DrawText.
            return false;
        }
    }

    private void EmitGlyph(IPathBuilder userPath, PdfGraphicsState state, Matrix3x2 toUser, bool applyBold)
    {
        int rm = state.RenderingMode;
        bool fill = rm is 0 or 2 or 4 or 6;
        bool stroke = rm is 1 or 2 or 5 or 6;
        bool invisible = rm is 3 or 7;
        if (invisible) return;

        if (fill) target.FillPath(userPath, state, evenOdd: true);

        if (stroke)
        {
            target.StrokePath(userPath, state);
        }
        else if (applyBold && fill)
        {
            // Synthetic bold: stroke the fill outline with the FILL color, ~4% em in user space.
            double scaleU = Math.Sqrt(Math.Abs(toUser.M11 * toUser.M22 - toUser.M12 * toUser.M21));
            double boldWidthUser = state.FontSize * 0.04 * scaleU;

            PdfGraphicsState bold = state.Clone();
            bold.LineWidth = boldWidthUser;
            bold.ResolvedStrokeColor = state.ResolvedFillColor;
            bold.ResolvedStrokeColorSpace = state.ResolvedFillColorSpace;
            bold.StrokeAlpha = state.FillAlpha;
            target.StrokePath(userPath, bold);
        }
    }

    private void RenderEmDash(double glyphWidth, PdfGraphicsState state, double currentX, float tHs)
    {
        // Em dash fallback (port of RenderEmDashFallback): a rectangle in glyph space, positioned
        // through the same glyph->user matrix (which applies the Y-flip).
        var emDashY = (float)state.FontSize * 0.35f;
        var emDashHeight = (float)state.FontSize * 0.06f;
        var emDashWidth = (float)glyphWidth * (float)state.FontSize;

        var rect = new PathBuilder();
        // Original SKRect(0, -emDashY-emDashHeight, emDashWidth, -emDashY) => x,y,w,h:
        rect.Rectangle(0, -emDashY - emDashHeight, emDashWidth, emDashHeight);

        Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs);
        IPathBuilder userPath = rect.Transform(toUser);
        target.FillPath(userPath, state, evenOdd: true);
    }

    private static bool ShouldApplyFauxBold(PdfFont font)
    {
        PdfFontDescriptor? descriptor = font.GetDescriptor();
        if (descriptor is null) return false;
        bool isForceBoldFlag = descriptor.IsBold;
        bool isBoldName = font.BaseFont?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true;
        bool isBoldStemV = descriptor.StemV >= 120;
        bool embeddedOutlineAlreadyBold = isBoldName || isBoldStemV;
        return isForceBoldFlag && !embeddedOutlineAlreadyBold;
    }

    private static void Advance(ref double currentX, List<double> glyphWidths, int i, PdfGraphicsState state)
    {
        if (i >= glyphWidths.Count) return;
        // XOR: FontSize<0 and TextMatrix.M11<0 each flip; two flips cancel.
        bool flipX = state.FontSize < 0 != state.TextMatrix.M11 < 0;
        currentX += glyphWidths[i] * (flipX ? -1.0 : 1.0);
    }

    // === Verbatim port of TextRenderer.ResolveGlyphId (603-702) ===
    private static ushort ResolveGlyphId(EmbeddedFontMetrics metrics, PdfFont font, ushort charCode,
        out string? resolvedGlyphName)
    {
        ushort glyphId;
        resolvedGlyphName = null;

        if ((metrics.IsCffFont || metrics.IsType1Font) && font.Encoding is not null)
        {
            resolvedGlyphName = font.Encoding.GetGlyphName(charCode);
            glyphId = resolvedGlyphName is not null ? metrics.GetGlyphIdByName(resolvedGlyphName) : (ushort)0;

            if (glyphId == 0 && metrics.IsType1Font)
            {
                string? builtInName = metrics.GetType1GlyphNameByCharCode(charCode);
                if (builtInName is not null)
                {
                    resolvedGlyphName = builtInName;
                    glyphId = metrics.GetGlyphIdByName(builtInName);
                }
            }
        }
        else if (font is Type0Font type0Font && metrics.IsType1Font && type0Font.ToUnicode is not null)
        {
            string? unicode = type0Font.ToUnicode.Lookup(charCode);
            if (unicode is not null)
            {
                resolvedGlyphName = GlyphList.GetGlyphName(unicode);
                if (resolvedGlyphName is not null)
                {
                    glyphId = metrics.GetGlyphIdByName(resolvedGlyphName);
                }
                else if (unicode.Length == 1 && char.IsAscii(unicode[0]))
                {
                    resolvedGlyphName = unicode;
                    glyphId = metrics.GetGlyphIdByName(resolvedGlyphName);
                }
                else
                {
                    glyphId = 0;
                }
            }
            else if (type0Font.DescendantFont is CidFont cidFont)
            {
                glyphId = (ushort)cidFont.MapCidToGid(charCode);
            }
            else
            {
                glyphId = metrics.GetGlyphId(charCode);
            }
        }
        else
        {
            if (font is Type0Font { DescendantFont: CidFont cidFont })
            {
                int cidAfterMap = cidFont.MapCidToGid(charCode);
                glyphId = metrics.IsCffFont
                    ? metrics.GetGlyphIdByCid((ushort)cidAfterMap)
                    : (ushort)cidAfterMap;
            }
            else
            {
                glyphId = metrics.GetGlyphId(charCode);
            }
        }

        return glyphId;
    }
}
```

> **Verify-as-you-go (only if RED reveals a name mismatch):** `PdfGraphicsState` must expose settable `LineWidth`, `ResolvedStrokeColor`, `ResolvedStrokeColorSpace`, `StrokeAlpha`, and read `ResolvedFillColor`/`ResolvedFillColorSpace`/`FillAlpha`/`RenderingMode`/`HorizontalScaling`/`FontSize`/`TextMatrix`, plus `Clone()`. The SkiaSharp `RenderGlyph` reads all of these, so they exist; only the exact names may need confirming against `PdfGraphicsState.cs`. If faux-bold's color fields are not settable, set the bold stroke color through whatever setter the state exposes (the goal: stroke with the FILL color). The em-dash and the main fill/stroke paths do not depend on any of that.

- [ ] **Step 4: This task alone does NOT make the test pass** — the test renders a whole page, which still routes through `DrawText` until Task 2 wires `CoreTextRenderer` in. Confirm the project COMPILES and the focused `GlyphPathService`/`GlyphPlacement` tests still pass:

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --nologo`
Expected: Build succeeded. (The `CoreTextRendererTests` page test stays RED until Task 2 — that is expected and correct; do not force it green here.)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/CoreTextRenderer.cs PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs
git commit -m "feat(render): CoreTextRenderer — SkiaSharp-free embedded-text pipeline (not yet wired)"
```

---

## Task 2: Flip `PdfRenderer` to the core pipeline (THE GATE)

**Files:**
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs`

**Background:** `PdfRenderer.OnShowText` (line 881) and `OnShowTextWithPositioning` (line 1173) currently call `_target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes)`. Replace each with: try `CoreTextRenderer.Render(...)`; if it returns `false` (non-embedded font), fall back to `_target.DrawText(...)`. This is the live flip — embedded text now renders through the core pipeline.

**Interfaces:**
- Consumes: `CoreTextRenderer` (Task 1), `GlyphPathService`.

- [ ] **Step 1: Add the `CoreTextRenderer` field**

In `PdfLibrary/Rendering/PdfRenderer.cs`, add a private field initialized from the existing `_target` (find the field declarations near the top; `_target` is the `IRenderTarget`). Add:

```csharp
    private readonly CoreTextRenderer _coreText;
```

and initialize it in the constructor (the constructor is `internal PdfRenderer(IRenderTarget target, ...)` around line 40) right after `_target` is assigned:

```csharp
        _coreText = new CoreTextRenderer(target, new GlyphPathService());
```

(Per-renderer cache is fine — a `PdfRenderer` is created per render. The original used a static cache for cross-render reuse; revisit only if profiling shows it matters.)

- [ ] **Step 2: Flip the two call sites**

Replace the `OnShowText` call (line 881):

```csharp
        // Render the text: embedded fonts go through the core glyph pipeline; non-embedded
        // fonts fall back to the target's DrawText (moved core-side in a later plan).
        if (!_coreText.Render(textToRender, glyphWidths, CurrentState, font, charCodes))
            _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);
```

Replace the `OnShowTextWithPositioning` call (line ~1173) identically — find the `_target.DrawText(...)` there and wrap it the same way:

```csharp
        if (!_coreText.Render(textToRender, glyphWidths, CurrentState, font, charCodes))
            _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);
```

(Use the actual local variable names at each site — confirm by reading the surrounding lines; the variables are the decoded text, the per-glyph widths list, and the char codes already computed there.)

- [ ] **Step 3: Run the embedded-text test from Task 1 — now it should PASS**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CoreTextRendererTests"`
Expected: PASS — embedded "AB" now emits `FillPath` per glyph.

- [ ] **Step 4: THE GATE — the 90 render tests must stay pixel-identical**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLibrary.Tests.Rendering" --nologo`
Expected: PASS — all rendering tests green (these rasterize pages through `SkiaSharpRenderTarget` and compare pixels; embedded text now comes from the core pipeline and must match byte-for-byte).

**If any render test fails, STOP and diagnose — do not weaken the test.** Likely causes, in order: (a) a glyph fill not using `evenOdd: true` (counters fill solid); (b) the Y-flip applied twice or not at all (text upside-down or mirrored); (c) `GlyphOutlineToPath` not matching the old converter's scale/flip; (d) stroke/faux-bold width. Compare a failing rendered page against the expected to localize. Report findings if the cause isn't one of these.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/PdfRenderer.cs
git commit -m "feat(render): flip PdfRenderer embedded text to the core FillPath pipeline"
```

---

## Task 3: Full verification + viewer check

**Files:** none (verification only)

- [ ] **Step 1: Full suite (CI filter)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`
Expected: PASS, 0 failures.

- [ ] **Step 2: Core still SkiaSharp-free**

Run: `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/`
Expected: no output.

- [ ] **Step 3: Release builds warning-free (core + renderer + viewer)**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo`, `dotnet build PdfLibrary.Rendering.SkiaSharp/PdfLibrary.Rendering.SkiaSharp.csproj -c Release --nologo`, `dotnet build PdfLibrary.Wpf.Viewer/PdfLibrary.Wpf.Viewer.csproj -c Debug --nologo`
Expected: `0 Warning(s)`, `0 Error(s)` for each (the viewer must still build — it renders through `SkiaSharpRenderTarget`, which is unchanged).

- [ ] **Step 4: Manual viewer check (human-in-the-loop)**

Build/run `PdfLibrary.Wpf.Viewer` and open a few **embedded-font** PDFs. Embedded text now renders through the core `FillPath` pipeline; confirm it looks identical to before (no shifted/mirrored/solid-filled glyphs). Non-embedded (Base-14-only) PDFs still use the old fallback — those are validated in B3c. This is a human sanity check on top of the automated pixel gate; note any visual anomaly.

- [ ] **Step 5: No commit (verification only).**

---

## Self-Review Notes

- **Spec coverage:** flips embedded-font text to the thin-target geometry path (`FillPath`/`StrokePath`), composing the B3a primitives exactly as the design intends. `ResolveGlyphId` and `AdvancePosition` ported verbatim; render-mode/em-dash logic preserved; Tr 4–7 clip deliberately left unimplemented (parity).
- **The constraints are honored in code:** `evenOdd: true` on every glyph fill; `GlyphPlacement` (not a second flip) for the Y-flip; stroke via `StrokePath(userPath, state)`; faux-bold strokes with the fill color in user space.
- **The risk is the 90-render-test gate (Task 2 Step 4)** — it is the arbiter of pixel parity. Everything upstream (B3a) was proven exact, so a failure points at the wiring/composition, not the math.

## Out of scope → Plan B3c

- Core fallback for non-embedded fonts: `CoreTextRenderer` (or a sibling) loads bytes via `SystemFontLocator.GetFontData` (Plan A) → `SfntFont`/`EmbeddedFontMetrics` (Plan B/B2, incl. the `.ttc` face selector) → outlines → the same `FillPath` path; ligature decomposition; DejaVu width-fixup. Then `PdfRenderer` no longer needs the `DrawText` fallback.
- Remove `DrawText`/`MeasureTextWidth` from `IRenderTarget`; delete the SkiaSharp `TextRenderer` + `GlyphToSKPathConverter`; update `SkiaSharpRenderTargetForPattern`. (`MeasureTextWidth` has zero callers.)
- Add CFF and Type1 font fixtures (B3a flagged: `GlyphPathService`'s CFF/Type1 branches are currently only reached transitively).
