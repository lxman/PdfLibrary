# PdfLibrary

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)

A comprehensive .NET library for parsing, rendering, and creating PDF documents. Built with C# and targeting .NET 10.

## Features

### PDF Rendering
- Full PDF 1.x and 2.0 parsing support
- High-quality rendering using SkiaSharp
- Support for complex graphics operations (paths, clipping, transparency)
- Comprehensive color space support (DeviceRGB, DeviceCMYK, DeviceGray, ICCBased, Separation, Lab)
- Image handling (JPEG, PNG, CCITT, JBIG2, JPEG2000)
- Font rendering (Type1, TrueType, CID, embedded fonts)
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

## Project Structure

```
PDF/
├── PdfLibrary/                    # Core library
│   ├── Document/                  # PDF document model
│   ├── Content/                   # Content stream processing
│   ├── Rendering/                 # Rendering pipeline
│   ├── Builder/                   # Fluent API for PDF creation
│   ├── Fonts/                     # Font handling
│   ├── Images/                    # Image decompression
│   └── ColorSpaces/               # Color space support
├── PdfLibrary.Rendering.SkiaSharp/ # SkiaSharp render target
├── PdfLibrary.Tests/              # Unit tests
├── PdfLibrary.Integration/        # Integration tests
├── PdfTool/                       # Command-line tool
├── PdfLibrary.Wpf.Viewer/         # WPF PDF viewer
├── Compressors/                   # Decompression libraries
│   ├── Compressors.Lzw/           # LZW decompression
│   ├── Compressors.Ccitt/         # CCITT fax decompression
│   ├── Compressors.Jbig2/         # JBIG2 decompression
│   └── Compressors.Jpeg2000/      # JPEG2000 decompression
├── FontParser/                    # TrueType/OpenType parsing
├── Logging/                       # Logging infrastructure
└── Docs/                          # Documentation
```

## Quick Start

### Rendering a PDF

```csharp
using PdfLibrary;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.SkiaSharp;

// Load a PDF document
var document = PdfDocument.Load("document.pdf");

// Create a SkiaSharp render target
using var target = new SkiaSharpRenderTarget(width: 612, height: 792);

// Render page 1
var renderer = new PdfRenderer(document, target);
renderer.RenderPage(0);

// Save as PNG
target.SavePng("output.png");
```

### Creating a PDF

```csharp
using PdfLibrary.Builder;

PdfDocumentBuilder.Create()
    .WithMetadata(meta => meta
        .Title("My Document")
        .Author("John Doe"))
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700)
            .WithFont("Helvetica-Bold")
            .WithSize(24)
            .WithColor(PdfColor.Blue)
        .AddRectangle(100, 650, 200, 30)
            .Fill(PdfColor.LightGray)
            .Stroke(PdfColor.Black))
    .AddPage(page => page
        .AddText("Page 2", 100, 700))
    .AddBookmark("Page 1", 0)
    .AddBookmark("Page 2", 1)
    .Save("output.pdf");
```

### Creating a Form

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Registration Form", 100, 750)
            .WithSize(18)
            .Bold()
        .AddText("Name:", 100, 700)
        .AddTextField("name", 170, 695, 200, 25)
            .Required()
        .AddText("Email:", 100, 660)
        .AddTextField("email", 170, 655, 200, 25)
        .AddText("I agree to terms:", 100, 620)
        .AddCheckbox("agree", 220, 618, 18, 18))
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("form.pdf");
```

## Requirements

- .NET 10.0 or later
- SkiaSharp (for rendering)
- SixLabors.ImageSharp (for image processing)

## Building

```bash
# Clone the repository
git clone https://github.com/yourusername/PDF.git
cd PDF

# Build the solution
dotnet build PdfProcessor.slnx

# Run tests
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```

## Documentation

- [Fluent API Reference](Docs/FluentApi.md) - Complete guide to the PDF creation API
- [Getting Started](Docs/GettingStarted.md) - Step-by-step introduction
- [Architecture](Docs/Architecture.md) - Technical architecture overview

## Command-Line Tool

PdfTool provides command-line access to rendering functionality:

```bash
# Render a PDF page to PNG
PdfTool render "document.pdf" --pages 1 --output "page1.png"

# Render with custom scale
PdfTool render "document.pdf" --pages 1 --scale 2.0 --output "page1@2x.png"

# Render multiple pages
PdfTool render "document.pdf" --pages 1-5 --output "pages/"
```

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

- [SkiaSharp](https://github.com/mono/SkiaSharp) - 2D graphics library
- [ImageSharp](https://github.com/SixLabors/ImageSharp) - Image processing library
- [Serilog](https://serilog.net/) - Logging framework
