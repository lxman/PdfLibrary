# PdfLibrary Improvements

Working notes on issues, gaps, and refactor opportunities. Scoped to in-house code — `JpegLibrary/` is vendored upstream and excluded except where it intersects replacement work; `Compressors.Jpeg2000`'s `Melville.CSJ2K` dependency is also tracked here as a replacement target.

Project size (in-house, excluding `JpegLibrary/`): ~96K LOC across 877 source files. Top-level: PdfLibrary, PdfLibrary.Rendering.SkiaSharp, PdfLibrary.Tests, PdfLibrary.Integration, PdfLibrary.Wpf.Viewer, PdfLibrary.Utilities/ImageUtility, PdfLibrary.Examples/*, ImageLibrary (+ codec sub-projects, including `Jbig2Decoder`), Compressors/Compressors.Jpeg2000, FontParser, Logging.

---

## 1. Third-party decoder replacement (primary objective)

Two outstanding third-party decoder dependencies, both replacements deferred until after the next NuGet release:

### 1a. JpegLibrary (vendored source fork)

Currently vendored at `JpegLibrary/src/JpegLibrary/`, used for `/DCTDecode`. Earlier plans referenced a `Compressors/Compressors.Jpeg/` wrapper as the seam to swap — that wrapper has since been deleted. The replacement should follow the **JBIG2 pattern** instead: write the new decoder, integrate directly in `PdfLibrary/Filters/DctDecodeFilter.cs` (no wrapper layer), then delete the vendored tree.

Integration points:

| Site | Role |
|---|---|
| `PdfLibrary/Filters/DctDecodeFilter.cs` | `/DCTDecode` filter |
| `PdfLibrary.Rendering.SkiaSharp/Rendering/ImageRenderer.cs` | Manual JPEG decode fallback path (now logs failures — see §3) |
| `ImageLibrary/ImageLibrary/Container/Tiff/TiffDecoder.cs` | TIFF JPEG sub-format |

Suggested sequence:

1. Inventory every `JpegLibrary` API actually called from PdfLibrary's filter and ImageLibrary's TIFF decoder (small list — wrapper-free integration narrows the surface).
2. Write the new decoder against that contract.
3. Run `PdfLibrary.Tests` filtered to JPEG fixtures (`StreamFilterTests`, `ImageRendererTests`) plus `JpegCodec.Tests` if added.
4. Visual-diff against `mutool draw` output for sample PDFs containing JPEG images (the `wsl bash -c "mutool draw …"` pattern from `CLAUDE.md`).
5. Once parity is confirmed, delete `JpegLibrary/` from the tree and update `PdfLibrary.slnx`.

### 1b. Melville.CSJ2K (NuGet, transitive via Compressors.Jpeg2000)

`Compressors.Jpeg2000` wraps `Melville.CSJ2K` 0.6.4 for `/JPXDecode`. CSJ2K is an IKVM-converted port of a Java JPEG2000 decoder, modernized as Melville's fork — slow, verbose stack traces, awkward to debug. Replacement is one of the longer-tail items because JPEG2000 is genuinely complex (DWT + EBCOT + tile-component-resolution-precinct-codeblock layout).

The page-2-takes-2-seconds rendering issue documented in performance work was traced to JPEG2000 decode; replacing CSJ2K with a pure C# implementation would address that latency directly.

---

## 2. Correctness / spec-compliance gaps

### High impact — embedded font extraction is TrueType-only

`PdfLibrary/Fonts/Embedded/EmbeddedFontExtractor.cs:35-36` — `FontFile` (Type1) and `FontFile3` (CFF/OpenType) are still marked TODO. Only `FontFile2` (TrueType) is parsed.

Consequence: text extraction and glyph rendering fall through to system fonts or Adobe glyph list lookups for any PDF embedding Type1 or CFF — a significant share of legacy and modern documents.

The Type1 parser already exists at `PdfLibrary/Fonts/Type1Font.cs` (1,328 LOC) and is not wired through. CFF parser exists at `FontParser/Tables/Cff/`. Both could be plumbed into `EmbeddedFontExtractor` without writing new parsing code.

### Medium impact — CalRGB / CalGray transforms not applied

`PdfLibrary/Rendering/ColorSpaceResolver.cs:549-550` and `:571-572` — both methods explicitly say `// For now, treat as Device{RGB,Gray}` and `// TODO: Apply gamma and matrix transformations`. Pass-through to device color produces incorrect output for PDFs declaring calibrated color.

### Medium impact — DeviceN color space not parsed

`ColorSpaceResolver` handles 10 of 11 standard color spaces. `DeviceN` (multi-component spot colors used in print workflows) is not handled. Zero matches for `DeviceN` in the resolver source.

### Lower impact — operator gaps

The 59 operator classes cover the common cases. Worth verifying behavior for: shading operators (`sh`), marked-content operators (`BDC`/`EMC`/`BMC`), and clipping operators (`W`/`W*` track pending state but the application path through `PdfRenderer` should be re-checked for correctness in nested-q/Q contexts).

---

## 3. Defensive coding

### Catch-all exception handlers — 3 of 4 sites now logged ✓

The four silent catch-alls flagged in the original sweep have been mostly addressed:

| File | Status |
|---|---|
| `PdfLibrary/Document/PdfPage.cs:364` | ✓ Logs `LogCategory.Images`: "Skipped malformed image XObject: {type}: {message}" |
| `PdfLibrary.Rendering.SkiaSharp/Rendering/ImageRenderer.cs:194` | ✓ Logs `LogCategory.Images`: "Manual JP2/JPEG decode failed, falling back: {type}: {message}" |
| `PdfLibrary/Filters/Jbig2DecodeFilter.cs` | ✓ Narrowed catch to `NullReferenceException`/`IndexOutOfRangeException` only (other exceptions now propagate as `InvalidOperationException`); logs failures with stream length + exception type |
| `FontParser/Tables/Cmap/CmapTable.cs:97` | ⚠ Still bare `catch (Exception)`. Would require `FontParser` to take a project reference on `Logging` (currently doesn't); architectural call rather than mechanical fix. |

### `#nullable enable` — project-enabled, cleanup pending

`PdfLibrary.csproj` has project-level `<Nullable>enable</Nullable>`, so the directive isn't missing. But the build emits ~240 CS8602 / CS8604 nullable warnings that nobody's cleaned up. Recommend a focused pass with the compiler driving the cleanup — same recommendation as before, just narrower than the original "add nullable annotations" framing.

### No async/await in rendering or parsing paths

`FontReader` exposes async methods but every caller in `PdfLibrary` is synchronous. Not urgent — synchronous rendering is fine for most use cases — but blocks UI/server scenarios that want non-blocking parsing.

---

## 4. Structural refactors (god classes)

Listed in priority order. Each fights you on every change to the affected area. **None of these have been split since the doc was first written.**

| File | LOC | Suggested split |
|---|---|---|
| `PdfLibrary/Builder/PdfDocumentWriter.cs` | 2,343 | `XrefWriter`, `ObjectSerializer`, `EncryptionApplier`, `ResourcePool` |
| `PdfLibrary/Rendering/PdfRenderer.cs` | 1,700 | Separate `ResourceResolver` and `OperatorDispatcher` from rendering orchestration |
| `PdfLibrary/Fonts/Type1Font.cs` | 1,328 | Already self-contained; consider extracting `Type1CharStringInterpreter` |
| `PdfLibrary/Fonts/Embedded/EmbeddedFontMetrics.cs` | 1,203 | Static metrics tables — could be data files (JSON/binary) loaded at startup |
| `ImageLibrary/ImageLibrary/Container/Tiff/TiffDecoder.cs` | 1,120 | Per-compression strategy classes (LZW, CCITT, JPEG already separate libraries) |
| `PdfLibrary.Rendering.SkiaSharp/Rendering/TextRenderer.cs` | 1,003 | Glyph extraction vs. layout vs. paint |
| `FontParser/Tables/Cff/CharStringParser.cs` | 1,013 | OK as-is given CFF spec complexity, but `CFF2` extension belongs in a sibling file |

---

## 5. Test coverage

`PdfLibrary.Tests` sits at **702 tests across 34 files** vs. 152 source files in `PdfLibrary/` (~0.46:1 ratio). Coverage gaps with no dedicated tests:

- `Builder/` — entire fluent API and `PdfDocumentWriter`
- `Rendering/` — `PdfRenderer`, `ColorSpaceResolver`, `ExtGStateApplier`
- `Fixups/` — workaround logic
- `Functions/` — exponential/sampled function evaluation
- `Security/` integration — encryption is unit-tested at the cipher level but no end-to-end "encrypt → write → re-read → decrypt" test
- Most `Content/` operator classes

Codec-level test coverage is healthier:

- `Jbig2Decoder.Tests`: **173 tests** (61 unit + 5 real-world PDF stream regressions + 107 golden-image corpus tests against the Nico Weber jbig2-tests-pdf set)
- `Compression.Ccitt.Tests`: 59 tests
- `Compression.Lzw.Tests`: 18 tests
- `BmpCodec.Tests` / `GifCodec.Tests` / `PngCodec.Tests` / `TgaCodec.Tests`: 18-27 each
- `ImageLibrary.IntegrationTests`: 31 tests

Solution-wide: **1,068 tests passing**.

---

## 6. Diagnostic logging cost

`PdfLibrary/Rendering/ColorSpaceResolver.cs` builds full interpolated payloads for every `LogCategory.Graphics` log call (35 sites), regardless of whether the category is enabled. On a non-trivial PDF render this is meaningful CPU.

A `Log(LogCategory, Func<string>)` overload was added to `PdfLogger` so call sites can lazy-build payloads:

```csharp
public static void Log(LogCategory category, Func<string> messageFactory)
{
    if (!IsCategoryEnabled(category)) return;
    Log(category, messageFactory());
}
```

Migration of `ColorSpaceResolver` to this overload was attempted and reverted: most resolver methods (`ResolveColorSpace`, `ResolveICCBased`, `ResolveSeparation`, `ResolveIndexed`, `ResolveLab`, `ResolveCalRGB`, `ResolveCalGray`) take `ref string? colorSpaceName` and `ref List<double>? color` parameters. C# disallows capturing `ref` parameters in lambdas (CS1628), and 21 of 35 sites failed to compile.

Two paths forward:
1. **Refactor the `ref` parameter signatures** to return tuples or value types instead. Substantive change — the resolver API surface is non-trivial.
2. **Wrap each call in an inline `IsCategoryEnabled` guard** — 35 mechanical edits, awkward verbosity, but works around the `ref` issue:
   ```csharp
   if (PdfLogger.IsCategoryEnabled(LogCategory.Graphics))
       PdfLogger.Log(LogCategory.Graphics, $"...");
   ```

The new `Func<string>` overload remains useful for future logging on non-`ref` methods.

---

## 7. TODO inventory (in-house code, ~36 items)

Concentrated in three areas:

- **FontParser** — proprietary tables (`Pfed`, `Webf`, `Prop`), AAT/Graphite, CFF2, bitmap fonts (`Cbdt`)
- **PdfLibrary** — the issues already covered in §2 plus minor renderer notes
- **ImageLibrary / Compressors / Utilities** — image codec example sites and TIFF compression-strategy stubs

Worth doing a sweep with `Grep -nE "TODO|FIXME|HACK"` and converting durable TODOs into GitHub issues so they don't decay into noise.

---

## Suggested ordering

1. **NuGet release** — checkpoint current state before further work (the user-stated immediate next step)
2. **JpegLibrary replacement** — long-running primary objective; the deleted-wrapper precedent (JBIG2) gives a known-good integration pattern
3. **Melville.CSJ2K replacement** — addresses the JPEG2000 render latency issue and removes the last third-party codec NuGet
4. **`PdfLibrary` nullable warning cleanup** — ~240 warnings, mechanical, durable correctness benefit
5. **Embedded Type1 + CFF extraction** — biggest correctness gap that affects text on real PDFs (parsers already exist)
6. **CalRGB/CalGray transforms** — correctness, smaller scope than fonts
7. **`PdfDocumentWriter` split** — unlocks future builder work
8. **`PdfRenderer` split** — unlocks future rendering work
9. **Test suite expansion** — ongoing, can be folded into other items
