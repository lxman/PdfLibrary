# PdfLibrary Architecture

This document describes the internal architecture of PdfLibrary, providing an overview of the major components and how they interact.

## High-Level Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                        Application Layer                         │
├─────────────────────────────────────────────────────────────────┤
│  PdfDocument.Load()           │    PdfDocumentBuilder.Create()  │
│  page.RenderToDrawing(scale)  │    (PDF Creation)               │
│  page.GetGeometry(scale)      │    (Editing / Optimization)     │
├───────────────────────────────┴──────────────────────────────────┤
│                         Core Library  (SkiaSharp-free)           │
├──────────────┬──────────────┬──────────────┬────────────────────┤
│   Parsing    │   Document   │   Content    │  Rendering SPI     │
│              │    Model     │  Processing  │  (IRenderTarget)   │
├──────────────┼──────────────┼──────────────┼────────────────────┤
│   Filters    │    Fonts     │   Security   │     Functions      │
│              │  (std-14     │              │                    │
│              │  substitute) │              │                    │
├──────────────┴──────────────┴──────────────┴────────────────────┤
│                 External Dependencies (core)                     │
│  Unicolour  │  In-tree codecs (JpegCodec, LzwCodec,            │
│             │  CcittCodec, Jbig2Decoder, Jp2Codec)             │
├─────────────────────────────────────────────────────────────────┤
│              Render Targets (separate projects)                  │
│  Lxman.PdfLibrary.Rendering.Wpf  ← published (Windows-only)   │
│  PdfLibrary.Rendering.Svg        ← in-repo reference impl      │
│  PdfLibrary.Rendering.SkiaSharp  ← in-repo test gate only      │
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
├── Editing/               # Mutate loaded documents (pages, merge, split)
├── Optimization/          # Optimize/compress loaded documents
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

Converts content to visual output via a geometry-only SPI.

#### IRenderTarget (geometry-only SPI)

`PdfLibrary.Rendering.IRenderTarget` is the interface all render backends implement. The core resolves all fonts, decodes all images, and bakes the CTM into path coordinates before any target method is called. A target receives only geometry calls — never font handles, glyph IDs, or compressed image data.

Key design points:
- **Paths are CTM-pre-baked.** `FillPath` / `StrokePath` / etc. receive coordinates already in PDF user space with the CTM applied. Targets apply only the page initial transform (Y-flip, scale, crop offset, rotation) to convert to device space.
- **Scalar measures are NOT pre-baked.** `LineWidth`, dash pattern/phase are in user space; targets must multiply by the CTM's linear scale factor (`sqrt(|M11·M22 − M12·M21|)`) before stroking.
- **Text is geometry.** `CoreTextRenderer` resolves glyph outlines from the font subsystem and emits them as `FillPath` calls. There is no `DrawText` method on `IRenderTarget`.
- **Images** are drawn into a 1×1 unit square at the origin; `state.Ctm` determines position and size.
- Two members (`PaintShading`, `FillPathWithShadingPattern`) have default no-op implementations so targets that don't support shadings need not implement them.

See [`Docs/RendererSpi.md`](RendererSpi.md) for the full coordinate contract and a worked SVG implementation example.

#### PdfRenderer

Internal class that extends `PdfContentProcessor`. Applications drive it through the public extension:

```csharp
// Call from any IRenderTarget consumer
page.Render(myTarget, pageNumber: 1, scale: 1.5);

// Or use the bundled WPF extension (from Lxman.PdfLibrary.Rendering.Wpf)
DrawingGroup drawing = page.RenderToDrawing(scale: 1.5);
```

#### Render Target Implementations

| Project | Status | Description |
|---------|--------|-------------|
| `PdfLibrary.Rendering.Wpf` | **published** | WPF `DrawingGroup` target; `page.RenderToDrawing(scale)`. Windows-only, STA thread required. |
| `PdfLibrary.Rendering.Svg` | in-repo | SVG file target; `page.RenderToSvg()`. Reference implementation for custom targets. |
| `PdfLibrary.Rendering.SkiaSharp` | in-repo (test-only) | SkiaSharp 4.x raster target; pixel-fidelity gate for regression tests. Not published. |

#### Core text pipeline

`CoreTextRenderer` resolves embedded and system-substitute font outlines to `IPathBuilder` geometry without any SkiaSharp dependency:

| Component | Responsibility |
|-----------|----------------|
| `SubstituteFontResolver` | Classifies fonts, locates system substitutes, caches font data |
| `SystemFontLocator` | Scans platform font directories; maps std-14 base names to files |
| `GlyphOutlineToPath` | Converts TrueType (quadratic) and CFF (cubic) glyph outlines to `IPathBuilder` |
| `GlyphPlacement` | Applies glyph-space → user-space matrix with Y-flip compensation |
| `GlyphPathService` | Caches positioned glyph paths per font instance + glyph ID |
| `CoreTextRenderer` | Drives the pipeline; emits `FillPath` calls for each glyph |

#### Blend Modes and Transparency Groups

PDF supports 16 blend modes for compositing operations. When a non-Normal blend mode is encountered, the renderer creates an **isolated transparency group** per PDF specification (ISO 32000, Section 11.4.5):

```csharp
// Isolated transparency group workflow:
1. Create offscreen surface with transparent backdrop
2. Draw existing canvas content into the group
3. Draw new content with blend mode into the group
4. Composite the group result back to main canvas with Normal blend mode
```

**Critical Implementation Detail**: Isolated groups must use transparent backdrop, NOT white or opaque backdrop. Blend modes operate against transparency first, then the group result composites over the page background.

Supported blend modes:
- Normal (SrcOver)
- Multiply, Screen, Overlay
- Darken, Lighten
- ColorDodge, ColorBurn
- HardLight, SoftLight
- Difference, Exclusion
- Hue, Saturation, Color, Luminosity

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
| `EmbeddedFontMetrics` | Parse font metrics/glyphs (via `FontParser.SfntFont`) |
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

### 12. Editing (`Editing/`)

Mutation API over a **loaded** document (distinct from the `Builder/` creation API). `PdfDocument.Edit()` returns a `PdfDocumentEditor` facade.

| Component | Responsibility |
|-----------|----------------|
| `PdfDocumentEditor` | Public facade: `Pages`, `Append`/`Extract`/`Merge`, `Save`, `Open`/`CreateBlank`. `IDisposable`. |
| `PdfPageCollection` | `edit.Pages` — live page view + mutators (rotate, move, remove, insert, import, duplicate, append). |
| `PageTreeNormalizer` | Flattens the page tree to one level and materializes inheritable page attributes on `Edit()`. |
| `ObjectGraphCloner` | Deep-copies a page's reachable object subgraph across documents (the merge/import engine); dedupes shared objects and handles cycles. |
| `DestinationRepairer` | On delete, strips bookmarks/named-destinations/link-annotations that targeted the removed page. |
| `AcroFormMerger` | Registers imported pages' form fields in the target's AcroForm, qualifying name collisions. |
| `PdfSaveOptions` | `RemoveOrphans` (GC) + `UseObjectStreams`. |
| `PdfDocument.Mutation.cs` | Foundation on `PdfDocument`: object-number allocation, register/replace/remove, `CreateEmpty`, `Edit()`. |

`Save` reuses the same serializers as optimization (`PdfDocumentSerializer` / `ObjectStreamWriter`) with the `ObjectGraphWalker` reachability GC — so deleting a page is just unlinking it; orphaned objects disappear at save. Editing is currently an API-only feature (no WPF viewer UI yet).

### 13. Optimization (`Optimization/`)

Shrinks a **loaded** document and writes it back. `PdfOptimizer.Optimize(document, output, options)` runs transforms over the in-memory object graph, then serializes.

| Component | Responsibility |
|-----------|----------------|
| `PdfOptimizer` | Orchestrates the passes (stream compression, image recompress, font subset, GC) and writes via the serializer. |
| `PdfOptimizationOptions` | Which passes run + tuning (image quality, downsample cap). |
| `ObjectGraphWalker` | Computes the live object set (reachable from catalog/info) for garbage collection. |
| `PdfDocumentSerializer` | Classic full-rewrite serializer (header/body/xref/trailer); original object numbers preserved. |
| `ObjectStreamWriter` | Object-stream + cross-reference-stream writer (PDF 1.5+) for smaller output. |
| `ImageRecompressor` | Opt-in lossy image re-encode (Flate→JPEG) with optional downsampling. |
| `FontSubsetter` | Opt-in subsetting of embedded TrueType (`/FontFile2`) and CFF (`/FontFile3`) programs to used glyphs. |

Lossless by default (`CompressStreams`/`RemoveUnusedObjects`/`UseObjectStreams`); `RecompressImages` and `SubsetFonts` are opt-in. Encrypted input is decrypted and written unencrypted. The WPF viewer surfaces this through an **Optimize…** dialog.

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

### Editing a PDF

```
1. PdfDocument.Load() → read the document
2. doc.Edit():
   - MaterializeAllObjects (whole graph into memory)
   - decrypt in place if encrypted (output is unencrypted)
   - PageTreeNormalizer flattens the tree + materializes inherited attrs
3. Mutate via editor.Pages (rotate/move/remove/insert/import) or Append/Extract/Merge
   - cross-document copies go through ObjectGraphCloner
   - delete runs DestinationRepairer; import runs AcroFormMerger
4. editor.Save():
   - ObjectGraphWalker.CollectReachable (GC) when RemoveOrphans
   - PdfDocumentSerializer (classic) or ObjectStreamWriter (packed)
```

### Optimizing a PDF

```
1. PdfDocument.Load() → read the document
2. PdfOptimizer.Optimize(doc, output, options):
   - decrypt in place if encrypted
   - CompressUncompressedStreams (Flate)
   - optionally RecompressImages / SubsetFonts
   - ObjectGraphWalker.CollectReachable (GC) when RemoveUnusedObjects
3. Serialize via ObjectStreamWriter (packed) or PdfDocumentSerializer (classic)
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

Render targets are interchangeable. Applications pass their chosen target to `PdfPage.Render()`:

```csharp
IRenderTarget target = new SkiaSharpRenderTarget(width, height, document);
// or a custom implementation
IRenderTarget target = new SvgRenderTarget(...);

page.Render(target, pageNumber, scale);
```

### 4. Factory Pattern

Filters are created by factory:

```csharp
var filter = StreamFilterFactory.Create("/FlateDecode", parameters);
byte[] decoded = filter.Decode(encoded);
```

---

## Threading Considerations

PdfLibrary is built for **concurrent rendering using one document per thread** — the typical web-server model:

- Load a separate `PdfDocument` per thread/request. A single instance is **not** safe to share: it lazy-loads objects by mutating internal state and seeking a shared `Stream`.
- Use a separate render target per render. `SkiaSharpRenderTarget` wraps a single `SKCanvas`, which is not thread-safe.
- `PdfDocumentBuilder` is **not thread-safe** during construction.

Under that model the library is thread-safe. The process-wide state shared across renders is synchronized: the glyph-path cache (bounded `MemoryCache`, keyed per font instance), the system-font/typeface resolver, built-in ICC profiles and the Lab→sRGB transform (`volatile` double-checked locking), the codec registry (lock + snapshot-on-read), and font lookup tables (immutable). CFF/Type1 charstring decoding uses per-parse state rather than shared static stacks. Concurrent rendering of independent documents is validated by a stress harness that hash-compares concurrent output against a single-threaded baseline.

---

## Memory Management

- Large streams are processed incrementally when possible
- Image data is decoded on-demand using `ArrayPool<byte>` to minimize allocations
- `FlateDecodeFilter` and PNG/TIFF predictors use pre-allocated buffer capacities
- Font metrics and glyph paths are cached at a granular level
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

### NuGet packages

| Package | Usage |
|---------|-------|
| SkiaSharp | High-quality 2D graphics rendering (consumed by `PdfLibrary.Rendering.SkiaSharp`) |
| Wacton.Unicolour | Color space transformations (CalGray/CalRGB/Lab/ICC) |
| Serilog | Logging infrastructure |
| Melville.CSJ2K | *Test-time only* — differential reference used by `ImageLibrary/Jp2Codec.Tests` for JPEG 2000 conformance; not referenced at runtime |

### In-tree codec projects

Each codec is its own project under `ImageLibrary/`. The PDF filters in `PdfLibrary/Filters/` are thin adapters over these.

| Project | PDF filter | Notes |
|---------|------------|-------|
| `ImageLibrary/JpegCodec` | `/DCTDecode` | In-house baseline + progressive JPEG (encode + decode) |
| `ImageLibrary/Jp2Codec` | `/JPXDecode` | In-house JPEG 2000 decoder (decode only) |
| `ImageLibrary/LzwCodec` | `/LZWDecode` | Early Change parameter supported |
| `ImageLibrary/CcittCodec` | `/CCITTFaxDecode` | Group 3 1D/2D and Group 4 |
| `ImageLibrary/Jbig2Decoder` | `/JBIG2Decode` | ITU-T T.88; used directly by `PdfLibrary.Filters.Jbig2DecodeFilter` |
| `ImageLibrary/BmpCodec`, `GifCodec`, `PngCodec`, `TgaCodec`, `TiffCodec`, `PbmCodec` | — | Image container codecs (consumed by `ImageUtility`, not by `PdfLibrary`) |
