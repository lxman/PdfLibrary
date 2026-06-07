# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

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
| 1.0.0-rc.1 | 2026-06-06 | Release candidate for 1.0: thread-safe concurrent rendering, pure-C# in-house image codecs (no third-party deps), performance + memory optimizations. Prerelease until SkiaSharp 3.x ships stable. |

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
