# PdfLibrary Architecture

This document describes the internal architecture of PdfLibrary, providing an overview of the major components and how they interact.

## High-Level Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Application Layer                         │
├─────────────────────────────────────────────────────────────────┤
│  PdfDocument.Load()           │    PdfDocumentBuilder.Create()  │
│  (Reading/Rendering)          │    (PDF Creation)                │
├───────────────────────────────┴──────────────────────────────────┤
│                         Core Library                             │
├──────────────┬──────────────┬──────────────┬────────────────────┤
│   Parsing    │   Document   │   Content    │     Rendering      │
│              │    Model     │  Processing  │                    │
├──────────────┼──────────────┼──────────────┼────────────────────┤
│   Filters    │    Fonts     │   Security   │     Functions      │
├──────────────┴──────────────┴──────────────┴────────────────────┤
│                      External Dependencies                       │
│  SkiaSharp  │  ImageSharp  │  Compressors (LZW, CCITT, etc.)   │
└─────────────────────────────────────────────────────────────────┘
```

## Project Structure

### Core Library (`PdfLibrary/`)

```
PdfLibrary/
├── Core/                  # PDF object primitives
│   └── Primitives/        # PdfInteger, PdfString, PdfArray, etc.
├── Parsing/               # PDF file parsing
├── Structure/             # Document structure (xref, trailer)
├── Document/              # High-level document model
├── Content/               # Content stream processing
│   └── Operators/         # PDF operator implementations
├── Rendering/             # Rendering pipeline
├── Builder/               # Fluent API for PDF creation
├── Fonts/                 # Font handling
│   └── Embedded/          # Embedded font extraction
├── Filters/               # Stream decompression
├── Functions/             # PDF function evaluation
└── Security/              # Encryption/decryption
```

---

## Component Details

### 1. Core (`Core/`)

The foundation of PDF object representation.

#### Primitives (`Core/Primitives/`)

All PDF objects inherit from `PdfObject`:

| Class | Description |
|-------|-------------|
| `PdfNull` | Null value |
| `PdfBoolean` | True/false |
| `PdfInteger` | Integer numbers |
| `PdfReal` | Floating-point numbers |
| `PdfString` | Byte strings (literal or hex) |
| `PdfName` | Name objects (e.g., `/Type`) |
| `PdfArray` | Ordered collections |
| `PdfDictionary` | Key-value mappings |
| `PdfStream` | Dictionary with binary data |
| `PdfIndirectReference` | References to objects (e.g., `5 0 R`) |

### 2. Parsing (`Parsing/`)

Converts PDF bytes into object graph.

| Component | Responsibility |
|-----------|----------------|
| `PdfLexer` | Tokenizes PDF syntax |
| `PdfParser` | Builds object tree from tokens |
| `PdfXrefParser` | Parses cross-reference tables |
| `PdfTrailerParser` | Parses document trailer |

**Parsing Flow:**
```
Raw Bytes → PdfLexer (tokens) → PdfParser (objects) → PdfDocument
```

### 3. Structure (`Structure/`)

Document-level structures.

| Component | Responsibility |
|-----------|----------------|
| `PdfDocument` | Root document container |
| `PdfXrefTable` | Object location index |
| `PdfXrefEntry` | Individual object entry |
| `PdfTrailer` | Document trailer dictionary |

### 4. Document (`Document/`)

High-level document model.

| Component | Responsibility |
|-----------|----------------|
| `PdfCatalog` | Document catalog (/Catalog) |
| `PdfPageTree` | Page tree navigation |
| `PdfPage` | Individual page |
| `PdfResources` | Fonts, images, patterns, etc. |
| `PdfImage` | Image XObject handling |
| `OptionalContentManager` | Layer (OCG) management |

### 5. Content (`Content/`)

Content stream processing.

#### PdfContentProcessor

Base class for processing PDF page content. Maintains:
- Graphics state stack
- Current transformation matrix (CTM)
- Text state (font, size, matrix)
- Color state

```csharp
public abstract class PdfContentProcessor
{
    protected Stack<PdfGraphicsState> _stateStack;
    protected PdfGraphicsState _state;

    // Override these in derived classes
    protected virtual void OnMoveTo(double x, double y) { }
    protected virtual void OnLineTo(double x, double y) { }
    protected virtual void OnShowText(string text, PdfFont font) { }
    // ... etc
}
```

#### PdfGraphicsState

Current graphics state:

```csharp
public class PdfGraphicsState
{
    // Transformation
    public Matrix CTM { get; set; }

    // Path state
    public double LineWidth { get; set; }
    public PdfLineCap LineCap { get; set; }
    public PdfLineJoin LineJoin { get; set; }

    // Color state
    public string FillColorSpace { get; set; }
    public double[] FillColor { get; set; }
    public string StrokeColorSpace { get; set; }
    public double[] StrokeColor { get; set; }

    // Resolved colors (after color space conversion)
    public string ResolvedFillColorSpace { get; set; }
    public double[] ResolvedFillColor { get; set; }

    // Text state
    public PdfFont Font { get; set; }
    public double FontSize { get; set; }
    public Matrix TextMatrix { get; set; }
    public double CharacterSpacing { get; set; }
    public double WordSpacing { get; set; }
    // ... etc
}
```

#### Operators (`Content/Operators/`)

PDF operator implementations organized by category:

| File | Operators |
|------|-----------|
| `GraphicsStateOperators.cs` | q, Q, cm, w, J, j, M, d, ri, i, gs |
| `PathAndXObjectOperators.cs` | m, l, c, v, y, h, re, S, s, f, F, f*, B, b, n, W, W*, Do |
| `TextOperators.cs` | BT, ET, Tc, Tw, Tz, TL, Tf, Tr, Ts, Td, TD, Tm, T*, Tj, TJ |
| `ColorOperators.cs` | CS, cs, SC, SCN, sc, scn, G, g, RG, rg, K, k |
| `InlineImageOperator.cs` | BI, ID, EI |

### 6. Rendering (`Rendering/`)

Converts content to visual output.

#### IRenderTarget

Interface for render backends:

```csharp
public interface IRenderTarget
{
    // Path operations
    void MoveTo(double x, double y);
    void LineTo(double x, double y);
    void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3);
    void ClosePath();
    void Fill(PdfColor color);
    void Stroke(PdfColor color, double width);

    // Text operations
    void DrawText(string text, PdfFont font, double size, PdfColor color);

    // Image operations
    void DrawImage(byte[] data, double x, double y, double width, double height);

    // State operations
    void SaveState();
    void RestoreState();
    void Transform(Matrix matrix);
    void SetClip();
}
```

#### PdfRenderer

Extends `PdfContentProcessor` to render pages:

```csharp
public class PdfRenderer : PdfContentProcessor
{
    private readonly IRenderTarget _target;
    private readonly PdfResources _resources;

    protected override void OnFill()
    {
        _target.Fill(ResolveColor(_state.FillColorSpace, _state.FillColor));
    }

    protected override void OnShowText(string text)
    {
        var font = _resources.GetFont(_state.Font);
        _target.DrawText(text, font, EffectiveFontSize, GetTextColor());
    }
}
```

#### ColorSpaceResolver

Converts PDF color spaces to device colors:

- DeviceGray, DeviceRGB, DeviceCMYK → Direct mapping
- ICCBased → Profile-based conversion
- Separation → Tint transform function
- Indexed → Lookup table
- CalGray, CalRGB, Lab → Calibrated conversion

### 7. Builder (`Builder/`)

Fluent API for PDF creation.

```
PdfDocumentBuilder
    ├── PdfPageBuilder
    │   ├── PdfTextBuilder
    │   ├── PdfPathBuilder
    │   ├── PdfImageBuilder
    │   └── Form Field Builders
    ├── PdfMetadataBuilder
    ├── PdfAcroFormBuilder
    ├── PdfBookmarkBuilder
    ├── PdfPageLabelBuilder
    ├── PdfLayerBuilder
    └── PdfEncryptionSettings
```

#### PdfDocumentWriter

Serializes builder state to PDF bytes:

```csharp
internal class PdfDocumentWriter
{
    public void Write(PdfDocumentBuilder builder, Stream output)
    {
        WriteHeader();
        WritePages();
        WriteResources();
        WriteAnnotations();
        WriteAcroForm();
        WriteBookmarks();
        WritePageLabels();
        WriteLayers();
        WriteXref();
        WriteTrailer();
    }
}
```

### 8. Fonts (`Fonts/`)

Font handling and text rendering.

| Component | Responsibility |
|-----------|----------------|
| `PdfFont` | Base font class |
| `Type1Font` | PostScript Type 1 fonts |
| `TrueTypeFont` | TrueType/OpenType fonts |
| `Type0Font` | CID-keyed fonts (CJK) |
| `Type3Font` | User-defined glyph fonts |
| `PdfFontEncoding` | Character encoding |
| `ToUnicodeCMap` | Unicode mapping |
| `AdobeGlyphList` | Glyph name to Unicode |

#### Embedded Fonts (`Fonts/Embedded/`)

| Component | Responsibility |
|-----------|----------------|
| `EmbeddedFontExtractor` | Extract font programs |
| `EmbeddedFontMetrics` | Parse TrueType metrics |
| `TrueTypeParser` | Parse TrueType tables |
| `GlyphExtractor` | Extract glyph outlines |
| `GlyphOutline` | Glyph path data |

### 9. Filters (`Filters/`)

Stream decompression.

| Filter | Description |
|--------|-------------|
| `FlateDecodeFilter` | zlib/deflate (most common) |
| `LzwDecodeFilter` | LZW compression |
| `DctDecodeFilter` | JPEG |
| `JpxDecodeFilter` | JPEG2000 |
| `Jbig2DecodeFilter` | JBIG2 (scanned documents) |
| `CcittFaxDecodeFilter` | Fax Group 3/4 |
| `Ascii85DecodeFilter` | ASCII85 encoding |
| `AsciiHexDecodeFilter` | Hexadecimal encoding |
| `RunLengthDecodeFilter` | Run-length encoding |

Filter chains are processed in order:
```
/Filter [/ASCII85Decode /FlateDecode]
→ ASCII85 decode → Flate decode → Raw data
```

### 10. Functions (`Functions/`)

PDF function evaluation for color transforms and shading.

| Function Type | Description |
|---------------|-------------|
| `SampledFunction` | Type 0 - Interpolated samples |
| `ExponentialFunction` | Type 2 - y = C0 + x^N × (C1 - C0) |
| `StitchingFunction` | Type 3 - Combines sub-functions |
| `PostScriptFunction` | Type 4 - PostScript calculator |

### 11. Security (`Security/`)

PDF encryption and decryption.

| Component | Responsibility |
|-----------|----------------|
| `PdfDecryptor` | Decrypts protected PDFs |
| `RC4` | RC4 stream cipher |
| `PdfPermissions` | Permission flags |

Encryption methods:
- RC4 40-bit (PDF 1.1)
- RC4 128-bit (PDF 1.4)
- AES-128 (PDF 1.5)
- AES-256 (PDF 1.7 Extension Level 3)

---

## Data Flow

### Reading a PDF

```
1. File/Stream → PdfLexer → Tokens
2. Tokens → PdfParser → PdfObject tree
3. PdfXrefParser → Object locations
4. PdfTrailerParser → Document metadata
5. Build PdfDocument with resolved references
6. Access pages via PdfPageTree
7. Process content streams with PdfContentProcessor
8. Render via IRenderTarget implementation
```

### Creating a PDF

```
1. PdfDocumentBuilder.Create()
2. Configure with fluent methods:
   - AddPage() → PdfPageBuilder
   - WithMetadata() → PdfMetadataBuilder
   - AddBookmark() → PdfBookmarkBuilder
   - etc.
3. Save() → PdfDocumentWriter
4. Writer serializes:
   - PDF header
   - Page objects
   - Content streams
   - Resources (fonts, images)
   - Cross-reference table
   - Trailer
```

---

## Key Design Patterns

### 1. Fluent Builder

All builder classes return `this` for chaining:

```csharp
PdfDocumentBuilder.Create()
    .AddPage(p => p.AddText("Hello", 100, 700).WithFont("Helvetica"))
    .Save("output.pdf");
```

### 2. Visitor Pattern

Content processing uses visitor-like callbacks:

```csharp
class MyProcessor : PdfContentProcessor
{
    protected override void OnMoveTo(double x, double y) { /* handle */ }
    protected override void OnShowText(string text) { /* handle */ }
}
```

### 3. Strategy Pattern

Render targets are interchangeable:

```csharp
IRenderTarget target = new SkiaSharpRenderTarget(...);
// or
IRenderTarget target = new SvgRenderTarget(...);

var renderer = new PdfRenderer(document, target);
```

### 4. Factory Pattern

Filters are created by factory:

```csharp
var filter = StreamFilterFactory.Create("/FlateDecode", parameters);
byte[] decoded = filter.Decode(encoded);
```

---

## Threading Considerations

- `PdfDocument` instances are **not thread-safe**
- Create separate `PdfDocument` instances per thread
- `PdfDocumentBuilder` is **not thread-safe** during construction
- Render targets should be used from a single thread

---

## Memory Management

- Large streams are processed incrementally when possible
- Image data is decoded on-demand
- Font metrics are cached per document
- Use `using` statements for render targets

---

## Extension Points

### Custom Render Target

Implement `IRenderTarget` for new output formats:

```csharp
public class SvgRenderTarget : IRenderTarget
{
    private StringBuilder _svg = new();

    public void MoveTo(double x, double y)
    {
        _svg.Append($"M {x} {y} ");
    }

    // ... implement all interface methods
}
```

### Custom Content Processor

Extend `PdfContentProcessor` for custom processing:

```csharp
public class TextExtractorProcessor : PdfContentProcessor
{
    public List<TextSpan> Spans { get; } = new();

    protected override void OnShowText(string text)
    {
        Spans.Add(new TextSpan(text, _state.TextMatrix));
    }
}
```

---

## External Dependencies

| Package | Usage |
|---------|-------|
| SkiaSharp | High-quality 2D graphics rendering |
| SixLabors.ImageSharp | Image format handling |
| Compressors.Lzw | LZW decompression |
| Compressors.Ccitt | CCITT fax decompression |
| Compressors.Jbig2 | JBIG2 decompression |
| Compressors.Jpeg2000 | JPEG2000 decompression |
| Serilog | Logging infrastructure |
