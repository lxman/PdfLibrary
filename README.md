# PdfLibrary

[![NuGet](https://img.shields.io/nuget/v/Lxman.PdfLibrary.svg?label=Lxman.PdfLibrary)](https://www.nuget.org/packages/Lxman.PdfLibrary)
[![NuGet](https://img.shields.io/nuget/v/Lxman.PdfLibrary.Rendering.Wpf.svg?label=Lxman.PdfLibrary.Rendering.Wpf)](https://www.nuget.org/packages/Lxman.PdfLibrary.Rendering.Wpf)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4)](https://dotnet.microsoft.com/)

**Everything you need to work with PDFs in .NET — read, render, create, edit, optimize, and validate — in pure C#.**

No native binaries. No platform-specific image libraries. Every codec — JPEG, JPEG 2000, JBIG2, CCITT fax, LZW — is implemented in C#, in this repository. If you can run .NET 8, 9, or 10, you can run PdfLibrary.

## What can it do?

- **Read** — parse any PDF 1.x or 2.0 document; extract text (with positions and fonts), embedded files, metadata, tag trees, and output intents
- **Render** — turn pages into crisp vector drawings (WPF out of the box) or plug in your own drawing backend
- **Create** — build documents from scratch with a fluent API: text, graphics, images, forms, bookmarks, layers, encryption
- **Edit** — rotate, reorder, delete, and merge pages in existing documents; fill forms; stamp and watermark; attach files
- **Optimize** — shrink files with lossless compression by default, plus opt-in image recompression and font subsetting
- **Validate** — preflight against PDF/A, PDF/X-4, and PDF/UA-1 profiles with structured, clause-level findings

## Installation

```bash
# The core library — load, parse, create, edit, optimize, validate
dotnet add package Lxman.PdfLibrary

# Add this if you want to render pages on Windows (WPF)
dotnet add package Lxman.PdfLibrary.Rendering.Wpf
```

The core package has no rendering dependency at all — it's safe for servers, containers, and any OS. Rendering goes through a small `IRenderTarget` interface, so on non-Windows platforms you can bring your own drawing backend (more on that below).

## Quick Start

### Load a PDF and extract text

```csharp
using PdfLibrary.Structure;

using var doc = PdfDocument.Load("document.pdf");
var page = doc.GetPage(0)!;   // 0-based index

string text = page.ExtractText();

// Or get every fragment with its position and font
var (fullText, fragments) = page.ExtractTextWithFragments();
foreach (var f in fragments)
    Console.WriteLine($"\"{f.Text}\" at ({f.X}, {f.Y}) in {f.FontName} {f.FontSize}pt");
```

### Create a PDF from scratch

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;   // PdfColor

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

### Render a page (WPF)

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Wpf;   // from Lxman.PdfLibrary.Rendering.Wpf (Windows-only)
using System.Windows.Media;

using var doc = PdfDocument.Load("document.pdf");
PdfPage page = doc.GetPage(0)!;

// A retained WPF DrawingGroup — vector, so it stays crisp at any zoom.
// Must be called on an STA thread.
DrawingGroup drawing = page.RenderToDrawing(scale: 1.0);   // 1.0 = 72 DPI

// Wrap it for <Image Stretch="Uniform"/> — fixes bounds to the full page rect
PageGeometry geo = page.GetGeometry(scale: 1.0);
DrawingImage pageImage = drawing.ToPageImage(geo.PixelWidth, geo.PixelHeight);
// myImage.Source = pageImage;
```

Not on Windows? Implement `IRenderTarget` (17 members) from `PdfLibrary.Rendering` and call `page.Render(myTarget, pageNumber: 1, scale: 1.0)` — the core hands you nothing but geometry (filled paths, images, clips), so any 2D drawing API works: Avalonia, Direct2D, SVG, and so on. The in-repo `SvgRenderTarget` and `SkiaSharpRenderTarget` are worked examples to crib from.

### Edit an existing PDF

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

Deleting a page also cleans up the bookmarks, named destinations, and links that pointed at it — no dangling references.

### Shrink a PDF

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Optimization;

using var doc = PdfDocument.Load("input.pdf");
using var output = File.Create("optimized.pdf");

// Lossless by default: Flate compression, object streams, unused-object cleanup
PdfOptimizationResult result = PdfOptimizer.Optimize(doc, output);
Console.WriteLine($"Removed {result.ObjectsRemoved} objects; wrote {result.OutputBytes} bytes");

// Opt in to lossy passes when you need the file smaller still
PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions
{
    RecompressImages = true,   // lossy: re-encode images as JPEG
    SubsetFonts      = true,   // discard unused glyphs in embedded fonts
});
```

### Check conformance (PDF/A, PDF/X, PDF/UA)

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Conformance;

using var doc = PdfDocument.Load("document.pdf");

// Read-only: never mutates the document
PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

Console.WriteLine(result.Conforms
    ? "Conforms (no violations among the checked rules)"
    : "Not conformant");

foreach (Finding f in result.Errors)
    Console.WriteLine($"[{f.Severity}] {f.Clause}: {f.Message}");
```

Profiles: `PdfA2b`, `PdfA2u`, `PdfA3b` (ISO 19005 archival), `PdfX4` (ISO 15930-7 print), `PdfUA1` (ISO 14289-1 accessibility). This is a *structural* validator — a deliberately partial, machine-decidable subset of each standard, not a certification. A "conforms" result means "no violations among the checked rules". Rules are cross-checked against the veraPDF conformance corpus and tuned for zero false positives on conformant files. `Preflighter.Check` also accepts a file path or a `byte[]`.

## More recipes

<details>
<summary><strong>Build a fillable form</strong></summary>

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
</details>

<details>
<summary><strong>Overlay native UI controls on form fields</strong></summary>

The `PageGeometry` API maps between PDF user space and rendered-image pixels, so you can place native controls (WPF, Avalonia, …) precisely over form fields:

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;   // PdfFormField, PdfFieldWidget
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
</details>

<details>
<summary><strong>Read embedded files (e.g. Factur-X invoice attachments)</strong></summary>

```csharp
using PdfLibrary.Structure;

using var doc = PdfDocument.Load("invoice.pdf");
foreach (var f in doc.GetEmbeddedFiles())
{
    Console.WriteLine($"{f.FileName} ({f.MimeType}) — {f.AfRelationship}");
    if (f.HasData)
        File.WriteAllBytes(f.FileName ?? "attachment.bin", f.GetDataBytes()!);
}
```
</details>

<details>
<summary><strong>Author a PDF/A-3 document (embed files + output intent)</strong></summary>

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using var doc = PdfDocument.Load("input.pdf");
var edit = doc.Edit();

edit.AddEmbeddedFile(new PdfEmbeddedFileSpec
{
    Name = "factur-x.xml",
    Data = File.ReadAllBytes("factur-x.xml"),
    MimeType = "text/xml",
    Description = "Factur-X invoice data"
});
edit.AddOutputIntent(File.ReadAllBytes("sRGB.icc"), "sRGB IEC61966-2.1");

edit.Save("output.pdf");
```
</details>

See the [Complete Guide](Docs/Guide.md) for the full API surface.

## Feature tour

### Rendering
- Full PDF 1.x and 2.0 parsing support
- Geometry-only `IRenderTarget` SPI — the core emits glyph outlines as filled paths, images, and clip geometry; no SkiaSharp or native dependency
- Bundled WPF render target (`Lxman.PdfLibrary.Rendering.Wpf`): renders to a retained `DrawingGroup` (vector, crisp at any zoom) via `page.RenderToDrawing(scale)`
- Bring-your-own render target for any other 2D drawing API (Avalonia, Direct2D, SVG, …)
- Complex graphics operations: paths, clipping, transparency, all seven shading types
- Comprehensive color space support (DeviceRGB, DeviceCMYK, DeviceGray, ICCBased, Separation, Lab)
- Font rendering (Type1, TrueType, CID, embedded fonts) with optimized glyph path extraction
- Memory-efficient image processing using `ArrayPool<byte>` and pre-allocated buffers
- Bundled standard-14 substitute fonts, so text renders even without system fonts

### Creation (fluent builder)
- Text with full styling (fonts, colors, spacing)
- Vector graphics (rectangles, circles, lines, paths)
- Image embedding (JPEG, PNG)
- Interactive forms (text fields, checkboxes, radio buttons, dropdowns)
- Annotations (links, notes, highlights)
- Bookmarks/outlines, page labels, layers (Optional Content Groups)
- Encryption (RC4, AES-128, AES-256)
- Custom font embedding (TrueType, OpenType)

### Editing
- `PdfDocument.Edit()` → `PdfDocumentEditor` for in-place changes
- Page operations: rotate, reorder, delete, insert blank pages
- Merge multiple PDFs, split out page ranges, import/duplicate pages (form fields come along)
- Full-rewrite save (classic xref or object streams) with automatic garbage collection

### Optimization
- Lossless by default: Flate-compress uncompressed streams, drop unused objects, pack into object streams
- Opt-in lossy passes: re-encode images as JPEG (with optional downsampling), subset embedded fonts (TrueType and CFF) to the glyphs actually used
- Encrypted input is decrypted and written out unencrypted

### Inspection & metadata
- Embedded/attached files: name, MIME subtype, `/AFRelationship`, decoded bytes — never throws on malformed attachments
- Tagged-PDF logical-structure tree (`PdfDocument.GetTagTree()`) for accessibility inspection
- ICC output intents (`PdfDocument.GetOutputIntents()`) and per-page colorant inventory (`PdfDocument.GetPageColorants(pageIndex)`)

## Using it on a server (thread safety)

PdfLibrary supports **concurrent rendering using the one-document-per-thread model** — the standard pattern for ASP.NET Core and other multi-threaded servers. Each request loads its own `PdfDocument`, renders it on its own render target, and disposes both. Under this model the library is thread-safe: the process-wide caches and lookup tables shared across renders (glyph-path cache, system-font/typeface resolver, built-in ICC profiles, codec registry, font lookup tables) are synchronized, and CFF/Type1 glyph decoding uses per-parse state.

This is verified by a stress harness that renders a corpus concurrently at 2× core count and compares every page's output pixel-for-pixel against a single-threaded baseline — zero divergence, with managed memory bounded across thousands of renders. No process-wide render lock is required; throughput scales with cores.

```csharp
// Per request/thread: load → render → dispose. No shared state, no global lock.
public byte[] RenderFirstPage(string pdfPath)
{
    using var document = PdfDocument.Load(pdfPath);
    PdfPage page = document.GetPage(0)!;              // 0-based

    // One render target per render — never shared across threads.
    using var target = new MyRenderTarget(page);      // your IRenderTarget (WPF, Avalonia, …)
    page.Render(target, pageNumber: 1, scale: 2.0);
    return target.GetImageBytes();
}
```

Three rules keep you safe:

- **Don't share one `PdfDocument` across threads.** It lazy-loads objects by mutating internal state and seeking a shared `Stream`. Load one per request instead.
- **Don't share an `IRenderTarget` across threads.** Render targets hold per-render mutable state — use one per render.
- **Build documents on a single thread.** `PdfDocumentBuilder` is not thread-safe during construction.

If the same PDFs are rendered repeatedly, caching the rendered output at the HTTP layer is still worthwhile — but as an optimization, not a correctness requirement.

## How the codecs work

Every image format a PDF can contain is decoded by a pure-C# codec that lives in this repository — no external image libraries, no P/Invoke:

| Codec | PDF filter | Notes |
|---|---|---|
| **JpegCodec** | DCTDecode | Baseline + progressive, encode + decode |
| **Jp2Codec** | JPXDecode | JPEG 2000 (JP2/J2K), decode |
| **CcittCodec** | CCITTFaxDecode | Group 3 1D/2D and Group 4 fax |
| **Jbig2Decoder** | JBIG2Decode | Monochrome document compression (ITU-T T.88) |
| **LzwCodec** | LZWDecode | With Early Change support |
| **FlateDecodeFilter** | FlateDecode | DEFLATE via `System.IO.Compression`, optimized predictors |

PDF stream filters in `PdfLibrary/Filters/` are thin adapters: each maps PDF filter parameters onto the underlying codec and returns decoded bytes in the layout the renderer expects. The image *containers* (BMP/GIF/PNG/TGA/TIFF/PBM) live in their own per-codec projects under `ImageLibrary/` and back the standalone `ImageUtility` application; PDF rendering only consumes the codec layer.

## Supported PDF features

<details>
<summary><strong>Full support matrix</strong></summary>

### Content streams
- Graphics state operators (q, Q, cm, w, J, j, M, d, ri, i, gs)
- Path operators (m, l, c, v, y, h, re, S, s, f, F, f*, B, B*, b, b*, n, W, W*)
- Text operators (BT, ET, Tc, Tw, Tz, TL, Tf, Tr, Ts, Td, TD, Tm, T*, Tj, TJ, ', ")
- Color operators (CS, cs, SC, SCN, sc, scn, G, g, RG, rg, K, k)
- XObject operators (Do)
- Inline image operators (BI, ID, EI)
- Marked content operators (MP, DP, BMC, BDC, EMC)

### Color spaces
- DeviceGray, DeviceRGB, DeviceCMYK
- CalGray, CalRGB, Lab
- ICCBased
- Indexed
- Separation, DeviceN
- Pattern (tiling and shading — all seven shading types)

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
</details>

## Repository layout

```
PDF/
├── PdfLibrary/                       # Core library (no rendering dependency)
│   ├── Document/                     # PDF document model
│   ├── Structure/                    # PDF structure (xref, trailer, objects)
│   ├── Parsing/                      # PDF lexer/parser
│   ├── Content/                      # Content stream processing
│   ├── Filters/                      # Stream decode filters (Flate, JBIG2Decode, etc.)
│   ├── Rendering/                    # IRenderTarget SPI + geometry pipeline
│   ├── Builder/                      # Fluent API for PDF creation
│   ├── Editing/                      # Edit/mutate loaded documents (pages, merge, split)
│   ├── Optimization/                 # Optimize/compress loaded documents
│   ├── Conformance/                  # Read-only preflight (PDF/A, PDF/X-4, PDF/UA-1) + rule engine
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
├── FontParser/                       # TrueType/OpenType parsing
├── Logging/                          # Logging infrastructure
└── Docs/                             # Documentation
```

## Building from source

```bash
git clone https://github.com/lxman/PdfLibrary.git
cd PdfLibrary

dotnet build PdfLibrary.slnx
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```

Everything is in-tree — no git submodules, no native toolchains.

## Documentation

- [Complete Guide](Docs/Guide.md) — loading, reading, rendering, creating, editing, and optimizing PDFs
- [Architecture](Docs/Architecture.md) — technical architecture overview

## Known limitations

Colour rendering: a small set of edge-case gaps is tracked in
[`Docs/colour/rendering-conformance.md`](Docs/colour/rendering-conformance.md) (entries G-8 … G-14),
each pinned by a baseline test that a future fix must deliberately flip. The notable ones:
`/None` shadings used as fill *patterns* still paint; `/All` images and stencil masks do not
receive spot planes on the CMYK soft-proof path; text rendering mode 4 with a `/None` fill drops
the add-to-clip half; a bare `/Pattern cs` (no `scn`) carries the previous colour over instead of
painting nothing; Indexed images over an all-reserved base still flatten. General-purpose RGB
rendering is unaffected by all of these.

## Contributing

Contributions are welcome! Feel free to open a Pull Request — and for major changes, please open an issue first so we can talk it through.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

A few style notes: follow standard C# conventions, use meaningful names, add XML documentation for public APIs, and include unit tests for new features.

## License

MIT — see the [LICENSE](LICENSE) file for details.

## Acknowledgments

### Core library
- [Serilog](https://serilog.net/) — structured logging framework
- [Unicolour](https://github.com/waacton/Unicolour) — advanced color space handling and transformations

### Test-time references
- [SkiaSharp](https://github.com/mono/SkiaSharp) — used only by the in-repo `PdfLibrary.Rendering.SkiaSharp` project as a pixel-fidelity test gate; not a runtime dependency of any published package
- [Melville.CSJ2K](https://www.nuget.org/packages/Melville.CSJ2K) — used only by `ImageLibrary/Jp2Codec.Tests` as a differential reference for in-house JPEG 2000 conformance testing; not a runtime dependency of `PdfLibrary`
