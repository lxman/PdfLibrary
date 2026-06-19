# Getting Started with PdfLibrary

This guide will walk you through the basics of using PdfLibrary to render existing PDFs and create new PDF documents.

## Installation

Add a reference to PdfLibrary in your project:

```xml
<ProjectReference Include="..\PdfLibrary\PdfLibrary.csproj" />
```

For rendering support, also add the SkiaSharp renderer:

```xml
<ProjectReference Include="..\PdfLibrary.Rendering.SkiaSharp\PdfLibrary.Rendering.SkiaSharp.csproj" />
```

## Part 1: Rendering PDFs

### Loading a PDF Document

```csharp
using PdfLibrary;

// Load from file
var document = PdfDocument.Load("path/to/document.pdf");

// Load from stream
using var stream = File.OpenRead("document.pdf");
var document = PdfDocument.Load(stream);

// Load from byte array
byte[] pdfData = File.ReadAllBytes("document.pdf");
var document = PdfDocument.Load(pdfData);
```

### Accessing Document Information

```csharp
// Get page count
int pageCount = document.PageCount;

// Get page dimensions
var page = document.GetPage(0);  // 0-based index
double width = page.Width;       // In points (1/72 inch)
double height = page.Height;
```

### Rendering to an Image

```csharp
using PdfLibrary.Rendering.SkiaSharp;

// Get the page to render
var page = document.GetPage(0);

// Render using the fluent API
page.Render(document)
    .WithScale(1.0)  // 1.0 = 72 DPI
    .ToFile("output.png");
```

### Rendering at Higher Resolution

```csharp
// Render at 2x scale (144 DPI instead of 72 DPI)
page.Render(document)
    .WithScale(2.0)
    .ToFile("output@2x.png");

// Or specify DPI directly
page.Render(document)
    .WithDpi(144)
    .ToFile("output@144dpi.png");
```

### Rendering Multiple Pages

```csharp
for (int i = 0; i < document.PageCount; i++)
{
    var page = document.GetPage(i);
    page.Render(document)
        .WithScale(1.0)
        .ToFile($"page_{i + 1}.png");
}
```

---

## Part 2: Creating PDFs

### Your First PDF

```csharp
using PdfLibrary.Builder;

PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700))
    .Save("hello.pdf");
```

### Adding Styled Text

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Title", 100, 750)
            .WithFont("Helvetica-Bold")
            .WithSize(24)
            .WithColor(PdfColor.Blue)
        .AddText("Regular paragraph text.", 100, 700)
            .WithFont("Times-Roman")
            .WithSize(12)
        .AddText("Italic text", 100, 680)
            .WithFont("Times-Italic")
            .WithSize(12)
            .WithColor(PdfColor.Gray))
    .Save("styled-text.pdf");
```

### Drawing Shapes

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        // Rectangle with fill and stroke
        .AddRectangle(100, 600, 150, 100)
            .Fill(PdfColor.LightBlue)
            .Stroke(PdfColor.Blue, 2)

        // Circle
        .AddCircle(400, 650, 50)
            .Fill(PdfColor.Yellow)
            .Stroke(PdfColor.Orange)

        // Line
        .AddLine(100, 500, 500, 500)
            .Stroke(PdfColor.Black, 1)

        // Rounded rectangle
        .AddRoundedRectangle(100, 350, 200, 100, 15)
            .Fill(PdfColor.LightGray))
    .Save("shapes.pdf");
```

### Adding Images

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Company Logo", 100, 750)
            .WithSize(18)
        .AddImageFromFile("logo.png", 100, 600, 200, 100)
            .PreserveAspectRatio()
        .AddText("Photo Gallery", 100, 500)
            .WithSize(18)
        .AddImageFromFile("photo1.jpg", 100, 300, 150, 150)
        .AddImageFromFile("photo2.jpg", 270, 300, 150, 150))
    .Save("images.pdf");
```

### Multiple Pages

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Page 1: Introduction", 100, 700)
            .WithSize(24))
    .AddPage(page => page
        .AddText("Page 2: Content", 100, 700)
            .WithSize(24))
    .AddPage(PdfPageSize.A4, page => page  // Different page size
        .AddText("Page 3: A4 Size", 100, 800)
            .WithSize(24))
    .Save("multi-page.pdf");
```

### Using Different Coordinate Systems

```csharp
// Default: points (1/72 inch), origin at bottom-left
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Bottom-left origin", 72, 72))  // 1 inch from each edge
    .Save("default.pdf");

// Inches with top-left origin (more intuitive)
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .WithInches()
        .FromTopLeft()
        .AddText("1 inch from top-left", 1, 1)
        .AddText("2 inches down", 1, 2))
    .Save("inches.pdf");

// Millimeters
PdfDocumentBuilder.Create()
    .AddPage(PdfPageSize.A4, page => page
        .WithMillimeters()
        .FromTopLeft()
        .AddText("25mm margins", 25, 25))
    .Save("millimeters.pdf");
```

---

## Part 3: Interactive Features

### Adding Bookmarks

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Chapter 1: Introduction", 100, 700))
    .AddPage(page => page.AddText("Chapter 2: Getting Started", 100, 700))
    .AddPage(page => page.AddText("Chapter 3: Advanced Topics", 100, 700))
    .AddBookmark("Chapter 1", 0)
    .AddBookmark("Chapter 2", 1)
    .AddBookmark("Chapter 3", 2)
    .Save("with-bookmarks.pdf");
```

### Hierarchical Bookmarks

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Introduction", 100, 700))
    .AddPage(page => page.AddText("Section 1.1", 100, 700))
    .AddPage(page => page.AddText("Section 1.2", 100, 700))
    .AddBookmark("Chapter 1", b => b
        .ToPage(0)
        .Expanded()
        .Bold()
        .AddChild("Section 1.1", c => c.ToPage(1))
        .AddChild("Section 1.2", c => c.ToPage(2)))
    .Save("hierarchical-bookmarks.pdf");
```

### Adding Links

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Go to Page 2", 100, 700)
        .AddLink(100, 695, 100, 20, 1))  // Link to page index 1
    .AddPage(page => page
        .AddText("Page 2", 100, 700)
        .AddText("Visit our website", 100, 650)
        .AddExternalLink(100, 645, 120, 20, "https://example.com"))
    .Save("links.pdf");
```

### Adding Notes (Sticky Notes)

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Document with annotations", 100, 700)
        .AddNote(300, 700, "This is a comment!", n => n
            .WithIcon(PdfTextAnnotationIcon.Comment)
            .WithColor(PdfColor.Yellow)))
    .Save("notes.pdf");
```

### Page Labels

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Cover", 100, 700))
    .AddPage(page => page.AddText("Preface", 100, 700))
    .AddPage(page => page.AddText("Page 1", 100, 700))
    .AddPage(page => page.AddText("Page 2", 100, 700))
    .SetPageLabels(0, l => l.NoNumbering().WithPrefix("Cover"))
    .SetPageLabels(1, l => l.LowercaseRoman())      // i
    .SetPageLabels(2, l => l.Decimal().StartingAt(1)) // 1, 2, 3...
    .Save("page-labels.pdf");
```

---

## Part 4: Forms

### Basic Text Field

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Name:", 100, 700)
        .AddTextField("name", 170, 695, 200, 25))
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("text-field.pdf");
```

### Complete Form

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Registration Form", 100, 750)
            .WithSize(18)
            .Bold()

        // Text fields
        .AddText("Name:", 100, 700)
        .AddTextField("name", 200, 695, 250, 25)
            .Required()

        .AddText("Email:", 100, 660)
        .AddTextField("email", 200, 655, 250, 25)

        // Checkbox
        .AddText("Subscribe to newsletter:", 100, 620)
        .AddCheckbox("subscribe", 280, 618, 18, 18)
            .Checked()

        // Radio buttons
        .AddText("Preferred contact:", 100, 580)
        .AddRadioGroup("contact", group => group
            .AddOption(200, 578, 15, 15, "email")
            .AddOption(270, 578, 15, 15, "phone")
            .Select("email"))
        .AddText("Email", 220, 580)
        .AddText("Phone", 290, 580)

        // Dropdown
        .AddText("Country:", 100, 540)
        .AddDropdown("country", 200, 535, 150, 25)
            .AddOption("United States", "US")
            .AddOption("Canada", "CA")
            .AddOption("United Kingdom", "UK")
            .Select("US"))
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("complete-form.pdf");
```

---

## Part 5: Security

### Simple Password Protection

```csharp
PdfDocumentBuilder.Create()
    .WithPassword("secret123")
    .AddPage(page => page
        .AddText("This document is password protected", 100, 700))
    .Save("protected.pdf");
```

### Advanced Encryption

```csharp
PdfDocumentBuilder.Create()
    .WithEncryption(e => e
        .WithUserPassword("view")      // Password to open
        .WithOwnerPassword("admin")    // Password to edit permissions
        .WithMethod(PdfEncryptionMethod.Aes256)
        .AllowPrinting()
        .AllowCopying())
    .AddPage(page => page
        .AddText("Encrypted with AES-256", 100, 700))
    .Save("encrypted.pdf");
```

---

## Part 6: Metadata

```csharp
PdfDocumentBuilder.Create()
    .WithMetadata(m => m
        .Title("Annual Report 2024")
        .Author("John Smith")
        .Subject("Financial Summary")
        .Keywords("finance, annual, report, 2024")
        .Creator("My Application")
        .Producer("PdfLibrary"))
    .AddPage(page => page
        .AddText("Annual Report 2024", 100, 700))
    .Save("with-metadata.pdf");
```

---

## Part 7: Editing Existing PDFs

Load a PDF, change it, and save. `PdfDocument.Edit()` returns an editor whose `Pages` collection supports rotate, reorder, delete, insert, and import.

### Basic Page Operations

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Editing;

using var doc = PdfDocument.Load("input.pdf");
var edit = doc.Edit();

edit.Pages.Rotate(0, 90);     // rotate page 1 by 90°
edit.Pages.Move(3, 0);        // move page 4 to the front
edit.Pages.RemoveAt(1);       // delete page 2

edit.Save("edited.pdf");
```

### Merging Documents

```csharp
using var a = PdfDocument.Load("chapter1.pdf");
using var b = PdfDocument.Load("chapter2.pdf");

// Combine into a new document
using PdfDocument book = PdfDocumentEditor.Merge([a, b]);
book.Save("book.pdf");

// Or append onto an existing one
using var main = PdfDocument.Load("main.pdf");
var edit = main.Edit();
using var appendix = PdfDocument.Load("appendix.pdf");
edit.Append(appendix);
edit.Save("main-with-appendix.pdf");
```

### Splitting and Importing

```csharp
using var doc = PdfDocument.Load("report.pdf");
var edit = doc.Edit();

// Extract pages 5–10 into a new document
using PdfDocument section = edit.Extract(start: 4, count: 6);
section.Save("section.pdf");

// Import a single page from another document
using var cover = PdfDocument.Load("cover.pdf");
edit.Pages.Import(cover, sourceIndex: 0, at: 0);  // put cover at the front
```

### Building Up a New Document

```csharp
using PdfLibrary.Editing;

using var editor = PdfDocumentEditor.CreateBlank();
editor.Pages.InsertBlank(0, 612, 792);            // US-Letter blank page

using var src = PdfDocument.Load("source.pdf");
editor.Pages.Import(src, 0, editor.Pages.Count);  // append source's first page

editor.Save("assembled.pdf");
```

Editing an encrypted PDF works too — pass the password to `Load`; the saved result is unencrypted. Deleting a page automatically removes bookmarks, named destinations, and links that pointed at it. For the complete surface, see the [Editing API Reference](EditingApi.md).

> Editing is an API-only feature today — the WPF viewer can open and render the result, but does not yet provide an in-app editing UI.

---

## Part 8: Optimizing PDFs

Shrink a loaded document with `PdfOptimizer.Optimize`. The default passes are lossless.

### Lossless Optimization

```csharp
using PdfLibrary.Structure;
using PdfLibrary.Optimization;

using var doc = PdfDocument.Load("input.pdf");
using var output = File.Create("optimized.pdf");

// Flate-compresses uncompressed streams, drops unused objects,
// and packs everything into object streams — all lossless.
PdfOptimizer.Optimize(doc, output);
```

### Lossy Size Reductions (Opt-In)

```csharp
using var doc = PdfDocument.Load("input.pdf");
using var output = File.Create("optimized.pdf");

PdfOptimizer.Optimize(doc, output, new PdfOptimizationOptions
{
    // Re-encode eligible images as JPEG (lossy)
    RecompressImages       = true,
    ImageJpegQuality       = 75,     // 1–100
    MaxImagePixelDimension = 2000,   // optional downsample cap (0 = off)

    // Subset embedded fonts to the glyphs actually used (TrueType + CFF)
    SubsetFonts = true,
});
```

| Option | Default | Description |
|--------|---------|-------------|
| `CompressStreams` | `true` | Flate-compress uncompressed streams. |
| `RemoveUnusedObjects` | `true` | Drop objects unreachable from the catalog. |
| `UseObjectStreams` | `true` | Pack into object streams + xref stream (smaller). |
| `RecompressImages` | `false` | **Lossy** — re-encode images as JPEG. |
| `SubsetFonts` | `false` | Subset embedded TrueType/CFF fonts (discards unused glyphs). |
| `ImageJpegQuality` | `75` | JPEG quality when re-encoding images. |
| `MaxImagePixelDimension` | `0` | Downsample images larger than this (0 = off). |

Encrypted input is decrypted and written out unencrypted. The WPF viewer exposes optimization through its **Optimize…** dialog.

---

## Next Steps

- Read the [Fluent API Reference](FluentApi.md) for complete API documentation
- Read the [Editing API Reference](EditingApi.md) for editing, merging, and splitting existing PDFs
- Explore the [Architecture Guide](Architecture.md) to understand the library internals
- Check out the test files in `PdfLibrary.Tests` for more examples
