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
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.SkiaSharp;

// Create render target with page dimensions
var page = document.GetPage(0);
using var target = new SkiaSharpRenderTarget(
    (int)page.Width,
    (int)page.Height);

// Render the page
var renderer = new PdfRenderer(document, target);
renderer.RenderPage(0);

// Save as PNG
target.SavePng("output.png");
```

### Rendering at Higher Resolution

```csharp
// Render at 2x scale (144 DPI instead of 72 DPI)
double scale = 2.0;
using var target = new SkiaSharpRenderTarget(
    (int)(page.Width * scale),
    (int)(page.Height * scale));

// Apply scale transform
target.Scale(scale, scale);

renderer.RenderPage(0);
target.SavePng("output@2x.png");
```

### Rendering Multiple Pages

```csharp
for (int i = 0; i < document.PageCount; i++)
{
    var page = document.GetPage(i);
    using var target = new SkiaSharpRenderTarget(
        (int)page.Width,
        (int)page.Height);

    var renderer = new PdfRenderer(document, target);
    renderer.RenderPage(i);
    target.SavePng($"page_{i + 1}.png");
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

## Next Steps

- Read the [Fluent API Reference](FluentApi.md) for complete API documentation
- Explore the [Architecture Guide](Architecture.md) to understand the library internals
- Check out the test files in `PdfLibrary.Tests` for more examples
