# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

API surface cleanup (additive, non-breaking) — targets 1.1.0.

### Added
- **Public exception hierarchy.** `PdfParseException` and `PdfSecurityException` are now `public` and derive from a new `public abstract PdfLibrary.PdfException` base. Consumers can `catch (PdfException)` to handle any PDF-specific failure, or catch the specific subtype to distinguish a malformed document from a decryption/password failure. Previously both were `internal`, so callers had to catch bare `Exception`.
- **`PdfDocumentEditor.Open(Stream, password?, leaveOpen?)`** — enter edit mode directly over an in-memory or network stream, matching `PdfDocument.Load(Stream)`. Previously only a file-path overload existed, forcing stream callers through `PdfDocument.Load(stream).Edit()`.
- **`PdfOptimizer.Optimize(document, string outputPath, options?)`** — optimize straight to a file path, matching `PdfDocument.Save(string)`. Previously only a `Stream` overload existed.
- **Collection-facade read/remove parity.** `PdfOutlineCollection.RemoveAt(int)` removes a top-level outline item by index (previously required `outlines[i].Remove()`); `PdfFormFields` now implements `IReadOnlyCollection<PdfFormField>`, exposing a `Count` property (previously `.Count` was only the LINQ extension method); `PdfNamedDestinations` gains a `this[string]` indexer (sugar for `Get`, parallel to `PdfFormFields[name]`).
- **`PdfOptimizer.Optimize` now returns a `PdfOptimizationResult`** (both the `Stream` and file-path overloads), reporting objects before/after/removed, output byte count, and per-pass counts (streams compressed, images recompressed, fonts subsetted). The return value is additive — existing call sites that ignore it still compile.

### Fixed
- **`PdfViewerSettings` boolean preferences can now be cleared.** Setting `HideToolbar` / `FitWindow` / `CenterWindow` / `DisplayDocTitle` to `null` now removes the preference (matching the `PageMode` / `PageLayout` setters). Previously a `null` assignment was silently ignored, so a preference could never be unset once written.

## [1.0.2] - 2026-06-25

Correctness patch. No public API changes.

### Fixed
- **Rendered pages now have the documented white background.** The fluent render path (`page.RenderTo().ToImage()/ToBytes()/ToStream()/ToFile()` and the `doc.SavePageAs(...)` shortcut) returned the raw transparent render instead of compositing onto white, so PNGs came out transparent and JPEGs black — even though `RenderTo()` documents a white default and `WithTransparentBackground()` is the opt-in for transparency. `SkiaSharpRenderTarget.GetImage()` now composites onto opaque white unless transparent output was explicitly requested, matching what the `SaveToFile` path already did. Soft-mask rendering is unaffected (it renders to a transparent target by design).
- **`PdfPageBuilder.AddText(text, x, y, fontName, fontSize)` now honors the page's unit and origin.** The five-argument (font-bearing) overload deposited raw coordinates, ignoring `WithInches()` / `WithUnit(...)` / `FromTopLeft()`, so text placed through it landed at the wrong position (e.g. at 1 point instead of 72 on an inches page). It now applies the same conversion as the `AddText(text, x, y)` overload.

## [1.0.1] - 2026-06-24

Packaging and discoverability patch. No API or runtime behavior changes.

### Added
- **Package icon** on both `Lxman.PdfLibrary` and `Lxman.PdfLibrary.Rendering.SkiaSharp`.
- **SourceLink + symbol packages** — both packages now publish `.snupkg` symbols with embedded SourceLink metadata, so consumers can step into library source while debugging. The publish workflow pushes the symbol packages alongside the main packages.

### Changed
- **NuGet metadata** — broadened `PackageTags` (now includes `pdf-editor`, `csharp`, `cross-platform`, `pure-csharp`, `managed`, `pdf2.0`, `pdf-to-image`) and clarified the descriptions to lead with the "pure C#, no native/third-party codec dependencies, cross-platform" differentiator. `Authors` set to `Michael Jordan`.

### Fixed
- **README** clone URL corrected (`github.com/lxman/PDF` → `github.com/lxman/PdfLibrary`).
- **Build warnings** — resolved `CS0108` (`Type0Font.Encoding` renamed to `EncodingName` to stop hiding the base `PdfFont.Encoding`) and `CS8600` (nullable `out` in `GlyphUsageCollector`). Library now builds warning-free.

## [1.0.0] - 2026-06-21

First stable release. Completes the *load → edit → optimize* story: a loaded PDF can now be modified in place and shrunk, in addition to being parsed, rendered, and created from scratch.

### Added
- **Editing / mutation API** — `doc.Edit()` returns a `PdfDocumentEditor` over a loaded document (see `Docs/EditingApi.md`):
  - **Page operations** — rotate, move, delete, insert blank, import/duplicate pages, append, plus document `Merge` and `Extract`. Deleting a page strips outline entries, named destinations, and link annotations that resolved to it.
  - **Stamping & overlays** — `edit.Pages.Stamp/StampRange/StampAll` with a fluent `PdfStampBuilder`: watermarks, image stamps, placement presets (center/corners/diagonal/tiled/explicit), scale, rotation, opacity, overlay/underlay.
  - **Annotations** — `AddNote`, `AddLink`, `AddExternalLink`, `AddHighlight`.
  - **Document metadata** — `edit.Metadata` writes the Info dictionary and a synced XMP packet (`/Catalog /Metadata`).
  - **Navigation** — `edit.Outlines` (mutable bookmark tree), `edit.PageLabels`, `edit.NamedDestinations`, `edit.ViewerSettings` (page mode/layout, open action, viewer preference flags).
  - **Form filling** — `edit.Forms` exposes typed `PdfTextField` / `PdfButtonField` / `PdfChoiceField` / `PdfSignatureField`. Setting a value regenerates the field's appearance stream (single-line, multiline, comb, combo, list; checkbox/radio appearances are generated when absent), marks the widget printable, and `Flatten()` bakes fields into static page content.
  - **Save** — full-rewrite save with reachability garbage collection; optional object-stream + xref-stream packing (`UseObjectStreams`).
- **Optimization API** — `PdfOptimizer.Optimize`: Flate stream compression, unreachable-object GC, object-stream/xref-stream packing, opt-in lossy image recompression, and opt-in font subsetting (TrueType, CFF Type1C, and CIDFontType0C).
- **Multi-targeting** — the shippable packages (`Lxman.PdfLibrary`, `Lxman.PdfLibrary.Rendering.SkiaSharp`) now target **.NET 8, 9, and 10** (previously .NET 10 only).
- **Unicode text** — all text-valued API (metadata, outline titles, field values, annotation contents) round-trips arbitrary Unicode: single-byte PDFDocEncoding when representable, UTF-16BE otherwise.

### Changed
- **All NuGet dependencies pinned to stable releases** — no preview/dev builds: SkiaSharp `3.119.4`, Microsoft.Extensions.Caching.Memory `10.0.9`, Serilog `4.3.1`, Serilog.Sinks.File `7.0.0`. (SkiaSharp 4.x is deferred: it has no stable release and obsoletes the mutable `SKPath` API the renderer is built on.)

### Fixed
- **Culture invariance (library-wide).** The creation writer emitted decimals with the thread culture and the parser read numbers without `InvariantCulture`, so creating and parsing PDFs broke (or silently corrupted) under non-`.`-decimal cultures (de-DE, fr-FR, etc.). Both sites are now invariant.
- **String round-trip corruption.** `PdfString` literal escapes were written in decimal but read back as octal, corrupting every byte ≥ 64 (UTF-16 titles, URLs, indexed palettes) on a save/optimize round-trip.
- **Form-XObject text extraction.** The extractor dropped the `PdfDocument` when descending into Form XObjects, losing/garbling text whose body lives in a form (and the same bug in soft-mask rendering).
- **Annotation-appearance rendering.** Text inside an annotation appearance rendered at the page origin (CTM not pushed to the target); per-annotation CTM accumulated so only the first widget landed; Form XObjects didn't inherit ExtGState transparency (watermarks painted at full opacity); widget appearances were skipped entirely.
- **Form-fill output.** Filled values now set the `/F` Print flag so they print (not just display); multiline honors explicit newlines; checkbox/radio marks are drawn as vector paths so they render in any viewer.
- **Parser robustness.** Fixed a content-stream parser infinite loop on a zero-width token, added an image-recompression memory guard, and made `Optimize` accept encrypted input (decrypts to an unencrypted equivalent).

## [1.0.0-rc.4] - 2026-06-10

### Added
- **Axial/radial shading rendering.** The `sh` operator and PatternType 2 shading patterns now paint as gradients (ShadingType 2 axial / 3 radial). Previously `sh` was an unimplemented no-op, so gradients were silently dropped and only the flat base fill showed.
- **Complete PDF function support.** Implemented Type 3 (stitching) and Type 4 (PostScript calculator) functions. PdfLibrary now evaluates every PDF function type (0, 2, 3, 4), so shadings and Separation/DeviceN tint transforms that rely on them render correctly.

### Fixed
- **Page `/Rotate` normalization.** `/Rotate` is normalized into `[0, 360)`, so negative or out-of-range angles (e.g. `-90` from Distiller, equivalent to 270) render correctly instead of producing a blank page.
- **WinAnsiEncoding `/Widths`** calculation in `PdfDocumentWriter`.
- **Character spacing** on long lines.

### Changed
- **FontParser consolidated into a single embedded-font layer.** Pruned FontParser to the PDF-relevant core and routed embedded-font parsing through a new `SfntFont` entry point. Corrected CFF charstring subroutine execution (no state reset on recursion — previously corrupted subroutinized glyphs), the flex-family operators (`hflex`/`flex`/`hflex1`/`flex1`), `seac` accented composites, and cmap format 2/4 parsing; hardened `BigEndianReader` bounds checks.

## [1.0.0-rc.1] - 2026-06-06

### Added
- **Performance and Memory Optimizations**:
  - Integrated `ArrayPool<byte>` in `ImageRenderer` (SkiaSharp) to reduce GC pressure during image decoding.
  - Optimized `FlateDecodeFilter` and PNG/TIFF predictor functions with pre-allocated buffer capacities.
  - Improved `TextRenderer` with granular glyph path caching and more efficient matrix transformations.
  - Enhanced path rendering accuracy in `TextRenderer` and `PathRenderer` by correctly applying transformations during drawing.
  - Removed document-level cache clearing in `SkiaSharpRenderTarget` in favor of more granular caching, improving the safety of concurrent rendering.
- **In-house JPEG codec** (`ImageLibrary/JpegCodec`) — pure C# baseline + progressive JPEG encoder and decoder; replaces the vendored `JpegLibrary` git submodule. Used by `PdfLibrary.Filters.DctDecodeFilter` and `ImageLibrary.TiffCodec` for TIFF's JPEG sub-format.
- **In-house Netpbm codec** (`ImageLibrary/PbmCodec`) — pure C# decoder for `P1`–`P6` (ASCII + binary bitmap/graymap/pixmap) and binary encoder (`P4`/`P5`/`P6`). Wired into ImageUtility as `CustomPbmCodec` for `.pbm` / `.pgm` / `.ppm` / `.pnm`.
- **`CustomJpegCodec`, `CustomPngCodec`, `CustomBmpCodec`, `CustomGifCodec`, `CustomTgaCodec`, `CustomTiffCodec`** wrappers in `PdfLibrary.Utilities/ImageUtility/Codecs/`, making the in-tree `ImageLibrary` codecs the primary implementations in ImageUtility's `CodecRegistry`.
- **Thread safety — concurrent rendering (one document per thread)**: the library is now safe for multi-threaded, document-per-request rendering (e.g. ASP.NET Core), validated by a stress harness that hash-compares concurrent output against a single-threaded baseline (zero divergence) with managed memory bounded across thousands of renders. No process-wide render lock is required; the previously documented global `SemaphoreSlim(1,1)` render lock is obsolete. Constraint: a `PdfDocument` and a `SkiaSharpRenderTarget` must each be used by a single thread (load one document per request; one render target per render). Specific fixes:
  - Made the CFF charstring subroutine stack (`FontParser` `SubroutineNester`) per-parse instance state instead of a shared `static` — it was on the live CFF glyph render path and could silently corrupt glyph outlines under concurrent rendering.
  - Fixed `SystemFontResolver.GetTypeface` to resolve typefaces under the cache lock, preventing duplicate (leaked) native `SKTypeface` handles on concurrent first-use.
  - Added `volatile` to the double-checked-locking fields in `BuiltInProfiles` (sRGB), `LabToSrgb`, `AdobeGlyphList`, and `PdfLogger` (`PdfLogger` also snapshots its logger reference before use).
  - Synchronized `CodecRegistry` (lock + snapshot-on-read) and made `FontParser.Table.Types` and `FontParser.Models.Language.Ids` immutable.
  - Made `PanoseInterpreter._values` instance state (was an instance-shadowing `static`) and `SkiaSharpRenderTarget.EnablePerfTrace` a `volatile` flag.
  - Removed the process-wide glyph-cache clearing (`_lastDocument`) in `SkiaSharpRenderTarget` that caused a cache stampede when different documents rendered concurrently; the glyph cache is now keyed per font instance with a bounded size and sliding expiration.
- **ImageLibrary reorganized into per-codec projects.** The monolithic `ImageLibrary/ImageLibrary` project has been split. Each codec now lives in its own project under `ImageLibrary/`:
  - `ImageLibrary/CcittCodec` (was `ImageLibrary/ImageLibrary/Compression/Ccitt`)
  - `ImageLibrary/LzwCodec` (was `ImageLibrary/ImageLibrary/Compression/Lzw`)
  - `ImageLibrary/JpegCodec` (new, replaces `JpegLibrary` submodule)
  - `ImageLibrary/BmpCodec` (was `ImageLibrary/ImageLibrary/Container/Bmp`)
  - `ImageLibrary/GifCodec` (was `ImageLibrary/ImageLibrary/Container/Gif`)
  - `ImageLibrary/PngCodec` (was `ImageLibrary/ImageLibrary/Container/Png`)
  - `ImageLibrary/TgaCodec` (was `ImageLibrary/ImageLibrary/Container/Tga`)
  - `ImageLibrary/TiffCodec` (was `ImageLibrary/ImageLibrary/Container/Tiff`)
  - `ImageLibrary/PbmCodec` (new)
  - `ImageLibrary/Jbig2Decoder` (unchanged)
- Test projects renamed accordingly: `Compression.Ccitt.Tests` → `CcittCodec.Tests`, `Compression.Lzw.Tests` → `LzwCodec.Tests`.
- The legacy `Container/Jp2` decoder has been removed from `ImageLibrary` and replaced by the in-house `ImageLibrary/Jp2Codec` (pure C# JPEG 2000 decoder). `PdfLibrary.Filters.JpxDecodeFilter` now calls `Jp2Codec` directly. The earlier `Compressors/Compressors.Jpeg2000` wrapper (over `Melville.CSJ2K`) has been deleted; `Melville.CSJ2K` is retained only as a test-time differential reference in `ImageLibrary/Jp2Codec.Tests`.
- **Solution-wide warning cleanup**: build now produces 0 warnings, 0 errors. Real fixes for null-safety (`CS8602`/`CS8603`/`CS8604`/`CS8600`/`CS8625`), unreachable code (`CS0162`), obsolete SkiaSharp API usage (`CS0618` — `SKPaint.TextSize`/`Typeface`/`FilterQuality` migrated to `SKFont` + `SKSamplingOptions`), malformed XML doc comments, unused variables/fields, and several xUnit lint suggestions (`xUnit2013`/`xUnit2017`/`xUnit2002`/`xUnit1026`). Policy suppressions (documented in csproj) for `CS1591` in the two NuGet-published libraries and `NU1701` in `PdfLibrary.Wpf.Viewer`.
- **FontParser**: renamed the AAT-bloc `ImageFormat` enum to `BlocImageFormat` to resolve a `CS0436` type-conflict with `PdfLibrary`'s rendering `ImageFormat` enum (both were in the global namespace). External callers using the bloc-table image format must update accordingly.

### Removed
- **`JpegLibrary` git submodule.** The vendored `JpegLibrary` fork has been removed from the tree; cloning no longer requires `--recurse-submodules`. The `PdfLibrary.Filters.JpegLibraryAdapter` wrapper has also been deleted — `DctDecodeFilter` now uses `ImageLibrary.JpegCodec` directly.
- Removed the legacy aggregated `ImageLibrary.ImageLibrary` project; consumers should reference the specific codec project(s) they need.
- **ImageSharp dependency.** `SixLabors.ImageSharp` has been removed from `ImageUtility.csproj`. Every supported format is now backed by an in-tree codec; WebP support has been intentionally dropped (no in-house VP8/VP8L implementation is planned — WebP is not a PDF image filter).
- **Codec Settings UI in ImageUtility.** The Tools → Codec Settings dialog (`CodecSettingsWindow`) and the supporting `CodecConfiguration` preference store have been removed. With one codec per format there is no preference to express; `CodecRegistry.FindByExtension` now resolves directly without consulting user preferences.

## [0.0.10-beta] - 2025-01-13

### Added
- **Fluent Builder API** - Complete API for programmatic PDF creation
  - Document creation with `PdfDocumentBuilder.Create()`
  - Page content: text, images, shapes, and paths
  - Interactive forms: text fields, checkboxes, radio buttons, dropdowns, signature fields
  - Annotations: internal links, external links, notes (sticky notes), highlights
  - Bookmarks/outlines with hierarchical navigation
  - Page labels with multiple numbering styles (decimal, roman, letters)
  - Layers (Optional Content Groups) with visibility and print control
  - Document encryption (RC4, AES-128, AES-256)
  - Custom font embedding (TrueType, OpenType)
  - Metadata support (title, author, subject, keywords)
  - Flexible coordinate systems (points, inches, mm, cm) and origins

- **Integration Testing Framework** - Automated testing infrastructure
  - Visual comparison testing
  - Golden file generation and comparison

### Changed
- Split rendering framework into separate `PdfLibrary.Rendering.SkiaSharp` project
- Improved font resolution and fallback logic
- Enhanced text positioning accuracy

### Fixed
- Font widths calculation issues
- Text rotation rendering
- JPEG image rendering
- Clipping path issues with transformations
- Percentage calculation in color spaces

---

## [0.9.0] - 2024-XX-XX

### Added
- **PDF Rendering Engine**
  - Full PDF 1.x and 2.0 parsing support
  - High-quality rendering using SkiaSharp
  - Comprehensive graphics state handling

- **Color Space Support**
  - DeviceGray, DeviceRGB, DeviceCMYK
  - CalGray, CalRGB, Lab
  - ICCBased profiles
  - Indexed color spaces
  - Separation and DeviceN
  - Tiling and shading patterns

- **Image Decompression**
  - DCTDecode (JPEG)
  - FlateDecode (zlib/deflate)
  - LZWDecode
  - CCITTFaxDecode (Group 3 and 4)
  - JBIG2Decode
  - JPXDecode (JPEG2000)
  - RunLengthDecode
  - ASCII85Decode, ASCIIHexDecode

- **Font Support**
  - Type1 and Type1C (CFF) fonts
  - TrueType fonts
  - Type0 (CID) fonts for CJK
  - Type3 user-defined fonts
  - Embedded font extraction
  - System font fallback

- **Security**
  - RC4 40-bit and 128-bit decryption
  - AES 128-bit and 256-bit decryption
  - Permission handling

- **Content Stream Processing**
  - Graphics state operators
  - Path construction and painting
  - Text positioning and rendering
  - Color operators
  - XObject handling
  - Inline images
  - Marked content

### Infrastructure
- Modular compressor libraries (LZW, CCITT, JBIG2, JPEG2000)
- FontParser library for TrueType/OpenType parsing
- Centralized logging with Serilog
- Command-line tool (PdfTool) for testing

---

## Version History Summary

| Version | Date | Highlights |
|---------|------|------------|
| 0.9.0 | TBD | Core rendering engine, font support, image decompression |
| 0.0.10-beta | 2025-01-13 | Fluent builder API, annotations, bookmarks, page labels, encryption |
| 1.0.0-rc.1 | 2026-06-06 | Release candidate for 1.0: thread-safe concurrent rendering, pure-C# in-house image codecs (no third-party deps), performance + memory optimizations. |
| 1.0.0 | 2026-06-21 | First stable release: editing/mutation API, optimization API, multi-targets .NET 8/9/10, all dependencies on stable releases (SkiaSharp 3.119.4). |

---

## Migration Guide

### Upgrading to Fluent API

The new fluent API provides a simpler way to create PDFs:

**Before (manual object creation):**
```csharp
// Complex manual object construction
var doc = new PdfDocument();
var page = new PdfPage(612, 792);
var stream = new PdfContentStream();
stream.AddOperator("BT");
stream.AddOperator("/F1 12 Tf");
// ... etc
```

**After (fluent API):**
```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700)
            .WithFont("Helvetica")
            .WithSize(12))
    .Save("output.pdf");
```

### Rendering API Changes

The rendering pipeline was refactored for better separation:

**Before:**
```csharp
// SkiaSharp directly in PdfLibrary
var renderer = new PdfRenderer(document);
renderer.RenderToSkia(canvas);
```

**After:**
```csharp
// Separate render target
using var target = new SkiaSharpRenderTarget(width, height);
var renderer = new PdfRenderer(document, target);
renderer.RenderPage(0);
target.SavePng("output.png");
```
