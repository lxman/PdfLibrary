# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

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
| Unreleased | - | Fluent builder API, annotations, bookmarks, page labels, encryption |

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
