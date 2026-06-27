# PdfLibrary

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

A comprehensive .NET library for parsing, rendering, creating, editing, and optimizing PDF documents. Built with C# and multi-targeting .NET 8, 9, and 10.

---

## Breaking Changes — 2.0.0

> **If you are upgrading from 1.x, read this before installing.**

| Category | What changed |
|---|---|
| **Rendering architecture** | Core `Lxman.PdfLibrary` is now SkiaSharp-free. Rendering goes through a geometry-only `IRenderTarget` SPI (`PdfLibrary.Rendering`): the core flattens everything — including text — to glyph-outline paths and emits geometry only. Bring your own render target. Bundled std-14 substitute fonts + font-bytes provider are included in the core. |
| **Packages** | `Lxman.PdfLibrary.Rendering.Wpf` (Windows-only) is now the published rendering package. `Lxman.PdfLibrary.Rendering.SkiaSharp` is **sunset** — no longer published to NuGet; it remains in-repo as a pixel-fidelity test gate only. |
| **WPF rendering API** | New: `page.RenderToDrawing(double scale)` → retained WPF `DrawingGroup` (vector, crisp at any zoom). `DrawingGroup.ToPageImage(pixelWidth, pixelHeight)` wraps it for `<Image Stretch="Uniform"/>`. |
| **Forms geometry API** | New public surface: `PdfPage.GetGeometry(double scale)` → `PageGeometry`; `ImageRect`; `PdfFieldWidget`; `PdfFormField.Widgets`, `.FontName`, `.FontSize`. |
| **Viewer app** | `PdfLibrary.Wpf.Viewer` is rewired to pure WPF — renders via `WpfRenderTarget` → `DrawingGroup` (vector); overlays native fillable form controls (text/checkbox/radio/combo/list) with write-back + Save/Flatten; honors field quadding and `/DA` font/size. |
| **API cleanups** | Dead `IRenderBuilder<TImage>` interface and `ImageFormat` enum removed; `PdfImageBuilder.Done()` terminal added; `ButtonKind.Push` renamed to `ButtonKind.PushButton`; `PdfColor.Components` and `PdfColor.GrayValue` accessors are now public. |
| **Dev/infra** | All test projects migrated to xUnit v3. |

**Migration quick guide:** Replace `using PdfLibrary.Rendering.SkiaSharp;` and `page.RenderTo()…` with `using PdfLibrary.Rendering.Wpf;` + `page.RenderToDrawing(scale)` (Windows/WPF). To target a different platform, implement `IRenderTarget` from `PdfLibrary.Rendering` — the core emits only geometry calls. The SkiaSharp in-repo implementation in `PdfLibrary.Rendering.SkiaSharp/` can serve as a reference.

---

## Features

### PDF Rendering
- Full PDF 1.x and 2.0 parsing support
- Geometry-only `IRenderTarget` SPI — core emits glyph outlines as filled paths, images, and clip geometry; no SkiaSharp dependency in the core
- Bundled WPF render target (`Lxman.PdfLibrary.Rendering.Wpf`): renders to a retained `DrawingGroup` (vector, crisp at any zoom) via `page.RenderToDrawing(scale)`
- Bring-your-own render target: implement `IRenderTarget` (17 required members) to integrate any 2D drawing API (Avalonia, Direct2D, SVG, …)
- Support for complex graphics operations (paths, clipping, transparency)
- Memory-efficient image processing using `ArrayPool<byte>` and pre-allocated buffers
- Comprehensive color space support (DeviceRGB, DeviceCMYK, DeviceGray, ICCBased, Separation, Lab)
- Image handling with custom high-performance decompressors:
  - JPEG (in-house JpegCodec — baseline and progressive)
  - JPEG2000 (JP2/J2K via in-house Jp2Codec)
  - CCITT Group 3 and Group 4 fax compression
  - JBIG2 monochrome compression
  - LZW compression
  - FlateDecode (zlib/PNG) with optimized predictor functions
  - RunLength, ASCII85, ASCIIHex encoding
- Font rendering (Type1, TrueType, CID, embedded fonts) with optimized glyph path extraction
- Text extraction and positioning

### PDF Creation (Fluent Builder API)
- Intuitive fluent API for document creation
- Text with full styling (fonts, colors, spacing)
- Vector graphics (rectangles, circles, lines, paths)
- Image embedding (JPEG, PNG)
- Interactive forms (text fields, checkboxes, radio buttons, dropdowns)
- Annotations (links, notes, highlights)
- Bookmarks/Outlines with hierarchical navigation
- Page labels (custom numbering schemes)
- Layers (Optional Content Groups)
- Encryption (RC4, AES-128, AES-256)
- Custom font embedding (TrueType, OpenType)

### PDF Editing
- Edit a loaded document via `PdfDocument.Edit()` → `PdfDocumentEditor`
- Page operations: rotate, reorder, delete, insert blank pages
- Merge multiple PDFs, split out page ranges, import/duplicate pages
- Deleting a page cleans up bookmarks, named destinations, and links that pointed at it
- Importing a page brings its interactive form fields across
- Full-rewrite save (classic xref or object streams) with automatic garbage collection

### PDF Optimization
- Optimize a loaded document via `PdfOptimizer.Optimize()`
- Lossless by default: Flate-compress uncompressed streams, drop unused objects, pack into object streams
- Opt-in lossy passes: re-encode images as JPEG (with optional downsampling), subset embedded fonts (TrueType and CFF) to the glyphs actually used
- Encrypted input is decrypted and written out unencrypted

## Project Structure

```
PDF/
├── PdfLibrary/                       # Core library (SkiaSharp-free)
│   ├── Document/                     # PDF document model
│   ├── Structure/                    # PDF structure (xref, trailer, objects)
│   ├── Parsing/                      # PDF lexer/parser
│   ├── Content/                      # Content stream processing
│   ├── Filters/                      # Stream decode filters (Flate, JBIG2Decode, etc.)
│   ├── Rendering/                    # IRenderTarget SPI + geometry pipeline
│   ├── Builder/                      # Fluent API for PDF creation
│   ├── Editing/                      # Edit/mutate loaded documents (pages, merge, split)
│   ├── Optimization/                 # Optimize/compress loaded documents
│   ├── Fonts/                        # Font handling + std-14 substitute locator
│   ├── Functions/                    # PDF function objects
│   ├── Fixups/                       # Per-document corrective passes
│   ├── Core/                         # Primitive types
│   └── Security/                     # Encryption/decryption
├── PdfLibrary.Rendering.Wpf/         # WPF render target (published; Windows-only)
├── PdfLibrary.Rendering.Svg/         # SVG render target (reference implementation)
├── PdfLibrary.Rendering.SkiaSharp/   # SkiaSharp render target (test-only; not published)
├── PdfLibrary.Tests/                 # Unit tests
├── PdfLibrary.Integration/           # Integration tests
├── PdfLibrary.Wpf.Viewer/            # WPF PDF viewer application
├── PdfLibrary.Utilities/             # Utility applications
│   └── ImageUtility/                 # Image format viewer with codec system
├── PdfLibrary.Examples/              # Standalone usage samples
├── ImageLibrary/                     # Pure-C# image format library — one project per codec
│   ├── CcittCodec/                   # CCITT Group 3 (1D/2D) and Group 4 fax
│   ├── LzwCodec/                     # LZW compression (with Early Change support)
│   ├── JpegCodec/                    # JPEG (baseline + progressive, encode + decode)
│   ├── Jbig2Decoder/                 # JBIG2 decoder (ITU-T T.88)
│   ├── BmpCodec/                     # BMP container
│   ├── GifCodec/                     # GIF container (with LZW)
│   ├── PngCodec/                     # PNG container
│   ├── TgaCodec/                     # TGA container
│   ├── TiffCodec/                    # TIFF container (uses CcittCodec + LzwCodec)
│   ├── CcittCodec.Tests/
│   ├── LzwCodec.Tests/
│   ├── JpegCodec.Tests/
│   ├── Jbig2Decoder.Tests/
│   ├── BmpCodec.Tests/
│   ├── GifCodec.Tests/
│   ├── PngCodec.Tests/
│   ├── TgaCodec.Tests/
│   └── ImageLibrary.IntegrationTests/
├── FontParser/                       # TrueType/OpenType parsing
├── Logging/                          # Logging infrastructure
└── Docs/                             # Documentation
```

## Quick Start

### Rendering a PDF (WPF)

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Wpf;   // from Lxman.PdfLibrary.Rendering.Wpf (Windows-only)
using System.Windows.Media;

// Load a PDF document
using var doc = PdfDocument.Load("document.pdf");
PdfPage page = doc.GetPage(0)!;   // 0-based index

// Render to a retained WPF DrawingGroup (vector — crisp at any zoom)
// Must be called on an STA thread.
DrawingGroup drawing = page.RenderToDrawing(scale: 1.0);   // 1.0 = 72 DPI

// Wrap for use in <Image Stretch="Uniform"/> — fixes bounds to the full page rect
PageGeometry geo = page.GetGeometry(scale: 1.0);
DrawingImage pageImage = drawing.ToPageImage(geo.PixelWidth, geo.PixelHeight);
// myImage.Source = pageImage;
```

**Custom render target:** To target a non-WPF backend (Avalonia, Direct2D, SVG, …) implement `IRenderTarget` from `PdfLibrary.Rendering`, then call `page.Render(myTarget, pageNumber: 1, scale: 1.0)`. The in-repo `SvgRenderTarget` and `SkiaSharpRenderTarget` are worked examples.

### Creating a PDF

```csharp
using PdfLibrary.Builder;

PdfDocumentBuilder.Create()
    .WithMetadata(meta => meta
        .SetTitle("My Document")
        .SetAuthor("John Doe"))
    .AddPage(page =>
    {
        page.AddText("Hello, World!", 100, 750)
            .Font("Helvetica-Bold", 24)
            .Color(PdfColor.Blue);
        page.AddRectangle(100, 650, 200, 30,
            fillColor: PdfColor.LightGray, strokeColor: PdfColor.Black);
    })
    .AddPage(page => page.AddText("Page 2", 100, 700, "Helvetica", 12))
    .AddBookmark("Page 1", 0)
    .AddBookmark("Page 2", 1)
    .Save("output.pdf");
```

### Creating a Form

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page =>
    {
        page.AddText("Registration Form", 100, 750, "Helvetica-Bold", 18);
        page.AddText("Name:", 100, 700, "Helvetica", 12);
        page.AddTextField("name", 170, 695, 200, 25).Required();
        page.AddText("Email:", 100, 660, "Helvetica", 12);
        page.AddTextField("email", 170, 655, 200, 25);
        page.AddText("I agree to terms:", 100, 620, "Helvetica", 12);
        page.AddCheckbox("agree", 220, 618, 18);   // name, x, y, size
    })
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("form.pdf");
```

### Forms Geometry — Overlaying UI Controls

The `PageGeometry` API maps between PDF user space and rendered-image pixels so you can overlay native controls (WPF, Avalonia, …) precisely over form fields:

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Rendering.Wpf;

using var doc = PdfDocument.Load("form.pdf");
PdfPage page = doc.GetPage(0)!;
double scale = 1.5;

DrawingGroup drawing = page.RenderToDrawing(scale);
PageGeometry geo = page.GetGeometry(scale);

// Each form field can have one or more widget annotations (visual locations on a page)
var editor = doc.Edit();
foreach (PdfFormField field in editor.Forms)
{
    foreach (PdfFieldWidget widget in field.Widgets)
    {
        if (widget.PageIndex != 0) continue;
        ImageRect rect = geo.MapRectToImage(widget.Rect);
        // Place a native TextBox at (rect.X, rect.Y) with rect.Width × rect.Height
        // using field.FontName and field.FontSize for styling
    }
}
```

### Text Extraction

```csharp
// Extract all text from a page
var page = doc.GetPage(0)!;
string textContent = page.ExtractText();

// Extract text with positioning information
var (text, fragments) = page.ExtractTextWithFragments();
foreach (var f in fragments)
{
    Console.WriteLine($"Text: {f.Text}");
    Console.WriteLine($"Position: ({f.X}, {f.Y})");
    Console.WriteLine($"Font: {f.FontName}, Size: {f.FontSize}");
}
```

### Editing an Existing PDF

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using var doc = PdfDocument.Load("input.pdf");
var edit = doc.Edit();

edit.Pages.RemoveAt(2);       // delete the 3rd page
edit.Pages.Rotate(0, 90);     // rotate the 1st page 90°
edit.Pages.Move(4, 0);        // move the 5th page to the front

edit.Save("edited.pdf");

// Merge several PDFs into one
using var a = PdfDocument.Load("part1.pdf");
using var b = PdfDocument.Load("part2.pdf");
using PdfDocument merged = PdfDocumentEditor.Merge([a, b]);
merged.Save("combined.pdf");
```

See the [Complete Guide](Docs/Guide.md) for the full surface.

### Optimizing a PDF

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Optimization;

using var doc = PdfDocument.Load("input.pdf");
using var output = File.Create("optimized.pdf");

// Lossless by default (Flate + object streams + unused-object GC)
PdfOptimizationResult result = PdfOptimizer.Optimize(doc, output);
Console.WriteLine($"Removed {result.ObjectsRemoved} objects; wrote {result.OutputBytes} bytes");

// Opt in to lossy size reductions
PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions
{
    RecompressImages = true,   // lossy: re-encode images as JPEG
    SubsetFonts      = true,   // discard unused glyphs in embedded fonts
});
```

## Thread Safety

PdfLibrary supports **concurrent rendering using the one-document-per-thread model** — the standard pattern for ASP.NET Core and other multi-threaded servers. Each request loads its own `PdfDocument`, renders it on its own render target, and disposes both. Under this model the library is thread-safe: the process-wide caches and lookup tables shared across renders (glyph-path cache, system-font/typeface resolver, built-in ICC profiles, codec registry, font lookup tables) are synchronized, and CFF/Type1 glyph decoding uses per-parse state.

This is verified by a stress harness that renders a corpus concurrently at 2× core count and compares every page's output pixel-for-pixel against a single-threaded baseline — zero divergence, with managed memory bounded across thousands of renders. No process-wide render lock is required; throughput scales with cores.

### Supported pattern (do this)

```csharp
// Per request/thread: load → render → dispose. No shared state, no global lock.
public async Task<byte[]> RenderFirstPageAsync(string pdfPath)
{
    using var document = PdfDocument.Load(pdfPath);
    return document.GetPage(0)!     // 0-based
        .RenderTo()
        .WithDpi(150)
        .ToBytes();                 // PNG bytes
}
```

### Constraints (don't do this)

- **Do not share one `PdfDocument` across threads.** It lazy-loads objects by mutating internal state and seeking a shared `Stream`; concurrent access to a single instance is unsafe. Load one per request instead.
- **Do not share an `IRenderTarget` implementation across threads.** Render targets hold per-render mutable state. Use one render target per render.
- `PdfDocumentBuilder` is not thread-safe during construction — build a document on a single thread.

If the same PDFs are rendered repeatedly, caching the rendered output at the HTTP layer is still worthwhile — but as an optimization, not a correctness requirement.

## Image Decompression Architecture

PdfLibrary uses custom-built, high-performance decompression libraries for all PDF image formats. These are **pure C# implementations** with no external dependencies:

### Custom Decompressors
- **JpegCodec** - DCTDecode filter (in-house, baseline + progressive JPEG, encode + decode)
- **Jp2Codec** - JPXDecode filter, in-house JPEG 2000 decoder (decode only)
- **CcittCodec** - CCITTFaxDecode filter (Group 3 1D/2D and Group 4)
- **Jbig2Decoder** - JBIG2Decode filter for monochrome document compression (ITU-T T.88, used directly by `PdfLibrary.Filters.Jbig2DecodeFilter`)
- **LzwCodec** - LZWDecode filter with Early Change support
- **PdfLibrary.Filters.FlateDecodeFilter** - FlateDecode (DEFLATE) using `System.IO.Compression`

### Integration
PDF stream filters in `PdfLibrary/Filters/` are thin adapters: each maps PDF filter parameters onto the underlying codec library and returns decoded bytes in the layout the renderer expects. Image containers (BMP/GIF/PNG/TGA/TIFF/PBM) live in their own per-codec projects under `ImageLibrary/` and are used by the standalone `ImageUtility` application; PDF rendering only consumes the codec layer (`JpegCodec`, `Jp2Codec`, `LzwCodec`, `CcittCodec`, `Jbig2Decoder`).

## Requirements

- .NET 8.0, 9.0, or 10.0
- `Lxman.PdfLibrary` — core: load, parse, create, edit, optimize (no native or SkiaSharp dependency)
- `Lxman.PdfLibrary.Rendering.Wpf` — WPF render target; Windows-only; requires an STA thread for rendering

### Core Dependencies
- **Serilog** - Structured logging
- **Unicolour** - Advanced color space transformations
- **In-tree codec libraries** (all pure C#, no third-party codec dependencies):
  - `ImageLibrary/JpegCodec` - JPEG (DCTDecode) baseline and progressive
  - `ImageLibrary/Jp2Codec` - JPEG 2000 (JPXDecode), decode only
  - `ImageLibrary/LzwCodec` - LZW (LZWDecode) with Early Change support
  - `ImageLibrary/CcittCodec` - CCITT (CCITTFaxDecode) Group 3 1D/2D and Group 4
  - `ImageLibrary/Jbig2Decoder` - JBIG2 (JBIG2Decode, ITU-T T.88)

**Note**: `Lxman.PdfLibrary` has no SkiaSharp or native-image-format dependencies. All image decoding is backed by in-tree codecs. The SkiaSharp backend (`PdfLibrary.Rendering.SkiaSharp`) is retained in-repo as a pixel-fidelity test gate but is not published to NuGet.

## Building

```bash
# Clone the repository
git clone https://github.com/lxman/PdfLibrary.git
cd PdfLibrary

# Build the solution
dotnet build PdfLibrary.slnx

# Run tests
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```

All codec implementations are in-tree (no git submodules required).

## Documentation

- [Complete Guide](Docs/Guide.md) - Loading, reading, rendering, creating, editing, and optimizing PDFs
- [Architecture](Docs/Architecture.md) - Technical architecture overview

## Supported PDF Features

### Content Streams
- Graphics state operators (q, Q, cm, w, J, j, M, d, ri, i, gs)
- Path operators (m, l, c, v, y, h, re, S, s, f, F, f*, B, B*, b, b*, n, W, W*)
- Text operators (BT, ET, Tc, Tw, Tz, TL, Tf, Tr, Ts, Td, TD, Tm, T*, Tj, TJ, ', ")
- Color operators (CS, cs, SC, SCN, sc, scn, G, g, RG, rg, K, k)
- XObject operators (Do)
- Inline image operators (BI, ID, EI)
- Marked content operators (MP, DP, BMC, BDC, EMC)

### Color Spaces
- DeviceGray, DeviceRGB, DeviceCMYK
- CalGray, CalRGB, Lab
- ICCBased
- Indexed
- Separation, DeviceN
- Pattern (tiling and shading)

### Fonts
- Type1, Type1C (CFF)
- TrueType
- Type0 (CID fonts)
- Type3
- Embedded and system fonts

### Images
- DCTDecode (JPEG)
- FlateDecode (PNG/zlib)
- LZWDecode
- CCITTFaxDecode (Group 3 and 4)
- JBIG2Decode
- JPXDecode (JPEG2000)
- RunLengthDecode
- ASCII85Decode, ASCIIHexDecode

### Security
- RC4 40-bit and 128-bit encryption
- AES 128-bit and 256-bit encryption
- Permission flags

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

### Development Setup

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Code Style

- Follow C# coding conventions
- Use meaningful variable and method names
- Add XML documentation for public APIs
- Include unit tests for new features

## Acknowledgments

### Core Library
- [Serilog](https://serilog.net/) - Structured logging framework
- [Unicolour](https://github.com/waacton/Unicolour) - Advanced color space handling and transformations

### Test-time references
- [SkiaSharp](https://github.com/mono/SkiaSharp) - Used only by the in-repo `PdfLibrary.Rendering.SkiaSharp` project as a pixel-fidelity test gate; not a runtime dependency of any published package.
- [Melville.CSJ2K](https://www.nuget.org/packages/Melville.CSJ2K) - Used only by `ImageLibrary/Jp2Codec.Tests` as a differential reference for in-house JPEG 2000 conformance testing; not a runtime dependency of `PdfLibrary`.
