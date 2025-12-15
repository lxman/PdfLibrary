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
- Image handling with custom high-performance decompressors:
  - JPEG (via JpegLibrary fork with enhanced compatibility)
  - JPEG2000 (JP2/J2K via custom CSJ2K-based decoder)
  - CCITT Group 3 and Group 4 fax compression
  - JBIG2 monochrome compression
  - LZW compression
  - FlateDecode (zlib/PNG)
  - RunLength, ASCII85, ASCIIHex encoding
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
├── PdfLibrary/                       # Core library
│   ├── Document/                     # PDF document model
│   ├── Content/                      # Content stream processing
│   ├── Rendering/                    # Rendering pipeline
│   ├── Builder/                      # Fluent API for PDF creation
│   ├── Fonts/                        # Font handling
│   ├── Images/                       # Image decompression
│   ├── ColorSpaces/                  # Color space support
│   ├── Parsing/                      # PDF parsing (lexer, parser)
│   └── Security/                     # Encryption/decryption
├── PdfLibrary.Rendering.SkiaSharp/   # SkiaSharp render target
├── PdfLibrary.Tests/                 # Unit tests
├── PdfLibrary.Integration/           # Integration tests
├── PdfLibrary.Wpf.Viewer/            # WPF PDF viewer application
├── PdfLibrary.Utilities/             # Utility applications
│   └── ImageUtility/                 # Image format viewer with codec system
├── Compressors/                      # Custom image decompression libraries
│   ├── Compressors.Lzw/              # LZW decompression
│   │   └── Compressors.Lzw.Tests/
│   ├── Compressors.Ccitt/            # CCITT fax (Group 3/4)
│   │   └── Compressors.Ccitt.Tests/
│   ├── Compressors.Jbig2/            # JBIG2 decompression
│   │   └── Compressors.Jbig2.Tests/
│   ├── Compressors.Jpeg/             # JPEG (DCT) decompression
│   │   └── Compressors.Jpeg.Tests/
│   └── Compressors.Jpeg2000/         # JPEG2000 (legacy)
│       └── Compressors.Jpeg2000.Tests/
├── FontParser/                       # TrueType/OpenType parsing
├── Logging/                          # Logging infrastructure
└── Docs/                             # Documentation
```

## Quick Start

### Rendering a PDF

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Rendering.SkiaSharp;

// Load a PDF document
using var stream = File.OpenRead("document.pdf");
var document = PdfDocument.Load(stream);

// Get the page to render
var page = document.GetPage(0);  // 0-based index

// Render to file using the fluent API
page.Render(document)
    .WithScale(1.0)  // 1.0 = 72 DPI
    .ToFile("output.png");

// Or render to SKImage for further processing
using var image = page.Render(document)
    .WithDpi(144)  // 2x resolution
    .ToImage();
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

### Advanced Rendering Options

```csharp
// Render with custom background color
using var image = page.Render(document)
    .WithDpi(300)  // High resolution for printing
    .WithBackgroundColor(new SKColor(255, 250, 240))  // Antique white
    .ToImage();

// Render specific region of page
using var cropImage = page.Render(document)
    .WithScale(2.0)
    .WithCropBox(100, 100, 400, 600)  // x, y, width, height
    .ToImage();
```

### Text Extraction

```csharp
// Extract all text from a page
var page = document.GetPage(0);
var textContent = page.ExtractText(document);

// Extract text with positioning information
var textBlocks = page.ExtractTextBlocks(document);
foreach (var block in textBlocks)
{
    Console.WriteLine($"Text: {block.Text}");
    Console.WriteLine($"Position: ({block.X}, {block.Y})");
    Console.WriteLine($"Font: {block.FontName}, Size: {block.FontSize}");
}
```

## Image Decompression Architecture

PdfLibrary uses custom-built, high-performance decompression libraries for all PDF image formats. These are **pure C# implementations** with no external dependencies:

### Custom Decompressors
- **Compressors.Jpeg** - DCTDecode filter with full baseline and progressive JPEG support
- **Compressors.Jpeg2000** - JPXDecode filter based on CSJ2K for JP2 and J2K codestreams
- **Compressors.Ccitt** - CCITTFaxDecode filter supporting Group 3 (1D, 2D) and Group 4
- **Compressors.Jbig2** - JBIG2Decode filter for monochrome document compression
- **Compressors.Lzw** - LZWDecode filter with support for Early Change optimization

### Integration
The core library uses **JpegLibrary** (a high-performance fork) for JPEG images embedded in PDFs. The custom Compressors.Jpeg library is available for standalone image processing and is used in utility applications.

## Requirements

- .NET 10.0 or later
- SkiaSharp (for rendering - separate PdfLibrary.Rendering.SkiaSharp package)

### Core Dependencies
- **Serilog** - Structured logging
- **Unicolour** - Advanced color space transformations
- **JpegLibrary** - High-performance JPEG decompression for PDF images
- **Custom Image Decompression Libraries**:
  - Compressors.Lzw - LZW decompression
  - Compressors.Ccitt - CCITT Group 3/4 fax
  - Compressors.Jbig2 - JBIG2 monochrome compression
  - Compressors.Jpeg2000 - JPEG2000 (JP2/J2K) support

**Note**: ImageSharp is NOT a dependency of the core library. It is only used in utility applications for general image format support (PNG, BMP, GIF, TIFF, WebP, etc.).

## Building

```bash
# Clone the repository with submodules
git clone --recurse-submodules https://github.com/lxman/PDF.git
cd PDF

# Or if already cloned, initialize submodules
git submodule update --init --recursive

# Build the solution
dotnet build PdfProcessor.slnx

# Run tests
dotnet test PdfLibrary.Tests/PdfLibrary.Tests.csproj
```

**Note**: The repository uses git submodules for JpegLibrary. Make sure to use `--recurse-submodules` when cloning or run `git submodule update --init --recursive` after cloning.

## Documentation

- [Fluent API Reference](Docs/FluentApi.md) - Complete guide to the PDF creation API
- [Getting Started](Docs/GettingStarted.md) - Step-by-step introduction
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
- [SkiaSharp](https://github.com/mono/SkiaSharp) - 2D graphics library for PDF rendering
- [Serilog](https://serilog.net/) - Structured logging framework
- [Unicolour](https://github.com/waacton/Unicolour) - Advanced color space handling and transformations
- [JpegLibrary](https://github.com/yigolden/JpegLibrary) - High-performance JPEG decoder

### Utilities
- [ImageSharp](https://github.com/SixLabors/ImageSharp) - Used in ImageUtility viewer for general image format support (not a dependency of PdfLibrary itself)
