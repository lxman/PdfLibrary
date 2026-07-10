# PdfLibrary — Complete Guide

A single, end-to-end guide to using PdfLibrary: **loading**, **reading & extracting**, **rendering**, **creating**, **editing**, **optimizing**, and **preflighting** (conformance validation) PDF documents.

Every code example in this document is compiled against the library, so it is copy-paste accurate.

## Contents

- [Install & packages](#install--packages)
- [The big picture](#the-big-picture)
- [Loading a document](#loading-a-document)
- [Reading & extracting](#reading--extracting)
- [Rendering to a WPF DrawingGroup](#rendering-to-a-wpf-drawinggroup)
- [Creating a PDF](#creating-a-pdf)
  - [Document, pages, units](#document-pages-units)
  - [Text](#text)
  - [Shapes & paths](#shapes--paths)
  - [Images](#images)
  - [Form fields](#form-fields)
  - [Links, notes & highlights](#links-notes--highlights)
  - [Bookmarks](#bookmarks)
  - [Custom fonts](#custom-fonts)
  - [Metadata](#metadata)
  - [Encryption](#encryption)
  - [Saving](#saving)
- [Editing an existing PDF](#editing-an-existing-pdf)
- [Optimizing](#optimizing)
- [Preflighting (conformance)](#preflighting-conformance)
- [Error handling](#error-handling)
- [Thread safety](#thread-safety)
- [API reference](#api-reference)

---

## Install & packages

```bash
dotnet add package Lxman.PdfLibrary                  # core: load, read, create, edit, optimize
dotnet add package Lxman.PdfLibrary.Rendering.Wpf    # WPF render target (Windows-only)
```

`Lxman.PdfLibrary` is pure managed C# with no native dependencies and no SkiaSharp reference — it targets .NET 8, 9, and 10. `Lxman.PdfLibrary.Rendering.Wpf` adds a WPF `DrawingGroup`-based render target (Windows-only; requires an STA thread for rendering). Both target .NET 8, 9, and 10.

> **Note:** `Lxman.PdfLibrary.Rendering.SkiaSharp` is **not published** in 2.0. It remains in-repo as a pixel-fidelity test gate. To render without WPF, implement `IRenderTarget` from `PdfLibrary.Rendering`; the in-repo `SvgRenderTarget` and `SkiaSharpRenderTarget` are reference implementations.

## The big picture

```
            ┌─────────── PdfDocument.Load ───────────┐
            │                                         │
   read / extract / render            doc.Edit() → PdfDocumentEditor → Save
            │                                         │
   PdfDocumentBuilder.Create() …          PdfOptimizer.Optimize → smaller file
   … build a new PDF from scratch
```

- **`PdfDocument`** — load and inspect an existing PDF (`PdfLibrary.Structure`).
- **`PdfDocumentBuilder`** — create a PDF from scratch with a fluent API (`PdfLibrary.Builder`).
- **`PdfDocumentEditor`** — mutate a loaded PDF (`PdfLibrary.Editing`), obtained via `doc.Edit()`.
- **`PdfOptimizer`** — shrink a loaded PDF (`PdfLibrary.Optimization`).
- **`Preflighter`** — validate a loaded PDF against PDF/A, PDF/X-4, or PDF/UA-1 (`PdfLibrary.Conformance`); read-only.
- **`page.RenderToDrawing(scale)`** — render a page to WPF vector geometry (`PdfLibrary.Rendering.Wpf`), or implement `IRenderTarget` for any other backend.

---

## Loading a document

```csharp
using PdfLibrary.Structure;

// From a file path
using PdfDocument doc = PdfDocument.Load("input.pdf");

// With a password (encrypted documents)
using PdfDocument encrypted = PdfDocument.Load("protected.pdf", "secret");

// From a stream (in-memory or network PDF)
using var ms = new MemoryStream(pdfBytes);
using PdfDocument fromStream = PdfDocument.Load(ms);                 // owns + disposes the stream
using PdfDocument keepOpen   = PdfDocument.Load(ms, leaveOpen: true); // you keep the stream
```

`PdfDocument` is `IDisposable` — dispose it (a `using` declaration is simplest). Loading a malformed file throws `PdfParseException`; a bad password throws `PdfSecurityException` (see [Error handling](#error-handling)).

## Reading & extracting

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;   // PdfPage, PdfImage
using PdfLibrary.Content;    // TextFragment

using PdfDocument doc = PdfDocument.Load("input.pdf");

int pageCount = doc.PageCount;
PdfPage first = doc.GetPage(0)!;            // 0-based; null if out of range
foreach (PdfPage page in doc.Pages) { /* … */ }

// Page geometry
double w = first.Width;                     // points
double h = first.Height;
int rotation = first.Rotate;

// Text
string allText  = doc.ExtractAllText();     // every page, joined
string pageText = first.ExtractText();      // one page

// Text with positions
(string text, List<TextFragment> fragments) = first.ExtractTextWithFragments();
foreach (TextFragment f in fragments)
    Console.WriteLine($"'{f.Text}' @ ({f.X:F1}, {f.Y:F1})  {f.FontName} {f.FontSize:F1}pt");

// Images
List<PdfImage> images = first.GetImages();
foreach (PdfImage img in images)
    Console.WriteLine($"{img.Width}x{img.Height}, {img.BitsPerComponent}bpc {img.ColorSpace}");
byte[] decoded = images[0].GetDecodedData();
```

To read an existing document's metadata, outlines, form fields, etc., enter edit mode (`doc.Edit()`) — those facades are read/write. See [Editing](#editing-an-existing-pdf).

## Rendering to a WPF DrawingGroup

Rendering lives in the `Lxman.PdfLibrary.Rendering.Wpf` package. The entry point is `page.RenderToDrawing(double scale)`, which returns a retained WPF `DrawingGroup` (vector geometry — scales without pixelation). Must be called on an STA thread.

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Document;
using PdfLibrary.Rendering.Wpf;
using System.Windows.Media;

using PdfDocument doc = PdfDocument.Load("input.pdf");
PdfPage page = doc.GetPage(0)!;

// Render to a DrawingGroup at 1.5× scale (= 108 DPI equivalent)
DrawingGroup drawing = page.RenderToDrawing(scale: 1.5);

// Wrap for hosting in <Image Stretch="Uniform"/> without distortion
PageGeometry geo = page.GetGeometry(scale: 1.5);
DrawingImage pageImage = drawing.ToPageImage(geo.PixelWidth, geo.PixelHeight);
// myWpfImage.Source = pageImage;

// Render to a raster BitmapSource (for export/print)
using var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
    geo.PixelWidth, geo.PixelHeight, 96, 96,
    System.Windows.Media.PixelFormats.Pbgra32);
var dv = new System.Windows.Media.DrawingVisual();
using (var dc = dv.RenderOpen())
    dc.DrawDrawing(drawing);
rtb.Render(dv);
// Encode rtb to PNG etc. with a BitmapEncoder
```

`WpfPageExtensions`: `RenderToDrawing(this PdfPage, double scale = 1.0)` → `DrawingGroup`; `ToPageImage(this DrawingGroup, int pixelWidth, int pixelHeight)` → frozen `DrawingImage`.

### Forms geometry overlay

Use `PdfPage.GetGeometry` to map form fields into rendered-image coordinates for UI overlay:

```csharp
PageGeometry geo = page.GetGeometry(scale: 1.5);

using PdfDocumentEditor editor = PdfDocumentEditor.Open("form.pdf");
foreach (PdfFormField field in editor.Forms)
{
    foreach (PdfFieldWidget widget in field.Widgets)
    {
        if (widget.PageIndex != 0) continue;
        ImageRect r = geo.MapRectToImage(widget.Rect);
        // Place a WPF TextBox at Canvas.Left=r.X, Canvas.Top=r.Y,
        // Width=r.Width, Height=r.Height, FontSize=field.FontSize (scaled to zoom)
    }
}
```

`PageGeometry`: `PdfToImage` / `ImageToPdf` (`Matrix3x2`); `PixelWidth` / `PixelHeight`; `MapRectToImage(PdfRect)` → `ImageRect`.
`PdfFieldWidget`: `Rect` (PDF user space), `PageIndex`, `OnState`.
`PdfFormField`: `FontName` (`string`), `FontSize` (`double`), `Widgets` (`IReadOnlyList<PdfFieldWidget>`).

### Custom render target

To render without WPF, implement `IRenderTarget` from `PdfLibrary.Rendering`:

```csharp
using PdfLibrary.Rendering;

public sealed class MyRenderTarget : IRenderTarget
{
    // BeginPage, EndPage, Clear, CurrentPageNumber,
    // StrokePath, FillPath, FillAndStrokePath, FillPathWithTilingPattern,
    // SetClippingPath, DrawImage,
    // SaveState, RestoreState, ApplyCtm, OnGraphicsStateChanged,
    // RenderSoftMask, ClearSoftMask, GetPageDimensions
    // PaintShading and FillPathWithShadingPattern have default no-op implementations.
}

// Then render:
using var doc = PdfDocument.Load("input.pdf");
PdfPage page = doc.GetPage(0)!;
var target = new MyRenderTarget();
page.Render(target, pageNumber: 1, scale: 1.0);
```

The in-repo `SvgRenderTarget` and `SkiaSharpRenderTarget` are worked examples. See [`Docs/RendererSpi.md`](RendererSpi.md) for the full coordinate contract.

---

## Creating a PDF

`PdfDocumentBuilder.Create()` starts a fluent builder. Calls chain; `Save` / `ToByteArray` finishes.

### Document, pages, units

```csharp
using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700, "Helvetica", 24))     // text, x, y, fontName, fontSize
    .AddPage(PdfPageSize.A4, page => page
        .AddText("Second page (A4)", 100, 700, "Helvetica", 12))
    .Save("hello.pdf");
```

- `AddPage(Action<PdfPageBuilder>)` adds a US-Letter page; `AddPage(PdfSize, Action<PdfPageBuilder>)` sets the size.
- Page sizes: `PdfPageSize.Letter`, `.Legal`, `.A3`, `.A4`, `.A5`, … or `PdfSize.FromInches(w, h)` / `PdfSize.FromMillimeters(w, h)`.

**Coordinates** are in PDF points (1/72 inch) with a bottom-left origin by default. Switch units/origin per page:

```csharp
using PdfLibrary.Builder;        // PdfUnitExtensions: .Inches(), .Mm(), …

.AddPage(page =>
{
    page.WithInches().FromTopLeft();                 // now plain numbers are inches, top-left origin
    page.AddText("1 inch in, 1 inch down", 1, 1, "Helvetica", 12);

    // Or pass explicit-unit lengths (mix freely), no page-level switch needed:
    page.AddText("Explicit units", 2.0.Inches(), 25.0.Mm(), "Helvetica", 10);
})
```

### Text

Two forms of `AddText`:

```csharp
.AddPage(page =>
{
    // 1) Quick form — font + size as parameters (returns the page builder)
    page.AddText("Body text", 100, 700, "Helvetica", 12);

    // 2) Fluent form — returns a PdfTextBuilder you can style
    page.AddText("Styled title", 100, 750)
        .Font("Helvetica-Bold", 24)
        .Color(PdfColor.Blue)
        .Bold();

    page.AddText("Right aligned", 100, 680)
        .Size(10)
        .Align(PdfTextAlignment.Right)
        .MaxWidth(400);
})
```

`PdfTextBuilder`: `Font(name, size)`, `Size(size)`, `Color(color)`, `StrokeColor(color, width)`, `Bold()`, `Align(PdfTextAlignment)`, `MaxWidth(points)`, `Rotate(degrees)`, `CharacterSpacing`, `WordSpacing`, `LineSpacing`, `Superscript()`, `Subscript()`, `RenderMode(PdfTextRenderMode)`, `Done()` (returns to the page builder).

### Shapes & paths

`AddRectangle` and `AddLine` take colours as **parameters** and return the page builder. `AddCircle`, `AddEllipse`, `AddRoundedRectangle`, and `AddPath` return a fluent `PdfPathBuilder`.

```csharp
.AddPage(page =>
{
    // Rectangle — fill/stroke are parameters
    page.AddRectangle(100, 600, 200, 80, fillColor: PdfColor.LightGray,
                      strokeColor: PdfColor.Black, lineWidth: 1);

    // Line — stroke colour/width are parameters
    page.AddLine(100, 580, 300, 580, PdfColor.Red, 2);

    // Circle / ellipse / path return a fluent path builder
    page.AddCircle(200, 450, 50)
        .Fill(PdfColor.Green)
        .Stroke(PdfColor.Black, 2);

    page.AddPath()
        .MoveTo(100, 300)
        .LineTo(200, 380)
        .CurveTo(250, 330, 150, 280, 100, 320)   // cubic Bézier
        .ClosePath()
        .Fill(PdfColor.FromRgb(255, 165, 0))      // custom colour
        .Stroke(PdfColor.Black, 1)
        .Dashed(5, 3);                            // 5pt dash, 3pt gap
})
```

`PdfPathBuilder` (selected): `MoveTo`, `LineTo`, `CurveTo`, `QuadraticCurveTo`, `Arc`, `Rectangle`, `Circle`, `Ellipse`, `ClosePath`, `Fill(color)`, `Stroke(color, width)`, `LineWidth`, `LineCap(PdfLineCap)`, `LineJoin(PdfLineJoin)`, `MiterLimit`, `DashPattern(double[], phase)`, `Dashed(dash, gap)`, `Dotted`, `FillRule(PdfFillRule)`, `Done()`.

### Images

```csharp
byte[] imageBytes = File.ReadAllBytes("logo.png");

.AddPage(page =>
{
    // Returns a fluent PdfImageBuilder
    page.AddImage(imageBytes, 100, 500, 200, 120)        // bytes, x, y, width, height
        .Opacity(0.9)
        .PreserveAspectRatio();

    page.AddImageFromFile("photo.jpg", 100, 200, 300, 200)
        .Compression(PdfImageCompression.Jpeg, jpegQuality: 80);
})
```

`PdfImageBuilder`: `Opacity`, `Rotate`, `PreserveAspectRatio(bool)`, `Stretch()`, `Compression(PdfImageCompression, jpegQuality)`, `Interpolate(bool)`, `NearestNeighbor()`.

### Form fields

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page =>
    {
        page.AddText("Registration", 100, 750, "Helvetica-Bold", 18);

        page.AddText("Name:", 100, 700, "Helvetica", 12);
        page.AddTextField("name", 170, 695, 200, 22)
            .Required();

        page.AddText("Country:", 100, 660, "Helvetica", 12);
        page.AddDropdown("country", 170, 655, 200, 22)
            .AddOptions("United States", "Canada", "Mexico")
            .Select("Canada");

        page.AddText("Subscribe:", 100, 620, "Helvetica", 12);
        page.AddCheckbox("subscribe", 190, 618, 16)          // name, x, y, size
            .Checked();
    })
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("form.pdf");
```

Field builders: `AddTextField` → `PdfTextFieldBuilder` (`Value`, `WithMaxLength`, `Multiline`, `Required`, `Font`, …); `AddCheckbox` → `PdfCheckboxBuilder` (`Checked`, `WithExportValue`, …); `AddDropdown` → `PdfDropdownBuilder` (`AddOption`, `AddOptions`, `Select`, `Editable`, …); `AddRadioGroup` → `PdfRadioGroupBuilder` (`AddOption(value, rect)`, `Select`); `AddSignatureField` → `PdfSignatureFieldBuilder`.

### Links, notes & highlights

```csharp
.AddPage(page =>
{
    // Internal link to another page
    page.AddLink(new PdfRect(100, 600, 250, 620), pageIndex: 2);

    // External hyperlink
    page.AddExternalLink(new PdfRect(100, 560, 300, 580), "https://example.com");

    // Sticky note
    page.AddNote(400, 700, "Review this section.");

    // Highlight
    page.AddHighlight(new PdfRect(100, 700, 300, 714));
})
```

### Bookmarks

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Chapter 1", 100, 700, "Helvetica", 24))
    .AddPage(page => page.AddText("Chapter 2", 100, 700, "Helvetica", 24))
    .AddBookmark("Chapter 1", 0)
    .AddBookmark("Chapter 2", 1)
    .Save("bookmarks.pdf");
```

### Custom fonts

Load a TrueType/OpenType font from a path, bytes, or stream, then reference it by alias:

```csharp
byte[] fontBytes = File.ReadAllBytes("MyFont.ttf");

PdfDocumentBuilder.Create()
    .LoadFont("MyFont.ttf", "MyFont")          // from a file path
    .LoadFont(fontBytes, "MyFontFromBytes")    // from bytes (an embedded resource, a download, …)
    .AddPage(page => page
        .AddText("Custom font text", 100, 700, "MyFont", 24))
    .Save("custom-font.pdf");
```

`LoadFont` overloads: `LoadFont(string path, alias)`, `LoadFont(byte[] data, alias)`, `LoadFont(Stream stream, alias)`. Supported formats: `.ttf`, `.otf`, `.ttc`, `.woff`, `.woff2`.

### Metadata

```csharp
PdfDocumentBuilder.Create()
    .WithMetadata(meta => meta
        .SetTitle("Quarterly Report")
        .SetAuthor("Finance Team")
        .SetSubject("Q3 figures")
        .SetKeywords("finance, quarterly"))
    .AddPage(page => page.AddText("…", 100, 700, "Helvetica", 12))
    .Save("report.pdf");
```

`PdfMetadataBuilder`: `SetTitle`, `SetAuthor`, `SetSubject`, `SetKeywords`, `SetCreator`, `SetProducer`, `SetCreationDate`, `SetModificationDate`.

### Encryption

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Confidential", 100, 700, "Helvetica", 12))
    .WithEncryption(enc => enc
        .WithUserPassword("open-me")
        .WithOwnerPassword("owner")
        .WithMethod(PdfEncryptionMethod.Aes256)
        .AllowPrinting())
    .Save("encrypted.pdf");

// Shortcut: user password only
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("…", 100, 700, "Helvetica", 12))
    .WithPassword("open-me")
    .Save("protected.pdf");
```

`PdfEncryptionSettings`: `WithUserPassword`, `WithOwnerPassword`, `WithMethod(PdfEncryptionMethod)`, `WithPermissions(PdfPermissionFlags)`, and `Allow*` / `Deny*` toggles (`AllowPrinting`, `AllowCopying`, `AllowModifying`, `DenyAll`, `AllowAll`, …).

### Saving

```csharp
PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("…", 100, 700, "Helvetica", 12));

builder.Save("output.pdf");                  // to a file
using (var fs = File.Create("output.pdf"))
    builder.Save(fs);                        // to a stream
byte[] bytes = builder.ToByteArray();        // to memory
```

---

## Editing an existing PDF

`doc.Edit()` returns a `PdfDocumentEditor` — the mutation facade. `.Edit()` materializes the object graph, decrypts in place if needed, and flattens the page tree so page operations are simple. `Save` is a full rewrite.

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using PdfDocument doc = PdfDocument.Load("input.pdf");
PdfDocumentEditor edit = doc.Edit();        // you own `doc`; dispose it

edit.Pages.RemoveAt(2);                      // delete the 3rd page
edit.Pages.Rotate(0, 90);                    // absolute rotation (×90)
edit.Pages.Move(4, 0);                       // reorder

edit.Save("edited.pdf");
```

Or load + edit in one step (the editor owns the document):

```csharp
using PdfDocumentEditor edit = PdfDocumentEditor.Open("input.pdf");
// or from a stream:  PdfDocumentEditor.Open(stream)
// or a blank target: PdfDocumentEditor.CreateBlank()
```

**Pages** (`edit.Pages`, a live `IReadOnlyList<PdfPage>`): `Rotate`, `RotateBy`, `Move`, `RemoveAt`, `InsertBlank(at, w, h)`, `Import(source, sourceIndex, at)`, `Duplicate(index, at)`, `Append(source)`, `AppendRange(source, start, count)`.

**Merge / split** (return a new `PdfDocument`):

```csharp
using var a = PdfDocument.Load("part1.pdf");
using var b = PdfDocument.Load("part2.pdf");
using PdfDocument merged = PdfDocumentEditor.Merge([a, b]);
merged.Save("combined.pdf");

using PdfDocument chapter = edit.Extract(start: 4, count: 6);  // pages 5–10
```

**Stamping / watermarks** (`edit.Pages.Stamp` / `StampRange` / `StampAll`):

```csharp
edit.Pages.StampAll(s => s.Watermark("DRAFT").Diagonal().Opacity(0.5).Underlay());
```

**Annotations** (read, add, remove):

```csharp
using PdfLibrary.Builder;        // PdfRect, PdfColor
using PdfLibrary.Builder.Page;

edit.Pages.AddNote(0, 72, 720, "Review this.");
edit.Pages.AddLink(0, new PdfRect(72, 600, 250, 620), targetPageIndex: 5);
edit.Pages.AddExternalLink(0, new PdfRect(72, 560, 300, 580), "https://example.com");
edit.Pages.AddHighlight(0, new PdfRect(72, 700, 400, 715), PdfColor.Yellow);

IReadOnlyList<PdfAnnotationInfo> annots = edit.Pages.GetAnnotations(0);
edit.Pages.RemoveAnnotationAt(0, 0);
```

**Metadata** (`edit.Metadata`, with automatic XMP sync):

```csharp
edit.Metadata.Title  = "Q3 Report";
edit.Metadata.Author = "Finance Team";
edit.Metadata.ModificationDate = DateTimeOffset.UtcNow;
```

**Outlines** (`edit.Outlines`, a mutable bookmark tree):

```csharp
using PdfLibrary.Builder.Bookmark;   // PdfDestination

PdfOutlineItem ch1 = edit.Outlines.Add("Chapter 1", PdfDestination.FitPage(0));
ch1.Add("Section 1.1", PdfDestination.ToPage(1));
edit.Outlines.Insert(0, "Cover", 0);     // insert at a position
edit.Outlines.RemoveAt(2);
```

**Page labels**, **named destinations**, **viewer settings**, **form filling**:

```csharp
edit.PageLabels.Set(0, PdfPageLabelStyle.LowercaseRoman);
edit.PageLabels.Set(3, PdfPageLabelStyle.Decimal, start: 1);

edit.NamedDestinations.Set("cover", PdfDestination.FitPage(0));
foreach ((string name, PdfDestination dest) in edit.NamedDestinations.Entries())
    Console.WriteLine($"{name} → page {dest.PageIndex}");

edit.ViewerSettings.PageMode = PdfPageMode.UseOutlines;
edit.ViewerSettings.HideMenubar = true;
edit.ViewerSettings.Direction = PdfReadingDirection.LeftToRight;

if (edit.Forms["address.street"] is PdfTextField street)
    street.Value = "123 Main St";
if (edit.Forms["agree"] is PdfButtonField cb && cb.Kind == ButtonKind.Checkbox)
    cb.Check();
edit.Forms.Flatten();   // bake fields into static content
```

> The full editing surface — every member of `PdfPageCollection`, `PdfOutlineCollection`, `PdfNamedDestinations`, `PdfViewerSettings`, `PdfFormFields`, and the form-field types — is tabulated in the [API reference](#api-reference) below.

### Authoring form fields

`editor.Forms` can create, remove, rename, move, and restyle AcroForm fields on any
existing document — including one with no form at all (the /AcroForm dictionary is
bootstrapped on first use, with appearance streams generated up front; /NeedAppearances
is never relied on). Geometry is PDF user space (Y-up), the same convention as
`PdfFieldWidget.Rect`.

```csharp
using PdfDocumentEditor editor = PdfDocumentEditor.Open("plain.pdf");

PdfTextField name = editor.Forms.AddTextField(0, "name", new PdfRect(72, 700, 372, 720));
name.Value = "Jane Doe";                       // fillable immediately
name.FontName = "Cour"; name.FontSize = 12;    // standard-14 restyling

PdfButtonField subscribe = editor.Forms.AddCheckbox(0, "subscribe", new PdfRect(72, 670, 86, 684));
subscribe.Check();

PdfButtonField size = editor.Forms.AddRadioGroup("size", new[]
{
    new PdfRadioOptionPlacement(0, new PdfRect(72, 640, 86, 654), "S"),
    new PdfRadioOptionPlacement(0, new PdfRect(100, 640, 114, 654), "M"),
});
size.SelectedOption = "M";

PdfChoiceField color = editor.Forms.AddDropdown(0, "color",
    new PdfRect(72, 610, 272, 630), new[] { ("r", "Red"), ("g", "Green") });

editor.Forms.AddSignatureField(0, "sig", new PdfRect(72, 540, 272, 590)); // unsigned placeholder

name.Rename("fullName");
name.SetWidgetRect(0, new PdfRect(72, 700, 500, 724));  // move/resize + appearance regen
editor.Forms.Remove("color");                            // widgets + field tree entry + prune

editor.Save("form.pdf");
```

Notes:

- Names are root-level partial names: non-empty, no `.`, unique — violations throw
  `ArgumentException` before anything is modified.
- All authoring entry points throw `InvalidOperationException` on dynamic XFA documents
  (check `Forms.IsDynamicXfa` first), and `Remove` refuses a **signed** signature field.
- `PdfFormField.Widgets` remains a read-time snapshot: after `SetWidgetRect`, re-read the
  field from `editor.Forms` for fresh geometry.

**Save** (with options):

```csharp
using PdfLibrary.Editing;

edit.Save("out.pdf", new PdfSaveOptions
{
    RemoveOrphans    = true,   // drop unreachable objects (default)
    UseObjectStreams = true,   // smaller output (default false)
});
```

Editing decrypts an encrypted input; the saved output is unencrypted. Re-encrypting on save is not yet supported.

## Optimizing

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Optimization;

using PdfDocument doc = PdfDocument.Load("input.pdf");

// Lossless by default (Flate compression + object streams + unused-object GC)
PdfOptimizationResult result = PdfOptimizer.Optimize(doc, "optimized.pdf");
Console.WriteLine($"{result.ObjectsRemoved} objects removed, {result.OutputBytes} bytes written");

// Opt in to lossy size reductions
using var output = File.Create("smaller.pdf");
PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions
{
    RecompressImages = true,   // lossy: re-encode images as JPEG
    SubsetFonts      = true,   // discard unused glyphs from embedded fonts
});
```

`Optimize` accepts a `Stream` or a file path and returns a `PdfOptimizationResult`: `ObjectsBefore`, `ObjectsAfter`, `ObjectsRemoved`, `OutputBytes`, `StreamsCompressed`, `ImagesRecompressed`, `FontsSubsetted`. `PdfOptimizationOptions`: `CompressStreams`, `RemoveUnusedObjects`, `UseObjectStreams`, `RecompressImages`, `SubsetFonts`, `ImageJpegQuality`, `MaxImagePixelDimension` (plus `PdfOptimizationOptions.Default`).

## Preflighting (conformance)

Validate a loaded document against an ISO PDF standard without modifying it. This is a **read-only** check — it never mutates the document.

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Conformance;

using PdfDocument doc = PdfDocument.Load("input.pdf");

PreflightResult result = Preflighter.Check(doc, ConformanceProfile.PdfA2b);

if (result.Conforms)
    Console.WriteLine("Conforms (no violations among the checked rules)");
else
    foreach (Finding f in result.Errors)
        Console.WriteLine($"[{f.Severity}] {f.Clause}: {f.Message}"
            + (f.PageIndex is { } p ? $" (page {p + 1})" : ""));
```

`Preflighter.Check` has three overloads — a loaded `PdfDocument`, a `byte[]`, or a file path (the byte/path overloads also let byte-level rules run over the original source). It returns a `PreflightResult`: `Conforms` (true when no finding has `Error` severity), `Findings` (all), `Errors`, and `Warnings`. Each `Finding` carries `Severity` (`Error`/`Warning`/`Info`), `Clause` (the governing ISO clause, e.g. `"ISO 14289-1:2014, 7.1"`), `Message`, and — where applicable — `PageIndex` and `ObjectNumber`.

`ConformanceProfile` is a `[Flags]` enum, but each `Check` call targets exactly one profile:

| Profile | Standard | Coverage |
|---|---|---|
| `PdfA2b` / `PdfA2u` / `PdfA3b` | ISO 19005-2/3 (archival) | level B/U structural conformance |
| `PdfX4` | ISO 15930-7 (print) | structural core + colour / version governance |
| `PdfUA1` | ISO 14289-1 (accessibility) | tagging, structure nesting, tables, language identifiers |

**What this is and isn't.** The preflight is a *structural* validator — a deliberately partial, machine-decidable subset of each standard, tuned for **zero false positives** on conformant files (cross-checked against the veraPDF conformance corpus). A `Conforms == true` result means "no violations among the checked rules", **not** a certification: PDF/X-4 does not run the full Ghent print profile, and PDF/UA-1 cannot judge reading order or whether alternative text is *meaningful* — those remain human review. Treat it as a fast, dependency-free first pass that catches structural defects, not a substitute for a certifying validator.

## Error handling

Loading or parsing a malformed PDF throws `PdfParseException`; a wrong or missing password throws `PdfSecurityException`. Both derive from the public base `PdfException` (in the `PdfLibrary` namespace):

```csharp
using PdfLibrary;            // PdfException
using PdfLibrary.Security;   // PdfSecurityException
using PdfLibrary.Parsing;    // PdfParseException

try
{
    using PdfDocument doc = PdfDocument.Load("maybe-broken.pdf", password);
    // …
}
catch (PdfSecurityException) { /* wrong/missing password */ }
catch (PdfException) { /* malformed or otherwise invalid PDF */ }
```

## Thread safety

PdfLibrary supports **concurrent rendering using the one-document-per-thread model** — the standard pattern for ASP.NET Core and other multi-threaded servers. Each request loads its own `PdfDocument`, renders on its own render target, and disposes both. The process-wide caches shared across renders (glyph-path cache, font/typeface resolver, ICC profiles, codec registry) are synchronized. Do **not** share a single `PdfDocument` or editor across threads. Do **not** share an `IRenderTarget` implementation across threads — render targets hold per-render mutable state.

> **WPF note:** `WpfRenderTarget` and `page.RenderToDrawing` must be called on an STA thread. In ASP.NET Core, dispatch each render to a dedicated STA thread pool, or use the SVG/SkiaSharp (in-repo) targets for headless server scenarios.

---

## API reference

### Loading & reading — `PdfDocument`

| Member | Description |
|--------|-------------|
| `static PdfDocument Load(string path, string password = "")` | Load from a file. |
| `static PdfDocument Load(Stream stream, bool leaveOpen = false)` | Load from a stream. |
| `static PdfDocument Load(Stream stream, string password, bool leaveOpen = false)` | Load an encrypted stream. |
| `int PageCount` / `IEnumerable<PdfPage> Pages` | Page count / enumeration. |
| `PdfPage? GetPage(int index)` | Page by 0-based index, or `null`. |
| `List<PdfPage> GetPages()` / `List<PdfPage> GetPages(int start, int end)` | All pages / a range. |
| `PdfPage? FirstPage` / `PdfPage? LastPage` | Convenience accessors. |
| `string ExtractAllText(string separator = "\n\n")` | All text in the document. |
| `bool IsEncrypted` / `PdfPermissions Permissions` | Encryption state and permissions. |
| `PdfDocumentEditor Edit()` | Enter edit mode. |
| `void Save(string path)` / `void Save(Stream stream)` | Write the document. |

### `PdfPage`

| Member | Description |
|--------|-------------|
| `double Width` / `double Height` / `int Rotate` | Geometry (points) and rotation. |
| `PdfRectangle GetMediaBox()` / `GetCropBox()` | Page boxes. |
| `string ExtractText()` | Text on this page. |
| `(string Text, List<TextFragment> Fragments) ExtractTextWithFragments()` | Text with positions. |
| `List<PdfImage> GetImages()` / `int GetImageCount()` | Images on the page. |

### Rendering — WPF (`PdfLibrary.Rendering.Wpf`)

| Member | Description |
|--------|-------------|
| `DrawingGroup RenderToDrawing(this PdfPage, double scale = 1.0)` | Render a page to a retained WPF `DrawingGroup` (STA thread required). |
| `DrawingImage ToPageImage(this DrawingGroup, int pixelWidth, int pixelHeight)` | Wrap a `DrawingGroup` into a frozen `DrawingImage` with correct page-rect bounds. |

### Forms geometry — `PdfPage.GetGeometry` / `PageGeometry`

| Member | Description |
|--------|-------------|
| `PageGeometry GetGeometry(this PdfPage, double scale)` | Map PDF user space ↔ rendered-image pixels at the given scale. |
| `PageGeometry.PdfToImage` / `.ImageToPdf` | Forward/inverse `Matrix3x2` transforms. |
| `PageGeometry.PixelWidth` / `.PixelHeight` | Rendered image dimensions at the given scale. |
| `PageGeometry.MapRectToImage(PdfRect)` → `ImageRect` | Convert a PDF-space rect to image-pixel space (top-left origin). |
| `ImageRect.X` / `.Y` / `.Width` / `.Height` | Pixel-space rect for UI control placement. |
| `PdfFormField.Widgets` | `IReadOnlyList<PdfFieldWidget>` — widget locations for a field. |
| `PdfFieldWidget.Rect` / `.PageIndex` / `.OnState` | Widget geometry in PDF user space, page, and radio/checkbox on-state. |
| `PdfFormField.FontName` / `.FontSize` | `/DA` appearance font name and size for host-side text styling. |

### Custom render target — `IRenderTarget` (`PdfLibrary.Rendering`)

| Member | Kind | Description |
|--------|------|-------------|
| `BeginPage(pageNumber, width, height, scale, cropOffsetX, cropOffsetY, rotation)` | required | Begin rendering a page; establish the initial transform. |
| `EndPage()` | required | Finish the current page. |
| `Clear()` | required | Reset all rendered state. |
| `CurrentPageNumber` | required | Current 1-based page number. |
| `StrokePath(path, state)` | required | Stroke a CTM-baked path. |
| `FillPath(path, state, evenOdd)` | required | Fill a CTM-baked path. |
| `FillAndStrokePath(path, state, evenOdd)` | required | Fill and stroke in one call. |
| `FillPathWithTilingPattern(path, state, evenOdd, pattern, renderCb)` | required | Pattern fill. |
| `SetClippingPath(path, state, evenOdd)` | required | Establish a clip region. |
| `DrawImage(image, state)` | required | Draw an image XObject. |
| `SaveState()` / `RestoreState()` | required | Push/pop graphics state (PDF `q`/`Q`). |
| `ApplyCtm(Matrix3x2)` | required | Track CTM for `DrawImage` positioning. |
| `OnGraphicsStateChanged(state)` | required | ExtGState update (alpha, blend mode, …). |
| `RenderSoftMask(subtype, renderCb)` / `ClearSoftMask()` | required | Transparency mask management. |
| `GetPageDimensions()` | required | Return `(width, height, scale)` for soft-mask allocation. |
| `PaintShading(shading, state)` | default no-op | `sh` operator (axial/radial shading). |
| `FillPathWithShadingPattern(path, state, evenOdd, shading)` | default no-op | PatternType 2 shading fill. |

### Creation — `PdfDocumentBuilder`

| Member | Description |
|--------|-------------|
| `static PdfDocumentBuilder Create()` | Start a new document. |
| `AddPage(Action<PdfPageBuilder>)` / `AddPage(PdfSize, Action<PdfPageBuilder>)` | Add a page. |
| `WithMetadata(Action<PdfMetadataBuilder>)` | Set document metadata. |
| `WithAcroForm(Action<PdfAcroFormBuilder>)` | Configure the form dictionary. |
| `WithEncryption(Action<PdfEncryptionSettings>)` / `WithPassword(string)` | Encrypt the output. |
| `AddBookmark(string title, int pageIndex)` | Add a top-level bookmark. |
| `SetPageLabels(int startPageIndex, …)` | Define page-label ranges. |
| `LoadFont(string path, alias)` / `LoadFont(byte[], alias)` / `LoadFont(Stream, alias)` | Embed a custom font. |
| `void Save(string path)` / `void Save(Stream)` / `byte[] ToByteArray()` | Finish and write. |

`PdfPageBuilder` (selected): `AddText` (`(text, x, y)` → fluent `PdfTextBuilder`; `(text, x, y, fontName, fontSize)` → page builder), `AddImage` / `AddImageFromFile` (→ `PdfImageBuilder`), `AddRectangle(left, top, w, h, fillColor?, strokeColor?, lineWidth?)`, `AddLine(x1, y1, x2, y2, strokeColor?, lineWidth?)` (+ a `PdfLength` overload), `AddCircle` / `AddEllipse` / `AddRoundedRectangle` / `AddPath` (→ `PdfPathBuilder`), `AddTextField` / `AddCheckbox` / `AddDropdown` / `AddRadioGroup` / `AddSignatureField`, `AddLink` / `AddExternalLink` / `AddNote` / `AddHighlight`, `WithInches()` / `WithMillimeters()` / `FromTopLeft()` / `FromBottomLeft()`.

### Editing — `PdfDocumentEditor : IDisposable`

| Member | Description |
|--------|-------------|
| `static PdfDocumentEditor Open(string path, string? password = null)` | Load + edit (owns the document). |
| `static PdfDocumentEditor Open(Stream stream, string? password = null, bool leaveOpen = false)` | Load + edit from a stream. |
| `static PdfDocumentEditor CreateBlank()` | New empty editable document. |
| `static PdfDocument Merge(IEnumerable<PdfDocument> sources)` | Combine documents into a new one. |
| `PdfPageCollection Pages` | Page view + operations. |
| `PdfMetadata Metadata` | Info dict + XMP. |
| `PdfOutlineCollection Outlines` | Bookmark tree. |
| `PdfPageLabels PageLabels` | Page-label ranges. |
| `PdfNamedDestinations NamedDestinations` | Named-destination table. |
| `PdfViewerSettings ViewerSettings` | Viewer preferences and open action. |
| `PdfFormFields Forms` | AcroForm field access and flattening. |
| `void Append(PdfDocument source)` | Append all of `source`'s pages. |
| `PdfDocument Extract(int start, int count)` | Copy a page range into a new document. |
| `void Save(string path, PdfSaveOptions? = null)` / `void Save(Stream, PdfSaveOptions? = null)` | Full-rewrite save. |

#### `PdfPageCollection : IReadOnlyList<PdfPage>`

| Member | Description |
|--------|-------------|
| `int Count` / `this[int index]` | Live page view. |
| `void Rotate(int index, int degrees)` / `void RotateBy(int index, int delta)` | Absolute / relative rotation (×90). |
| `void Move(int fromIndex, int toIndex)` | Reorder. |
| `void RemoveAt(int index)` | Delete (with navigation cleanup). |
| `PdfPage InsertBlank(int at, double width, double height)` | Insert a blank page. |
| `PdfPage Import(PdfDocument source, int sourceIndex, int at)` | Cross-document page copy. |
| `PdfPage Duplicate(int index, int at)` | In-document page copy. |
| `void Append(PdfDocument source)` / `void AppendRange(PdfDocument source, int start, int count)` | Append pages. |
| `void Stamp(int index, Action<PdfStampBuilder>)` / `StampRange(...)` / `StampAll(...)` | Stamps and watermarks. |
| `void AddNote(int index, double x, double y, string contents)` | Sticky-note annotation. |
| `void AddLink(int index, PdfRect rect, int targetPageIndex)` | Internal link. |
| `void AddExternalLink(int index, PdfRect rect, string url)` | URI link. |
| `void AddHighlight(int index, PdfRect rect, PdfColor? color = null)` | Highlight (default yellow). |
| `IReadOnlyList<PdfAnnotationInfo> GetAnnotations(int index)` | Read a page's annotations. |
| `void RemoveAnnotationAt(int index, int annotationIndex)` | Remove an annotation by index. |

#### `PdfOutlineCollection : IReadOnlyList<PdfOutlineItem>`

| Member | Description |
|--------|-------------|
| `int Count` / `this[int index]` | Top-level items. |
| `PdfOutlineItem Add(string title, int page)` / `Add(string title, PdfDestination, Action<PdfOutlineItem>? = null)` | Append an item. |
| `PdfOutlineItem Insert(int index, string title, int page)` / `Insert(int index, string title, PdfDestination, Action<PdfOutlineItem>? = null)` | Insert at a position. |
| `void RemoveAt(int index)` / `void Clear()` | Remove one / all. |

`PdfOutlineItem`: `Title`, `Destination`, `IsOpen`, `Children`, `Add(...)`, `Remove()`, `MoveTo(parent, index)`.

#### `PdfNamedDestinations : IReadOnlyCollection<string>`

| Member | Description |
|--------|-------------|
| `int Count` | Total named destinations. |
| `void Set(string name, PdfDestination destination)` | Create or update. |
| `PdfDestination? Get(string name)` / `PdfDestination? this[string name]` | Resolve, or `null`. |
| `IEnumerable<KeyValuePair<string, PdfDestination>> Entries()` | Enumerate name → destination pairs. |
| `bool Rename(string oldName, string newName)` / `bool Remove(string name)` | Rename / delete (`false` if missing). |

#### `PdfViewerSettings`

| Member | Description |
|--------|-------------|
| `PdfPageMode? PageMode` / `PdfPageLayout? PageLayout` | Panel and layout on open. |
| `PdfDestination? OpenAction` | Destination navigated to on open. |
| `bool? HideToolbar` / `HideMenubar` / `HideWindowUI` / `FitWindow` / `CenterWindow` / `DisplayDocTitle` | Viewer-preference flags. |
| `PdfPageMode? NonFullScreenPageMode` | Page mode when leaving full-screen. |
| `PdfReadingDirection? Direction` / `PdfPrintScaling? PrintScaling` / `PdfDuplex? Duplex` | Reading order and print preferences. |

Setting any nullable viewer property to `null` removes the corresponding key.

#### `PdfFormFields : IReadOnlyCollection<PdfFormField>`

| Member | Description |
|--------|-------------|
| `int Count` | Number of fields. |
| `PdfFormField? this[string fullName]` / `bool TryGet(string, out PdfFormField?)` | Field by fully-qualified name. |
| `void Flatten()` / `void Flatten(string fullName)` | Bake field appearances into page content. |

Field types: `PdfTextField` (`Value`), `PdfButtonField` (`Kind`, `IsChecked`, `Check()`/`Uncheck()`, `SelectedOption`), `PdfChoiceField` (`Options`, `SelectedValues`, `SelectedIndices`), `PdfSignatureField` (`IsSigned`).

#### Options & results

| Type | Members |
|------|---------|
| `PdfSaveOptions` | `bool RemoveOrphans` (default `true`), `bool UseObjectStreams` (default `false`), `static PdfSaveOptions Default`. |
| `PdfOptimizationOptions` | `CompressStreams`, `RemoveUnusedObjects`, `UseObjectStreams`, `RecompressImages`, `SubsetFonts`, `ImageJpegQuality`, `MaxImagePixelDimension`, `static Default`. |
| `PdfOptimizationResult` | `ObjectsBefore`, `ObjectsAfter`, `ObjectsRemoved`, `OutputBytes`, `StreamsCompressed`, `ImagesRecompressed`, `FontsSubsetted`. |

#### Destinations & enums

`PdfDestination` factories (navigation targets): `ToPage(int)`, `FitPage(int)`, `FitWidth(int, double? top)`, `FitHeight(int, double? left)`, `At(int, double? left, double? top, double? zoom)`, `FitRect(int, double left, double bottom, double right, double top)`.

| Enum | Values |
|------|--------|
| `PdfPageMode` | `UseNone`, `UseOutlines`, `UseThumbs`, `FullScreen`, `UseOC`, `UseAttachments` |
| `PdfPageLayout` | `SinglePage`, `OneColumn`, `TwoColumnLeft`, `TwoColumnRight`, `TwoPageLeft`, `TwoPageRight` |
| `PdfPageLabelStyle` | `None`, `Decimal`, `UppercaseRoman`, `LowercaseRoman`, `UppercaseLetters`, `LowercaseLetters` |
| `PdfReadingDirection` | `LeftToRight`, `RightToLeft` |
| `PdfPrintScaling` | `AppDefault`, `None` |
| `PdfDuplex` | `Simplex`, `DuplexFlipShortEdge`, `DuplexFlipLongEdge` |

Creation enums — `PdfTextAlignment`, `PdfImageCompression`, `PdfLineCap`, `PdfLineJoin`, `PdfTextRenderMode`, `PdfFillRule`, `PdfEncryptionMethod`, `PdfPermissionFlags` — are shown in the examples above and fully described in IntelliSense.

### Exceptions

| Type | Namespace | Thrown when |
|------|-----------|-------------|
| `PdfException` (abstract base) | `PdfLibrary` | Catch-all for PDF-specific failures. |
| `PdfParseException` | `PdfLibrary.Parsing` | The input is not a valid/parseable PDF. |
| `PdfSecurityException` | `PdfLibrary.Security` | Wrong/missing password, or a decryption failure. |

---

*Internal design (parsers, codecs, the rendering pipeline) is documented in [Architecture.md](Architecture.md). The full public-surface consistency/coverage audit is in [ApiSurfaceAudit.md](ApiSurfaceAudit.md).*
