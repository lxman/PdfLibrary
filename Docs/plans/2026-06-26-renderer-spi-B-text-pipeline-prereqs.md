# Renderer SPI — Plan B: Text-Pipeline Prerequisites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the SkiaSharp-free prerequisites the thin-target text pipeline needs — a readable path interface, `.ttc` font-collection support, and complete standard-14 substitute coverage — without touching the live render path.

**Architecture:** Three independent, additive changes plus a verification gate: (1) lift `Segments` onto `IPathBuilder` so a render target can read path geometry; (2) restore classic `'ttcf'` TrueType-Collection support in `FontParser.SfntFont` (ported from the upstream `FontManager.NET/FontParser.FontReader.ParseTtc`) so a located macOS `.ttc` parses; (3) finish the `Standard14Fonts` candidate map (macOS literal faces + the already-implemented-but-untested aliases). The glyph→`IPathBuilder` converter and the live pipeline rewire are **Plan B2** (deliberately out of scope — they require deeper exploration of `EmbeddedFontMetrics`/`GlyphMetrics`/CFF command types and the existing `TextRenderer`).

**Tech Stack:** C# 12, .NET 8/9/10, xUnit. FontParser (internal project). No new package dependencies.

## Global Constraints

- Core `PdfLibrary` must remain SkiaSharp-free (no `using SkiaSharp`, no package ref).
- Multi-target `net8.0;net9.0;net10.0`.
- Adding `Segments` to the public `IPathBuilder` is a breaking interface change — acceptable because this is the 2.0 line (`skia-v4`).
- New tests live in `PdfLibrary.Tests/Rendering/` and `PdfLibrary.Tests/Fonts/` (namespaces `PdfLibrary.Tests.Rendering` / `PdfLibrary.Tests.Fonts`).
- xUnit conventions; xUnit available via global usings (don't add `using Xunit;` unless the build complains).
- The full suite (currently 1244 on the CI filter `Category!=LocalOnly`) must stay green after every task.

---

## File Structure

- `PdfLibrary/Rendering/IPathBuilder.cs` (modify) — add `Segments`.
- `FontParser/SfntFont.cs` (modify) — classic `'ttcf'` collection support (use font 0).
- `PdfLibrary/Fonts/Standard14Fonts.cs` (modify) — insert macOS literal faces before the DejaVu coverage fallback.
- `PdfLibrary.Tests/Rendering/PathBuilderSegmentsTests.cs` (create)
- `PdfLibrary.Tests/Fonts/SfntFontTtcTests.cs` (create)
- `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs` (modify — add alias assertions)

---

## Task 1: Lift `Segments` onto `IPathBuilder`

**Files:**
- Modify: `PdfLibrary/Rendering/IPathBuilder.cs`
- Test: `PdfLibrary.Tests/Rendering/PathBuilderSegmentsTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<PathSegment> IPathBuilder.Segments { get; }` — the ordered path segments, readable through the interface (the concrete `PathBuilder` already implements this property, so no implementation change is needed).

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Rendering/PathBuilderSegmentsTests.cs`:

```csharp
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class PathBuilderSegmentsTests
{
    [Fact]
    public void Segments_AreReadable_ThroughTheInterface()
    {
        IPathBuilder path = new PathBuilder();
        path.MoveTo(1, 2);
        path.LineTo(3, 4);
        path.CurveTo(5, 6, 7, 8, 9, 10);
        path.ClosePath();

        IReadOnlyList<PathSegment> segments = path.Segments;

        Assert.Equal(4, segments.Count);
        Assert.IsType<MoveToSegment>(segments[0]);
        Assert.IsType<LineToSegment>(segments[1]);
        Assert.IsType<CurveToSegment>(segments[2]);
        Assert.IsType<ClosePathSegment>(segments[3]);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PathBuilderSegmentsTests"`
Expected: FAIL — compile error: `IPathBuilder` has no `Segments`.

- [ ] **Step 3: Add `Segments` to the interface**

In `PdfLibrary/Rendering/IPathBuilder.cs`, add inside the interface body (after `Clone()`):

```csharp
    /// <summary>
    /// The ordered path segments (move/line/cubic-curve/close), for a render target to read
    /// and convert to its native path representation.
    /// </summary>
    IReadOnlyList<PathSegment> Segments { get; }
```

(The concrete `PathBuilder.Segments` already returns `IReadOnlyList<PathSegment>`, so it satisfies this member with no change.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~PathBuilderSegmentsTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Rendering/IPathBuilder.cs PdfLibrary.Tests/Rendering/PathBuilderSegmentsTests.cs
git commit -m "feat(render): expose IPathBuilder.Segments so targets can read path geometry"
```

---

## Task 2: Classic `.ttc` (TrueType Collection) support in `SfntFont`

**Files:**
- Modify: `FontParser/SfntFont.cs`
- Test: `PdfLibrary.Tests/Fonts/SfntFontTtcTests.cs`

**Background:** This repo's `SfntFont` was scoped to single-font sfnt programs and throws on `'ttcf'`. A system font located for a non-embedded face can be a collection (notably macOS `Helvetica.ttc`/`Times.ttc`/`Courier.ttc`). The classic `'ttcf'` format is: tag `'ttcf'` (already consumed as `sfntVersion`), `majorVersion` (uint16), `minorVersion` (uint16), `numFonts` (uint32), then `numFonts` × uint32 offsets to each font's table directory. We use **font 0**. The directory entries hold absolute offsets into the whole buffer, so after seeking to font 0's directory the existing `GetTableBytes` resolves correctly. (Logic ported from upstream `FontManager.NET/FontParser/FontReader.cs:ParseTtc`.)

**Interfaces:**
- Produces: `new SfntFont(ttcBytes)` parses a `'ttcf'` collection by reading font 0's table directory; existing accessors (`NumGlyphs`, `UnitsPerEm`, table getters) work unchanged.

- [ ] **Step 1: Write the failing test**

Create `PdfLibrary.Tests/Fonts/SfntFontTtcTests.cs`. It synthesizes a 1-font `'ttcf'` wrapper around the vendored single-font fixture (a 16-byte TTC header, with font 0 at offset 16) and asserts the collection parses identically to the bare font:

```csharp
using FontParser;

namespace PdfLibrary.Tests.Fonts;

public class SfntFontTtcTests
{
    private static byte[] BareFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    // Builds a VALID 1-font classic TTC: a 16-byte TTC header, then the font — with each table
    // offset shifted by the header size, because real .ttc table offsets are absolute from the
    // FILE start (so the parser must NOT add the font's base offset; it seeks them directly).
    private static byte[] WrapAsTtc(byte[] font)
    {
        const int headerSize = 16;
        byte[] header =
        {
            0x74, 0x74, 0x63, 0x66, // 'ttcf'
            0x00, 0x01,             // majorVersion
            0x00, 0x00,             // minorVersion
            0x00, 0x00, 0x00, 0x01, // numFonts = 1
            0x00, 0x00, 0x00, 0x10  // offset[0] = 16
        };
        var ttc = new byte[headerSize + font.Length];
        Array.Copy(header, 0, ttc, 0, headerSize);
        Array.Copy(font, 0, ttc, headerSize, font.Length);

        // sfnt directory at ttc[headerSize]: sfntVersion(4) numTables(2) searchRange(2)
        // entrySelector(2) rangeShift(2) = 12, then 16-byte records (tag4, checksum4, offset4, length4).
        int numTables = (ttc[headerSize + 4] << 8) | ttc[headerSize + 5];
        int firstRecord = headerSize + 12;
        for (int i = 0; i < numTables; i++)
        {
            int offPos = firstRecord + i * 16 + 8; // skip tag(4) + checksum(4)
            uint off = ((uint)ttc[offPos] << 24) | ((uint)ttc[offPos + 1] << 16)
                     | ((uint)ttc[offPos + 2] << 8) | ttc[offPos + 3];
            off += headerSize;
            ttc[offPos]     = (byte)(off >> 24);
            ttc[offPos + 1] = (byte)(off >> 16);
            ttc[offPos + 2] = (byte)(off >> 8);
            ttc[offPos + 3] = (byte)off;
        }
        return ttc;
    }

    [Fact]
    public void Ttc_ParsesFirstFont_LikeBareFont()
    {
        byte[] bare = BareFont();
        var single = new SfntFont(bare);
        var collection = new SfntFont(WrapAsTtc(bare));

        Assert.Equal(single.NumGlyphs, collection.NumGlyphs);
        Assert.Equal(single.UnitsPerEm, collection.UnitsPerEm);
        Assert.Equal(single.OutlineKind, collection.OutlineKind);
        Assert.True(collection.HasTable("glyf") || collection.HasTable("CFF "));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntFontTtcTests"`
Expected: FAIL — `SfntFont` throws `InvalidDataException` ("TrueType collections (ttcf) ... are not supported") on the `'ttcf'` magic.

- [ ] **Step 3: Add the `'ttcf'` branch**

In `FontParser/SfntFont.cs`, inside the constructor, immediately after `uint sfntVersion = reader.ReadUInt32();` and BEFORE the existing version-validation `if`, insert:

```csharp
            // A TrueType/OpenType Collection ('ttcf'): parse font 0's table directory. PDFs never
            // embed collections, but a system font located for a NON-embedded face may be a .ttc
            // (notably macOS Helvetica/Times/Courier). Ported from FontManager.NET FontReader.ParseTtc.
            if (sfntVersion == 0x74746366) // 'ttcf'
            {
                reader.ReadUShort();            // majorVersion
                reader.ReadUShort();            // minorVersion
                uint numFonts = reader.ReadUInt32();
                if (numFonts == 0)
                    throw new InvalidDataException("TrueType collection (ttcf) declares zero fonts.");
                uint firstFontOffset = reader.ReadUInt32(); // offset to font 0's table directory
                reader.Seek(firstFontOffset);
                sfntVersion = reader.ReadUInt32();          // the real sfnt version of font 0
            }
```

Then update the doc comment on the class: change the "scope is ... single-font case only" sentence (lines ~33-36) to note that a `'ttcf'` collection is handled by using its first font. Suggested replacement for that sentence:

```csharp
    /// Scope is deliberately PDF-shaped: a PDF embeds exactly one font program. A TrueType
    /// Collection ('ttcf') — which can appear when locating a system font for a non-embedded
    /// face — is handled by parsing its first font; WOFF/WOFF2 containers are not supported.
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~SfntFontTtcTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add FontParser/SfntFont.cs PdfLibrary.Tests/Fonts/SfntFontTtcTests.cs
git commit -m "feat(fontparser): parse classic ttcf collections (use font 0) for located system fonts"
```

---

## Task 3: Complete `Standard14Fonts` — macOS literal faces + alias tests

**Files:**
- Modify: `PdfLibrary/Fonts/Standard14Fonts.cs`
- Test: `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs` (add assertions)

**Background:** The Plan A final review found the candidate lists never include the literal macOS faces `Helvetica`/`Times`/`Courier`, so a libre-less macOS box can't resolve the three text families (Symbol/ZapfDingbats already lead with their macOS literals). The literal faces are exact metric matches, so they belong **before** the `DejaVu*` coverage fallback (DejaVu is glyph-coverage, not metric-compatible). Also, the `TimesNewRoman`/`CourierNew` aliases are implemented but only `Arial` is tested.

**Interfaces:**
- Produces: `Standard14Fonts.SubstituteFileBaseNames` now yields the macOS literal face (`Helvetica`/`Times`/`Courier`) as the candidate immediately before the `DejaVu*` entry, for every style of the three text families.

- [ ] **Step 1: Write the failing tests**

Add to `PdfLibrary.Tests/Fonts/Standard14FontsTests.cs`:

```csharp
    [Fact]
    public void TextFamilies_IncludeMacOsLiteral_BeforeDejaVuFallback()
    {
        // ToList() because SubstituteFileBaseNames returns IReadOnlyList<string>, which has no IndexOf.
        var helv = Standard14Fonts.SubstituteFileBaseNames("Helvetica").ToList();
        var times = Standard14Fonts.SubstituteFileBaseNames("Times-Bold").ToList();
        var cour = Standard14Fonts.SubstituteFileBaseNames("Courier-Oblique").ToList();

        Assert.True(helv.IndexOf("Helvetica") >= 0 && helv.IndexOf("Helvetica") < helv.IndexOf("DejaVuSans"));
        Assert.True(times.IndexOf("Times") >= 0 && times.IndexOf("Times") < times.IndexOf("DejaVuSerif-Bold"));
        Assert.True(cour.IndexOf("Courier") >= 0 && cour.IndexOf("Courier") < cour.IndexOf("DejaVuSansMono-Oblique"));
    }

    [Fact]
    public void TimesNewRoman_And_CourierNew_Aliases_Resolve()
    {
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Times-Roman"),
            Standard14Fonts.SubstituteFileBaseNames("TimesNewRoman"));
        Assert.Equal(
            Standard14Fonts.SubstituteFileBaseNames("Courier"),
            Standard14Fonts.SubstituteFileBaseNames("CourierNew"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Standard14FontsTests"`
Expected: `TextFamilies_IncludeMacOsLiteral_BeforeDejaVuFallback` FAILS (literal not present → `IndexOf` returns -1). `TimesNewRoman_And_CourierNew_Aliases_Resolve` should already PASS (aliases were implemented in Plan A) — that is fine; it locks the behavior.

- [ ] **Step 3: Insert the literal faces**

In `PdfLibrary/Fonts/Standard14Fonts.cs`, in the `Family.Sans`, `Family.Serif`, and `Family.Mono` branches, insert the literal face immediately before the trailing `DejaVu*` entry in EACH of the four style arrays. The literal is `"Helvetica"` for Sans, `"Times"` for Serif, `"Courier"` for Mono. For example, the Sans branch becomes:

```csharp
            Family.Sans => Pick(bold, italic,
                ["arial", "LiberationSans-Regular", "NimbusSans-Regular", "Arimo-Regular", "Helvetica", "DejaVuSans"],
                ["arialbd", "LiberationSans-Bold", "NimbusSans-Bold", "Arimo-Bold", "Helvetica", "DejaVuSans-Bold"],
                ["ariali", "LiberationSans-Italic", "NimbusSans-Italic", "Arimo-Italic", "Helvetica", "DejaVuSans-Oblique"],
                ["arialbi", "LiberationSans-BoldItalic", "NimbusSans-BoldItalic", "Arimo-BoldItalic", "Helvetica", "DejaVuSans-BoldOblique"]),
```

Apply the same pattern to `Family.Serif` (insert `"Times"` before each `DejaVuSerif*`) and `Family.Mono` (insert `"Courier"` before each `DejaVuSansMono*`). Leave `Family.Symbol` and `Family.Dingbats` unchanged (they already lead with their macOS literals). (Note: macOS ships these as `.ttc` collections, which Task 2 now parses.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "FullyQualifiedName~Standard14FontsTests"`
Expected: PASS (all, including the prior Plan A cases)

- [ ] **Step 5: Commit**

```bash
git add PdfLibrary/Fonts/Standard14Fonts.cs PdfLibrary.Tests/Fonts/Standard14FontsTests.cs
git commit -m "feat(fonts): add macOS literal faces (Helvetica/Times/Courier) before the DejaVu fallback"
```

---

## Task 4: Verification gate

**Files:** none (verification only)

- [ ] **Step 1: Full suite (CI filter)**

Run: `dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj --filter "Category!=LocalOnly" --nologo`
Expected: PASS, 0 failures (≥ 1244 + the new tests).

- [ ] **Step 2: Core still SkiaSharp-free**

Run: `grep -rln "using SkiaSharp\|SkiaSharp\." PdfLibrary --include=*.cs | grep -v /obj/`
Expected: no output.

- [ ] **Step 3: Release builds warning-free (core + FontParser)**

Run: `dotnet build PdfLibrary/PdfLibrary.csproj -c Release --nologo`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 4: No commit (verification only).**

---

## Self-Review Notes

- **Spec coverage:** advances the design's font path — `IPathBuilder.Segments` is the "lift `Segments` onto the interface" prep item; the `.ttc` support + macOS literals close two of Plan A's explicitly-deferred forward gaps. `PdfGraphicsState` leak-audit prep, `GlyphOutlineToPath`, and the live pipeline rewire (remove `DrawText`/`MeasureTextWidth`, flip text to `FillPath`) are **Plan B2**, not here.
- **No live-path change:** none of these touch the render pipeline or the SkiaSharp adapter, so existing rendering behavior and the suite are unaffected until B2 consumes them.
- **TTC scope:** only the classic `'ttcf'` collection (font 0) is supported, matching what macOS ships; WOFF/WOFF2 remain unsupported (PDFs and OS font dirs don't need them here). The DejaVu metric-fidelity caveat (DejaVu is coverage-only) is a B2 concern — B2's width-fixup must treat non-metric substitutes accordingly.

## Out of scope → Plan B2

- Core `GlyphOutlineToPath` (TrueType quadratic→cubic elevation; CFF cubic) producing `IPathBuilder` — needs `EmbeddedFontMetrics`/`GlyphMetrics` construction + the CFF `PathCommand` types pinned down first.
- Moving glyph resolution (charCode→glyphId→outline) from the SkiaSharp `TextRenderer` into the core.
- Flipping text to `FillPath`/`StrokePath`; removing `DrawText`/`MeasureTextWidth` from `IRenderTarget`.
- DejaVu/non-metric width fixup so `/AP` form widths match render advances.
- **Within-`.ttc` face selection (raised by Plan B's final review).** `SfntFont` uses font 0, which is **Regular**. macOS ships Helvetica/Times/Courier as multi-face `.ttc` collections, so a Bold/Italic request fed `.ttc` bytes would render Regular. B2 must add a face selector (e.g. `SfntFont(data, faceIndex)` or pick-the-face-matching-bold/italic by name/OS2 style). `Standard14Fonts` already seeds the bare literal (`"Helvetica"`/`"Times"`/`"Courier"`) in **all four** style slots, so face selection must be driven by the bold/italic flags at resolve time — this is already committed; land it WITH B2.
- **`PdfGraphicsState` public-view leak audit** (design Migration step 1 prep — still outstanding; the pipeline exposes `PdfGraphicsState` to the target as a documented SPI type).
- **Thread `evenOdd` through glyph fills** — `Segments` carries no fill rule by design; B2 passes `evenOdd: true` per the Tr table at every glyph `FillPath` (preserves the Y-flip winding).
- **Real-`.ttc` integration test** once the locator feeds real font bytes (parse an actual multi-face `/System/Library/Fonts/Helvetica.ttc`, not just the synthetic single-font wrapper); note macOS `.dfont` (resource-fork) is neither bare-sfnt nor `ttcf` and won't parse (older-system edge case).
