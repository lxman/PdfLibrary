# Renderer SPI — Plan B3c: Core Font Fallback + Remove the Text SPI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render non-embedded (Base-14 / unembedded) fonts in the SkiaSharp-free core by locating a system substitute and drawing its glyph outlines through the same `FillPath` pipeline as embedded fonts — then delete `DrawText`/`MeasureTextWidth` from `IRenderTarget` and the SkiaSharp `TextRenderer`/`GlyphToSKPathConverter`. After this, the render SPI is geometry-only.

**Architecture:** A new core `SubstituteFontResolver` classifies a PDF font (descriptor flags + BaseFont name heuristics, ported from `TextRenderer.RenderWithFallbackFont`), maps it to a synthetic Standard-14 name, and uses `ISystemFontProvider.GetFontData` (Plan A's `SystemFontLocator`) to load substitute bytes → a cached `EmbeddedFontMetrics`. `CoreTextRenderer` gains a non-embedded branch: for each decoded Unicode char, `substituteMetrics.GetGlyphId(unicode)` → the **existing** `GlyphPathService`/`GlyphPlacement.GlyphToUser * state.Ctm`/`EmitGlyph` path. `PdfRenderer` injects a `SystemFontLocator` and drops the `DrawText` fallback; the now-dead `DrawText`/`MeasureTextWidth` surface and the SkiaSharp text classes are deleted.

**Tech Stack:** C# 12, .NET 8/9/10, `System.Numerics`, xUnit. No new dependencies.

## Global Constraints

- **Bake the CTM:** every glyph (embedded AND substitute) positions via `GlyphPlacement.GlyphToUser(state, currentX, tHs) * state.Ctm` — `FillPath` applies only the page initial-transform, never the CTM. (This is the bug fixed in `8429607`; the fallback path must not reintroduce it.)
- **Advance comes from `glyphWidths`** (PDF-space, computed by `PdfRenderer`), never from substitute-font metrics — identical to the embedded path.
- Core `PdfLibrary` stays SkiaSharp-free; multi-target net8/9/10; new types `internal`.
- **Render-test gate is by category:** *embedded-font* render output must stay pixel-identical (as in B3b). *Non-embedded* output legitimately CHANGES (Skia `SKFont` → core outlines of a system substitute) — the tolerant/structural render tests must still pass, but pixel drift on non-embedded text is expected, not a regression. Call it out explicitly when it happens.
- The full suite (1261 on the CI filter) stays green.

---

## File Structure

- `PdfLibrary/Fonts/SubstituteFontResolver.cs` (create, internal) — classify + locate + cache substitute `EmbeddedFontMetrics`.
- `PdfLibrary/Rendering/CoreTextRenderer.cs` (modify) — inject `ISystemFontProvider`; add the non-embedded branch + per-glyph robustness.
- `PdfLibrary/Rendering/PdfRenderer.cs` (modify) — inject `SystemFontLocator`; drop the `DrawText` fallback; thread an optional provider for the test seam.
- `PdfLibrary/Document/PdfPage.cs` (modify) — internal `Render` overload accepting `ISystemFontProvider` (test seam).
- `PdfLibrary/Rendering/IRenderTarget.cs` (modify) — remove `DrawText` + `MeasureTextWidth`.
- SkiaSharp package: `SkiaSharpRenderTarget.cs`, `SkiaSharpRenderTargetForPattern.cs` (modify — remove the two members + the `TextRenderer` field); **delete** `Rendering/TextRenderer.cs`, `Rendering/GlyphToSKPathConverter.cs`.
- Tests: `MockRenderTarget.cs`, `CoreTextRendererTests.cs` (remove stubs); **delete** `GlyphToSKPathConverterTests.cs`; new `SubstituteFontResolverTests.cs`.

---

## Task 1: `SubstituteFontResolver` — classify, locate, cache

**Files:**
- Create: `PdfLibrary/Fonts/SubstituteFontResolver.cs`
- Test: `PdfLibrary.Tests/Fonts/SubstituteFontResolverTests.cs`

**Background:** Ports the classification from `TextRenderer.RenderWithFallbackFont` (descriptor flags `IsBold`/`IsItalic`/`IsSerif`/`IsFixedPitch` + `StemV>=120`; BaseFont name heuristics for Bold/Italic/Oblique, Courier/Consolas/Monaco/Mono, Times/Serif/Georgia/Palatino/Garamond/Cambria/Bodoni/Century/Bookman). Instead of a Skia family name it produces a synthetic Standard-14 name (`Helvetica`/`Times`/`Courier` + `-Bold`/`-Italic`/`-BoldItalic`) and feeds it to `ISystemFontProvider.GetFontData` — which routes through `Standard14Fonts.SubstituteFileBaseNames` to a real system font file. It first tries the raw `BaseFont` (so genuine Std-14 names like `Symbol`/`ZapfDingbats` resolve precisely), then the synthetic name. Results (including null) are cached by `BaseFont` so the same substitute `EmbeddedFontMetrics` instance is reused — keeping `GlyphPathService`'s identity-keyed cache effective.

**Interfaces:**
- Consumes: `ISystemFontProvider.GetFontData(string)`, `PdfFontDescriptor` (`IsBold`/`IsItalic`/`IsSerif`/`IsFixedPitch`/`StemV`), `EmbeddedFontMetrics(byte[])`.
- Produces:
  - `internal sealed class SubstituteFontResolver(ISystemFontProvider provider)`
  - `EmbeddedFontMetrics? Resolve(string baseFont, PdfFontDescriptor? descriptor)` — cached.
  - `internal static (bool serif, bool mono, bool bold, bool italic) Classify(string baseFont, PdfFontDescriptor? descriptor)` — pure, testable.
  - `internal static string SyntheticStd14Name(bool serif, bool mono, bool bold, bool italic)` — pure, testable.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/SubstituteFontResolverTests.cs`:

```csharp
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts;

public class SubstituteFontResolverTests
{
    [Theory]
    [InlineData("Times-Bold", false, false, true, false, true, false)]      // serif, bold
    [InlineData("CourierNewPSMT", false, false, false, true, false, false)] // mono
    [InlineData("Helvetica-Oblique", false, false, false, false, false, true)] // sans, italic
    [InlineData("ABCDEF+Garamond", false, false, true, false, false, false)] // serif by name
    public void Classify_FromName_NoDescriptor(string baseFont, bool _, bool __,
        bool expectSerif, bool expectMono, bool expectBold, bool expectItalic)
    {
        (bool serif, bool mono, bool bold, bool italic) = SubstituteFontResolver.Classify(baseFont, null);
        Assert.Equal(expectSerif, serif);
        Assert.Equal(expectMono, mono);
        Assert.Equal(expectBold, bold);
        Assert.Equal(expectItalic, italic);
    }

    [Theory]
    [InlineData(false, false, false, false, "Helvetica")]
    [InlineData(true, false, true, false, "Times-Bold")]
    [InlineData(false, true, false, true, "Courier-Italic")]
    [InlineData(true, false, true, true, "Times-BoldItalic")]
    public void SyntheticStd14Name_Maps(bool serif, bool mono, bool bold, bool italic, string expected)
        => Assert.Equal(expected, SubstituteFontResolver.SyntheticStd14Name(serif, mono, bold, italic));

    [Fact]
    public void Resolve_LoadsAndCachesSubstituteMetrics()
    {
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        var provider = new FakeProvider(fontBytes);

        var resolver = new SubstituteFontResolver(provider);
        EmbeddedFontMetrics? m1 = resolver.Resolve("Helvetica", null);
        EmbeddedFontMetrics? m2 = resolver.Resolve("Helvetica", null);

        Assert.NotNull(m1);
        Assert.True(m1!.IsValid);
        Assert.Same(m1, m2);                       // cached by BaseFont
        Assert.True(provider.Requested.Count >= 1); // asked the provider for bytes
    }

    private sealed class FakeProvider(byte[] bytes) : ISystemFontProvider
    {
        public List<string> Requested { get; } = [];
        public byte[]? GetFontData(string baseFontName) { Requested.Add(baseFontName); return bytes; }
        public IEnumerable<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => true;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SubstituteFontResolverTests"`
Expected: FAIL — `SubstituteFontResolver` does not exist.

- [ ] **Step 3: Implement**

Create `PdfLibrary/Fonts/SubstituteFontResolver.cs`:

```csharp
using System.Collections.Concurrent;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// Resolves a non-embedded PDF font to a system substitute font, parsed as EmbeddedFontMetrics so
/// the core text pipeline can render its glyph outlines exactly like an embedded font. SkiaSharp-free:
/// font classification ported from TextRenderer.RenderWithFallbackFont, byte loading via
/// ISystemFontProvider (Plan A's SystemFontLocator). Cached by BaseFont so the same substitute
/// instance is reused (keeps GlyphPathService's identity-keyed cache effective).
/// </summary>
internal sealed class SubstituteFontResolver(ISystemFontProvider provider)
{
    private readonly ConcurrentDictionary<string, EmbeddedFontMetrics?> _cache = new();

    public EmbeddedFontMetrics? Resolve(string baseFont, PdfFontDescriptor? descriptor)
        => _cache.GetOrAdd(baseFont ?? "", _ => Load(baseFont ?? "", descriptor));

    private EmbeddedFontMetrics? Load(string baseFont, PdfFontDescriptor? descriptor)
    {
        // Try the raw BaseFont first (resolves genuine Standard-14 names incl. Symbol/ZapfDingbats
        // precisely), then a synthetic name from classification (covers arbitrary subset names).
        byte[]? bytes = provider.GetFontData(baseFont);
        if (bytes is null)
        {
            (bool serif, bool mono, bool bold, bool italic) = Classify(baseFont, descriptor);
            bytes = provider.GetFontData(SyntheticStd14Name(serif, mono, bold, italic));
        }
        if (bytes is null) return null;

        var metrics = new EmbeddedFontMetrics(bytes);
        return metrics.IsValid ? metrics : null;
    }

    public static (bool serif, bool mono, bool bold, bool italic) Classify(
        string baseFont, PdfFontDescriptor? descriptor)
    {
        var bold = false; var italic = false; var serif = false; var mono = false;
        if (descriptor is not null)
        {
            bold = descriptor.IsBold || descriptor.StemV >= 120;
            italic = descriptor.IsItalic;
            serif = descriptor.IsSerif;
            mono = descriptor.IsFixedPitch;
        }

        string name = baseFont ?? "";
        if (name.Contains("Bold", StringComparison.OrdinalIgnoreCase)) bold = true;
        if (name.Contains("Italic", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Oblique", StringComparison.OrdinalIgnoreCase)) italic = true;
        if (name.Contains("Courier", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Consolas", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Monaco", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Mono", StringComparison.OrdinalIgnoreCase)) mono = true;
        if (name.Contains("Times", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Serif", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Georgia", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Palatino", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Garamond", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cambria", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bodoni", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Century", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Bookman", StringComparison.OrdinalIgnoreCase)) serif = true;

        return (serif, mono, bold, italic);
    }

    public static string SyntheticStd14Name(bool serif, bool mono, bool bold, bool italic)
    {
        string family = mono ? "Courier" : serif ? "Times" : "Helvetica";
        string style = (bold, italic) switch
        {
            (true, true) => "-BoldItalic",
            (true, false) => "-Bold",
            (false, true) => "-Italic",
            _ => ""
        };
        return family + style;
    }
}
```

(Note: `Standard14Fonts.SubstituteFileBaseNames` parses the synthetic `-Bold`/`-Italic`/`-BoldItalic` suffix via `Contains("bold")`/`Contains("italic")`, so these names resolve to the correct Liberation/Nimbus styles. `mono` takes priority over `serif`, matching the original.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SubstituteFontResolverTests"`
Expected: PASS (3 facts/theories). If `PdfFontDescriptor` member names differ (`IsFixedPitch`/`IsSerif`/`IsBold`/`IsItalic`/`StemV`), confirm against `PdfFontDescriptor.cs` and adjust `Classify` (the SkiaSharp `RenderWithFallbackFont` reads exactly these, so they exist).

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/SubstituteFontResolver.cs PdfLibrary.Tests/Fonts/SubstituteFontResolverTests.cs
git commit -m "feat(fonts): SubstituteFontResolver — classify + locate + cache system substitute metrics"
```

---

## Task 2: `CoreTextRenderer` non-embedded branch + provider injection (self-contained)

**Files:**
- Modify: `PdfLibrary/Rendering/CoreTextRenderer.cs`
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs` (ctor gains an optional provider)
- Modify: `PdfLibrary/Document/PdfPage.cs` (internal `Render(target, provider)` seam)
- Test: `PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs`

**Background:** When `font.GetEmbeddedMetrics()` is null/invalid, instead of `return false`, render the decoded Unicode `text` via a substitute font. Reuse the embedded per-glyph machinery exactly — only the glyph-id source differs (`substituteMetrics.GetGlyphId((ushort)unicodeChar)` via the substitute's cmap) and ligatures are decomposed (the substitute may lack `ﬀ`…`ﬄ`). Harden the per-glyph render with a try/catch so one bad glyph skips rather than abandoning the whole run — important now that no `DrawText` fallback sits behind it. To make this testable (and to seed the client-supplied-fonts hook from the design), the provider is injected through `PdfRenderer` and an internal `PdfPage.Render(target, provider)` overload. **This task does NOT remove the `DrawText` fallback** — it stays (now unreachable for non-embedded fonts, since `Render` returns `true`) until Task 3; the build and all tests stay green throughout.

**Interfaces:**
- Consumes: `SubstituteFontResolver` (Task 1), `ISystemFontProvider`, `SystemFontLocator`, existing `GlyphPathService`/`GlyphPlacement.GlyphToUser`/`EmitGlyph`.
- Produces: `CoreTextRenderer(IRenderTarget, GlyphPathService, ISystemFontProvider)` (new ctor param); `PdfRenderer(... , ISystemFontProvider? fontProvider = null)`; `internal void PdfPage.Render(IRenderTarget, ISystemFontProvider?)`.

- [ ] **Step 1: Write the failing test** (self-contained — the seam is added in this task)

Add to `PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs` (the `RecordingRenderTarget` already exists there):

```csharp
[Fact]
public void Render_NonEmbeddedFont_RendersViaSubstituteOutlines()
{
    // A page drawing non-embedded "Helvetica" text. With a provider that returns a real font
    // (PublicPixel) as the substitute, the core must emit FillPath per glyph via the substitute's
    // outlines — not return false (there is no DrawText fallback for the geometry SPI).
    byte[] subst = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    byte[] pdf = PdfDocumentBuilder.Create()
        .AddPage(p => p.AddText("Hi", 100, 700, "Helvetica", 24))
        .ToByteArray();

    using var ms = new MemoryStream(pdf);
    using PdfDocument doc = PdfDocument.Load(ms);

    var target = new RecordingRenderTarget();
    doc.GetPage(0)!.Render(target, new StubProvider(subst)); // internal seam added in this task

    Assert.True(target.FillPaths.Count >= 1, $"expected substitute glyph fills, got {target.FillPaths.Count}");
}

private sealed class StubProvider(byte[] bytes) : PdfLibrary.Fonts.ISystemFontProvider
{
    public byte[]? GetFontData(string baseFontName) => bytes;
    public IEnumerable<string> GetAvailableFontFamilies() => [];
    public bool IsFontAvailable(string familyName) => true;
    public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
    public void RefreshCache() { }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CoreTextRendererTests.Render_NonEmbeddedFont"`
Expected: FAIL — no `Render(target, provider)` overload yet (and no non-embedded branch).

- [ ] **Step 3a: Implement the non-embedded branch + robustness in `CoreTextRenderer.cs`**

(a) Add the provider to the primary constructor and a resolver field (add `using PdfLibrary.Fonts;` / `using PdfLibrary.Fonts.Embedded;` if not present):
```csharp
internal sealed class CoreTextRenderer(
    IRenderTarget target, GlyphPathService glyphPaths, ISystemFontProvider fontProvider)
{
    private readonly SubstituteFontResolver _substitutes = new(fontProvider);
```

(b) Replace the early `return false` for missing embedded metrics with a call into the fallback:
```csharp
EmbeddedFontMetrics? metrics = font.GetEmbeddedMetrics();
if (metrics is not { IsValid: true })
    return RenderWithSubstitute(text, glyphWidths, state, font);
```

(c) Wrap the existing embedded per-glyph `EmitGlyph(...)` call in a try/catch so one glyph can't abandon the run (keep the outer `try/catch` that returns false — it now guards setup, not per-glyph drawing):
```csharp
try { EmitGlyph(userPath, state, toUser, applyBold); }
catch (Exception ex) { PdfLogger.Log(LogCategory.Text, () => $"glyph emit failed (embedded): {ex.Message}"); }
```

(d) Add the fallback renderer (mirrors the embedded loop; reuses every primitive; CTM baked):
```csharp
    private bool RenderWithSubstitute(string text, List<double> glyphWidths,
        PdfGraphicsState state, PdfFont font)
    {
        EmbeddedFontMetrics? sub = _substitutes.Resolve(font.BaseFont, font.GetDescriptor());
        if (sub is not { IsValid: true }) return false; // no substitute available → nothing to draw

        if (state.RenderingMode is 3 or 7) return true;  // invisible

        bool applyBold = ShouldApplyFauxBold(font);
        var tHs = (float)state.HorizontalScaling / 100f;
        bool flipX = state.FontSize < 0 != state.TextMatrix.M11 < 0;
        double currentX = 0;

        for (var i = 0; i < text.Length; i++)
        {
            string ch = DecomposeLigature(text[i]);
            double w = i < glyphWidths.Count ? glyphWidths[i] : 0;
            double subW = ch.Length > 0 ? w / ch.Length : 0;

            foreach (char c in ch)
            {
                try
                {
                    ushort glyphId = sub.GetGlyphId(c);
                    if (glyphId != 0)
                    {
                        GlyphOutline? outline = sub.GetGlyphOutline(glyphId);
                        if (outline is { IsEmpty: false })
                        {
                            IPathBuilder glyphSpace = glyphPaths.GetGlyphPath(
                                sub, glyphId, (float)state.FontSize, outline, resolvedGlyphName: null);
                            Matrix3x2 toUser = GlyphPlacement.GlyphToUser(state, currentX, tHs) * state.Ctm;
                            EmitGlyph(glyphSpace.Transform(toUser), state, toUser, applyBold);
                        }
                    }
                }
                catch (Exception ex)
                {
                    PdfLogger.Log(LogCategory.Text, () => $"glyph emit failed (substitute): {ex.Message}");
                }
                currentX += subW * (flipX ? -1.0 : 1.0);
            }
        }
        return true;
    }

    private static string DecomposeLigature(char c) => c switch  // port of TextRenderer.cs:954-962
    {
        'ﬀ' => "ff", 'ﬁ' => "fi", 'ﬂ' => "fl", 'ﬃ' => "ffi", 'ﬄ' => "ffl",
        _ => c.ToString()
    };
```

(Note: a system substitute is usually TrueType or OpenType-CFF — both handled by `GlyphPathService`/`GetGlyphOutline`. `GetGlyphId(char)` is the substitute's Unicode cmap. Advance is the PDF width split across decomposed ligature parts. The CTM is baked via `* state.Ctm` — do NOT omit it; that was the `8429607` bug.)

- [ ] **Step 3b: Thread the provider through `PdfRenderer` + `PdfPage`**

`PdfRenderer.cs`: add an optional `ISystemFontProvider? fontProvider = null` ctor parameter and build the core text renderer with it (one shared `SystemFontLocator` default — it builds a `FontDirectoryIndex`, so construct once, never per glyph):
```csharp
_coreText = new CoreTextRenderer(target, new GlyphPathService(), fontProvider ?? new SystemFontLocator());
```
(Leave the two `if (!_coreText.Render(...)) _target.DrawText(...)` fallbacks in place for now — Task 3 removes them.)

`PdfPage.cs`: add the internal seam and make the public method delegate:
```csharp
public void Render(IRenderTarget target) => Render(target, null);
internal void Render(IRenderTarget target, ISystemFontProvider? fontProvider) { /* existing body; pass fontProvider into the PdfRenderer ctor */ }
```
(Find where the existing `Render` constructs `PdfRenderer` and pass `fontProvider`. Confirm `PdfLibrary` already has `InternalsVisibleTo PdfLibrary.Tests` — it does, from earlier plans.)

- [ ] **Step 4: Run the test to verify it passes + embedded tests stay green**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CoreTextRendererTests"`
Expected: PASS — embedded fill, the CTM regression test, AND the new non-embedded substitute test.

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/CoreTextRenderer.cs PdfLibrary/Rendering/PdfRenderer.cs PdfLibrary/Document/PdfPage.cs PdfLibrary.Tests/Rendering/CoreTextRendererTests.cs
git commit -m "feat(render): non-embedded text renders core-side via system substitute outlines (CTM baked)"
```

---

## Task 3: Drop the dead `DrawText` fallback in `PdfRenderer`

**Files:**
- Modify: `PdfLibrary/Rendering/PdfRenderer.cs`

**Background:** `CoreTextRenderer.Render` now handles both embedded and non-embedded fonts, so the two `_target.DrawText(...)` fallbacks are unreachable. Remove them (this is what frees `DrawText`/`MeasureTextWidth` to be deleted from the SPI in Task 4).

- [ ] **Step 1: Replace both call-sites**

`PdfRenderer.cs:884-885` (Tj): replace
```csharp
if (!_coreText.Render(textToRender, glyphWidths, CurrentState, font, charCodes))
    _target.DrawText(textToRender, glyphWidths, CurrentState, font, charCodes);
```
with
```csharp
_coreText.Render(textToRender, glyphWidths, CurrentState, font, charCodes);
```

`PdfRenderer.cs:1177-1178` (TJ): replace the `if (!_coreText.Render(combinedText.ToString(), ...)) _target.DrawText(combinedText.ToString(), ...)` with a single call, hoisting the string (the B3b-T2 minor):
```csharp
var combined = combinedText.ToString();
_coreText.Render(combined, combinedWidths, CurrentState, font, combinedCharCodes);
```

- [ ] **Step 2: Build + run the render + core text tests**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Debug --nologo` (expect success) and `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~CoreTextRendererTests|FullyQualifiedName~PdfLibrary.Tests.Rendering&Category!=LocalOnly" --nologo`
Expected: PASS. (`_target.DrawText` now has zero call-sites in `PdfRenderer`.)

- [ ] **Step 3: Commit**

```bash
git add PdfLibrary/Rendering/PdfRenderer.cs
git commit -m "refactor(render): drop the dead DrawText fallback — CoreTextRenderer handles all fonts"
```

---

## Task 4: Remove `DrawText`/`MeasureTextWidth` from the SPI; delete the SkiaSharp text classes

**Files:**
- Modify: `PdfLibrary/Rendering/IRenderTarget.cs` (remove both members — lines ~109, ~121)
- Modify: `PdfLibrary.Rendering.SkiaSharp/SkiaSharpRenderTarget.cs` (remove `DrawText` ~345, `MeasureTextWidth` ~399, the `_textRenderer` field ~26 + its construction ~70)
- Modify: `PdfLibrary.Rendering.SkiaSharp/Rendering/SkiaSharpRenderTargetForPattern.cs` (remove `DrawText` ~142, `MeasureTextWidth` ~237)
- Delete: `PdfLibrary.Rendering.SkiaSharp/Rendering/TextRenderer.cs`, `PdfLibrary.Rendering.SkiaSharp/Rendering/GlyphToSKPathConverter.cs`
- Modify/Delete tests: `MockRenderTarget.cs` (remove stubs ~51, ~104), `CoreTextRendererTests.cs` (remove `DrawText`/`MeasureTextWidth` stubs from `RecordingRenderTarget` ~116/118), delete `GlyphToSKPathConverterTests.cs`
- Modify (if now unused): `SystemFontResolver.cs`, `SkiaFontProvider.cs`, `FontCategory.cs`

**Background:** With both `DrawText` call-sites gone (Task 3), the interface members and every implementation are dead. `GlyphToSKPathConverter`'s only consumer is `TextRenderer` (map §4.3). Deleting `TextRenderer` may leave `SystemFontResolver`/`SkiaFontProvider`/`FontCategory` unused (they existed only to back the fallback) — remove them if no references remain.

- [ ] **Step 1: Remove the interface members + all implementations + delete the classes**

Delete the two members from `IRenderTarget`. Remove the overrides from `SkiaSharpRenderTarget` (and the `_textRenderer` field + its `new TextRenderer(_canvas)` construction), `SkiaSharpRenderTargetForPattern`, `MockRenderTarget`, and the `RecordingRenderTarget` in `CoreTextRendererTests`. Delete `TextRenderer.cs`, `GlyphToSKPathConverter.cs`, `GlyphToSKPathConverterTests.cs`.

- [ ] **Step 2: Build; chase down references**

Run: `dotnet build PdfLibrary.sln -c Debug --nologo` (or build each project).
Expected: errors point at any remaining `DrawText`/`MeasureTextWidth`/`TextRenderer`/`GlyphToSKPathConverter`/`FontCategory` references. Fix each: remove the call, or delete the now-orphaned class. After it builds, grep to confirm zero references remain:
`grep -rn "DrawText\|MeasureTextWidth\|GlyphToSKPathConverter\|new TextRenderer" --include=*.cs PdfLibrary PdfLibrary.Rendering.SkiaSharp PdfLibrary.Tests | grep -v /obj/`
Expected: no output (other than unrelated `_canvas.DrawText` — there should be none left, since `TextRenderer` is gone).

- [ ] **Step 3: Remove now-dead font-resolver code (if unused)**

`grep -rn "SystemFontResolver\|SkiaFontProvider\|FontCategory" --include=*.cs PdfLibrary.Rendering.SkiaSharp | grep -v /obj/` — if the only hits were inside the deleted `TextRenderer`, delete `SystemFontResolver.cs`, `SkiaFontProvider.cs`, `FontCategory.cs`. If anything else references them, leave them and note it.

- [ ] **Step 4: Full build + suite**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo` and `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`
Expected: build `0 Error(s)`; suite PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(render): remove DrawText/MeasureTextWidth from IRenderTarget; delete SkiaSharp TextRenderer + GlyphToSKPathConverter"
```

---

## Task 5: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Embedded render output unchanged (the B3b invariant)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PdfLibrary.Tests.Rendering&Category!=LocalOnly" --nologo`
Expected: PASS. These are tolerant/structural for non-embedded text but pixel-tight where they assert embedded output. If a test fails, determine whether the failure is (a) a non-embedded page now drawn with a different system substitute (EXPECTED — re-baseline or confirm visually) or (b) embedded text changed (a REGRESSION — stop and fix). Report which.

- [ ] **Step 2: Full suite + Skia-free core + Release (core, renderer, viewer)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`; `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/` (expect none); `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo`; `dotnet build PdfLibrary.Rendering.SkiaSharp/...csproj -c Release --nologo`; `dotnet build PdfLibrary.Wpf.Viewer/...csproj -c Debug --nologo`.
Expected: suite PASS; core Skia-free; all builds `0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual viewer check (human-in-the-loop)**

Open PDFs that use **non-embedded** (Base-14 / unembedded) fonts in the viewer. Their text now renders via core outlines of a located system substitute. Confirm it is legible and correctly positioned (including text inside figures / `cm`-transformed contexts — the CTM-baking path). Note any font that renders blank (no system substitute found) or mispositioned.

- [ ] **Step 4: No commit (verification only).**

---

## Self-Review Notes

- **Spec coverage:** non-embedded fallback moved core-side (Task 1-3) reusing the embedded pipeline with the CTM baked; the text SPI (`DrawText`/`MeasureTextWidth`) and the SkiaSharp text classes deleted (Task 4) → `IRenderTarget` is geometry-only.
- **CTM:** `RenderWithSubstitute` uses `GlyphToUser(...) * state.Ctm` — the fix from `8429607` is not reintroduced.
- **Robustness:** per-glyph try/catch on both paths (one bad glyph skips, not the whole run) — matters now that no `DrawText` fallback exists.
- **Known acceptable change:** non-embedded text rasterizes differently than the old Skia `SKFont` fallback (different substitute + outline path). Tolerant render tests absorb it; the viewer check is the human confirmation.

## Out of scope / still deferred

- **Faux-bold 0.5-device-px floor** — still requires the page render scale, which the core doesn't have at glyph time. The nominal `FontSize*0.04*scale` width is applied; the small-size floor stays deferred until the page scale is threaded core-side.
- **`GlyphPathService` document-scope cache** — still per-`PdfRenderer`. Substitute metrics are cached per-`SubstituteFontResolver` (per renderer) by BaseFont, which is sufficient; cross-document reuse remains a later perf item.
- **Plan C** — slim the SkiaSharp adapter, write a sample non-Skia `IRenderTarget`, document the now-geometry-only SPI.
- **Plan D** — interactive forms (field-widget geometry + `PageGeometry`).
