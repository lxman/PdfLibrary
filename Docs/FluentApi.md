# PDF Builder Fluent API Documentation

This document provides comprehensive documentation for the PDF Builder library's fluent API, enabling you to create PDF documents programmatically with an intuitive, chainable syntax.

## Table of Contents

- [Quick Start](#quick-start)
- [Document Creation](#document-creation)
- [Page Creation](#page-creation)
- [Coordinate Systems](#coordinate-systems)
- [Text](#text)
- [Images](#images)
- [Shapes and Paths](#shapes-and-paths)
- [Form Fields](#form-fields)
- [Annotations](#annotations)
- [Bookmarks](#bookmarks)
- [Page Labels](#page-labels)
- [Layers (Optional Content)](#layers-optional-content)
- [Encryption](#encryption)
- [Metadata](#metadata)
- [Custom Fonts](#custom-fonts)
- [Saving Documents](#saving-documents)

---

## Quick Start

```csharp
using PdfLibrary.Builder;

// Create a simple PDF with one page
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddText("Hello, World!", 100, 700)
        .AddRectangle(100, 650, 200, 30)
            .Stroke(PdfColor.Blue))
    .Save("hello.pdf");
```

---

## Document Creation

All PDF documents start with `PdfDocumentBuilder.Create()`:

```csharp
var builder = PdfDocumentBuilder.Create();
```

The builder supports method chaining, so you can configure the entire document in a single expression:

```csharp
PdfDocumentBuilder.Create()
    .WithMetadata(m => m.Title("My Document").Author("John Doe"))
    .AddPage(page => page.AddText("Page 1", 100, 700))
    .AddPage(page => page.AddText("Page 2", 100, 700))
    .AddBookmark("Page 1", 0)
    .AddBookmark("Page 2", 1)
    .Save("document.pdf");
```

---

## Page Creation

### Adding Pages

```csharp
// Add a letter-sized page (default)
.AddPage(page => page.AddText("Content", 100, 700))

// Add a page with specific size
.AddPage(PdfPageSize.A4, page => page.AddText("A4 Content", 100, 700))

// Add a page with custom dimensions
.AddPage(new PdfSize(400, 600), page => page.AddText("Custom", 100, 500))
```

### Standard Page Sizes

- `PdfPageSize.Letter` - 8.5" x 11" (612 x 792 points)
- `PdfPageSize.Legal` - 8.5" x 14" (612 x 1008 points)
- `PdfPageSize.A4` - 210mm x 297mm (595 x 842 points)
- `PdfPageSize.A3` - 297mm x 420mm (842 x 1191 points)
- `PdfPageSize.A5` - 148mm x 210mm (420 x 595 points)

---

## Coordinate Systems

### Units

By default, coordinates are in PDF points (1/72 inch). You can change the unit system:

```csharp
.AddPage(page => page
    .WithUnit(PdfUnit.Inches)
    .AddText("At 1 inch from left, 10 inches from bottom", 1, 10))

.AddPage(page => page
    .WithMillimeters()  // Shorthand
    .AddText("At 25mm from left", 25, 250))

.AddPage(page => page
    .WithInches()  // Shorthand
    .AddText("At 1.5 inches", 1.5, 9.5))

.AddPage(page => page
    .WithCentimeters()  // Shorthand
    .AddText("At 5cm from left", 5, 25))
```

### Origin

PDF traditionally uses bottom-left origin. You can switch to top-left:

```csharp
.AddPage(page => page
    .FromTopLeft()  // Origin at top-left
    .AddText("100 points from top", 100, 100))

.AddPage(page => page
    .FromBottomLeft()  // Default PDF origin
    .AddText("100 points from bottom", 100, 100))
```

### Combining Units and Origin

```csharp
.AddPage(page => page
    .WithInches()
    .FromTopLeft()
    .AddText("1 inch from left, 1 inch from top", 1, 1))
```

---

## Text

### Basic Text

```csharp
.AddPage(page => page
    .AddText("Simple text", 100, 700))
```

### Styled Text

`AddText()` returns a `PdfTextBuilder` for styling:

```csharp
.AddPage(page => page
    .AddText("Styled text", 100, 700)
        .WithFont("Helvetica-Bold")
        .WithSize(24)
        .WithColor(PdfColor.Blue))
```

### Text Builder Methods

| Method | Description |
|--------|-------------|
| `.WithFont(string name)` | Set font family (e.g., "Helvetica", "Times-Roman", "Courier") |
| `.WithSize(double size)` | Set font size in points |
| `.WithColor(PdfColor color)` | Set text color |
| `.Bold()` | Use bold variant of current font |
| `.Italic()` | Use italic variant of current font |
| `.WithCharacterSpacing(double spacing)` | Adjust spacing between characters |
| `.WithWordSpacing(double spacing)` | Adjust spacing between words |
| `.WithHorizontalScaling(double scale)` | Scale text horizontally (100 = normal) |
| `.WithRise(double rise)` | Raise/lower text (for superscript/subscript) |
| `.WithRenderMode(PdfTextRenderMode mode)` | Fill, stroke, both, invisible, etc. |

### Available Fonts

Standard PDF fonts (always available):
- `Helvetica`, `Helvetica-Bold`, `Helvetica-Oblique`, `Helvetica-BoldOblique`
- `Times-Roman`, `Times-Bold`, `Times-Italic`, `Times-BoldItalic`
- `Courier`, `Courier-Bold`, `Courier-Oblique`, `Courier-BoldOblique`
- `Symbol`, `ZapfDingbats`

### Unit-Aware Text

```csharp
.AddPage(page => page
    .WithInches()
    .AddTextInches("Text at 1.5 inches", 1.5, 9.5)
        .WithSize(14))
```

---

## Images

### Adding Images

```csharp
// From file path
.AddPage(page => page
    .AddImageFromFile("photo.jpg", 100, 500, 200, 150))

// From byte array
byte[] imageData = File.ReadAllBytes("photo.png");
.AddPage(page => page
    .AddImage(imageData, 100, 500, 200, 150))
```

**Supported Image Formats** (automatically detected):
- **JPEG** (.jpg, .jpeg) - Passed through with DCTDecode filter
- **JPEG2000** (.jp2, .j2k) - Passed through with JPXDecode filter (uses Melville.CSJ2K)
- **PNG, BMP, TIFF, GIF, etc.** - Decoded via ImageSharp, then compressed with FlateDecode

### Image Builder Methods

```csharp
.AddImageFromFile("photo.jpg", 100, 500, 200, 150)
    .WithOpacity(0.8)           // 80% opacity
    .WithRotation(45)           // Rotate 45 degrees
    .PreserveAspectRatio()      // Maintain aspect ratio
    .WithLayer(backgroundLayer) // Place on specific layer
```

---

## Shapes and Paths

### Rectangles

```csharp
.AddRectangle(100, 500, 200, 100)  // x, y, width, height
    .Fill(PdfColor.LightBlue)
    .Stroke(PdfColor.Blue, 2)       // 2pt stroke width

.AddRoundedRectangle(100, 400, 200, 100, 10)  // 10pt corner radius
    .Fill(PdfColor.Yellow)
```

### Lines

```csharp
.AddLine(100, 700, 300, 700)  // x1, y1, x2, y2
    .Stroke(PdfColor.Black, 1)

.AddLine(100, 700, 300, 600)
    .Stroke(PdfColor.Red, 2)
    .WithDash(5, 3)  // 5pt dash, 3pt gap
```

### Circles and Ellipses

```csharp
.AddCircle(200, 400, 50)  // centerX, centerY, radius
    .Fill(PdfColor.Green)
    .Stroke(PdfColor.DarkGreen, 2)

.AddEllipse(300, 400, 100, 50)  // centerX, centerY, radiusX, radiusY
    .Fill(PdfColor.Orange)
```

### Custom Paths

```csharp
.AddPath(path => path
    .MoveTo(100, 500)
    .LineTo(200, 600)
    .LineTo(300, 500)
    .CurveTo(350, 450, 250, 400, 200, 450)  // Bezier curve
    .ClosePath())
    .Fill(PdfColor.Purple)
    .Stroke(PdfColor.Black)
```

### Path Builder Methods

| Method | Description |
|--------|-------------|
| `.MoveTo(x, y)` | Move to point without drawing |
| `.LineTo(x, y)` | Draw line to point |
| `.CurveTo(cp1x, cp1y, cp2x, cp2y, x, y)` | Cubic Bezier curve |
| `.QuadraticCurveTo(cpx, cpy, x, y)` | Quadratic Bezier curve |
| `.Arc(x, y, radius, startAngle, endAngle)` | Arc segment |
| `.ClosePath()` | Close path back to start |

### Shape Finishing Methods

| Method | Description |
|--------|-------------|
| `.Fill(PdfColor color)` | Fill the shape |
| `.Stroke(PdfColor color, double width = 1)` | Stroke the outline |
| `.FillAndStroke(PdfColor fill, PdfColor stroke)` | Both fill and stroke |
| `.WithDash(double dash, double gap)` | Dashed line pattern |
| `.WithLineCap(PdfLineCap cap)` | Line cap style (Butt, Round, Square) |
| `.WithLineJoin(PdfLineJoin join)` | Line join style (Miter, Round, Bevel) |

---

## Form Fields

Interactive form fields require enabling the AcroForm:

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page
        .AddTextField("name", 100, 700, 200, 30))
    .WithAcroForm(form => form.SetNeedAppearances(true))
    .Save("form.pdf");
```

### Text Fields

```csharp
.AddTextField("fieldName", 100, 700, 200, 30)  // name, x, y, width, height
    .Value("Default text")
    .WithMaxLength(50)
    .Multiline()
    .Font("Helvetica", 12)
    .Color(PdfColor.Black)
    .Align(PdfTextAlignment.Left)
    .Border(PdfColor.Gray, 1)
    .Required()
    .ReadOnly()

// Password field
.AddTextField("password", 100, 650, 200, 30)
    .Password()
```

### Checkboxes

```csharp
.AddCheckbox("agree", 100, 600, 20, 20)
    .Checked()
    .WithExportValue("Yes")
    .Style(PdfCheckboxStyle.Check)  // Check, Circle, Cross, Diamond, Square, Star
    .Required()
```

### Radio Button Groups

```csharp
.AddRadioGroup("color", group => group
    .AddOption(100, 500, 15, 15, "Red")
    .AddOption(100, 480, 15, 15, "Green")
    .AddOption(100, 460, 15, 15, "Blue")
    .Select("Green")
    .NoToggleOff()
    .Style(PdfRadioStyle.Circle))
```

### Dropdowns

```csharp
.AddDropdown("country", 100, 400, 150, 25)
    .AddOption("United States", "US")
    .AddOption("United Kingdom", "UK")
    .AddOption("Canada", "CA")
    .AddOptions(new[] { "France", "Germany", "Japan" })  // Value = display text
    .Select("US")
    .Editable()  // Allow custom input
    .Sorted()    // Sort options alphabetically
    .Font("Helvetica", 10)
```

### Signature Fields

```csharp
.AddSignatureField("signature", 100, 300, 200, 50)
    .Border(PdfColor.Black, 1)
    .Required()
```

---

## Annotations

### Internal Links (Navigate to Page)

```csharp
.AddPage(page => page.AddText("Page 1", 100, 700))
.AddPage(page => page
    .AddText("Go to Page 1", 100, 700)
    .AddLink(100, 695, 100, 20, 0))  // x, y, width, height, targetPageIndex
```

With configuration:

```csharp
.AddLink(100, 695, 100, 20, 0, link => link
    .WithHighlight(PdfLinkHighlightMode.Push)
    .WithBorder(1)
    .Printable())
```

**Link Highlight Modes:**
- `PdfLinkHighlightMode.None` - No visual feedback
- `PdfLinkHighlightMode.Invert` - Invert colors (default)
- `PdfLinkHighlightMode.Outline` - Invert border only
- `PdfLinkHighlightMode.Push` - Push button effect

### External Links (URLs)

```csharp
.AddExternalLink(100, 700, 100, 20, "https://example.com")
```

### Text Annotations (Sticky Notes)

```csharp
.AddNote(300, 700, "This is a comment")

.AddNote(300, 700, "Help note", note => note
    .WithIcon(PdfTextAnnotationIcon.Help)
    .WithColor(PdfColor.Yellow)
    .Open()       // Show popup initially
    .Printable())
```

**Note Icons:**
- `PdfTextAnnotationIcon.Comment`
- `PdfTextAnnotationIcon.Key`
- `PdfTextAnnotationIcon.Note`
- `PdfTextAnnotationIcon.Help`
- `PdfTextAnnotationIcon.NewParagraph`
- `PdfTextAnnotationIcon.Paragraph`
- `PdfTextAnnotationIcon.Insert`

### Highlight Annotations

```csharp
.AddHighlight(100, 695, 100, 20, highlight => highlight
    .WithColor(PdfColor.Yellow)
    .AddRegion(100, 675, 200, 695)  // Add additional region
    .Printable())
```

---

## Bookmarks

Bookmarks (outlines) provide navigation structure:

### Simple Bookmarks

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Chapter 1", 100, 700))
    .AddPage(page => page.AddText("Chapter 2", 100, 700))
    .AddBookmark("Chapter 1", 0)
    .AddBookmark("Chapter 2", 1)
    .Save("document.pdf");
```

### Configured Bookmarks

```csharp
.AddBookmark("Introduction", bookmark => bookmark
    .ToPage(0)
    .FitWidth()      // Fit page width in viewer
    .Bold()
    .WithColor(PdfColor.Blue))
```

### Hierarchical Bookmarks

```csharp
.AddBookmark("Chapter 1", bookmark => bookmark
    .ToPage(0)
    .Expanded()
    .AddChild("Section 1.1", child => child
        .ToPage(0)
        .AtPosition(100, 500))  // Scroll to specific position
    .AddChild("Section 1.2", child => child
        .ToPage(0)
        .AtPosition(100, 300)))
```

### Bookmark Builder Methods

| Method | Description |
|--------|-------------|
| `.ToPage(int index)` | Navigate to page (0-based) |
| `.AtPosition(double x, double y)` | Scroll to specific position |
| `.FitPage()` | Fit entire page in viewer |
| `.FitWidth()` | Fit page width |
| `.FitHeight()` | Fit page height |
| `.FitRectangle(x, y, width, height)` | Fit specific rectangle |
| `.Expanded()` | Show children by default |
| `.Collapsed()` | Hide children by default |
| `.Bold()` | Bold text |
| `.Italic()` | Italic text |
| `.WithColor(PdfColor)` | Text color |
| `.AddChild(title, configure)` | Add child bookmark |

---

## Page Labels

Page labels customize how page numbers appear in PDF viewers:

### Basic Page Labels

```csharp
PdfDocumentBuilder.Create()
    .AddPage(page => page.AddText("Cover", 100, 700))
    .AddPage(page => page.AddText("Preface i", 100, 700))
    .AddPage(page => page.AddText("Preface ii", 100, 700))
    .AddPage(page => page.AddText("Chapter 1", 100, 700))
    .SetPageLabels(0, label => label.NoNumbering().WithPrefix("Cover"))
    .SetPageLabels(1, label => label.LowercaseRoman())  // i, ii, iii...
    .SetPageLabels(3, label => label.Decimal().StartingAt(1))  // 1, 2, 3...
    .Save("document.pdf");
```

### Shorthand Syntax

```csharp
.SetPageLabels(0, 1, "A-")  // Decimal starting at 1 with "A-" prefix
```

### Page Label Styles

| Method | Output |
|--------|--------|
| `.Decimal()` | 1, 2, 3, 4... |
| `.UppercaseRoman()` | I, II, III, IV... |
| `.LowercaseRoman()` | i, ii, iii, iv... |
| `.UppercaseLetters()` | A, B, C, D... |
| `.LowercaseLetters()` | a, b, c, d... |
| `.NoNumbering()` | No number (use with prefix) |

### Page Label Builder Methods

| Method | Description |
|--------|-------------|
| `.WithPrefix(string prefix)` | Add prefix before number |
| `.StartingAt(int number)` | Start numbering from specific value |

---

## Layers (Optional Content)

Layers allow content to be shown/hidden in PDF viewers:

### Defining Layers

```csharp
PdfDocumentBuilder.Create()
    .DefineLayer("Background", out var background)
    .DefineLayer("Foreground", out var foreground)
    .AddPage(page => page
        .Layer(background, content => content
            .AddRectangle(0, 0, 612, 792)
                .Fill(PdfColor.LightGray))
        .Layer(foreground, content => content
            .AddText("Visible content", 100, 700)))
    .Save("layered.pdf");
```

### Configured Layers

```csharp
.DefineLayer("Watermark", layer => layer
    .Hidden()        // Initially hidden
    .Locked()        // User cannot toggle
    .NeverPrint())   // Don't print even when visible
```

### Layer Builder Methods

| Method | Description |
|--------|-------------|
| `.Hidden()` | Initially hidden |
| `.Visible()` | Initially visible (default) |
| `.Locked()` | Prevent user from toggling |
| `.WithIntent(string intent)` | Set layer intent |
| `.PrintWhenVisible()` | Print when visible (default) |
| `.NeverPrint()` | Never print |
| `.AlwaysPrint()` | Always print regardless of visibility |

---

## Encryption

### Simple Password Protection

```csharp
PdfDocumentBuilder.Create()
    .WithPassword("secret123")  // AES-256, all permissions
    .AddPage(page => page.AddText("Protected content", 100, 700))
    .Save("protected.pdf");
```

### Full Encryption Configuration

```csharp
PdfDocumentBuilder.Create()
    .WithEncryption(encryption => encryption
        .WithUserPassword("viewpassword")      // Required to open
        .WithOwnerPassword("adminpassword")    // Required to change permissions
        .WithMethod(PdfEncryptionMethod.Aes256)
        .AllowPrinting()
        .AllowCopying()
        .DenyAll()  // Start with no permissions, then add
        .AllowFormFilling())
    .AddPage(page => page.AddText("Encrypted", 100, 700))
    .Save("encrypted.pdf");
```

### Encryption Methods

| Method | Description |
|--------|-------------|
| `PdfEncryptionMethod.Rc4_40` | RC4 with 40-bit key (weak) |
| `PdfEncryptionMethod.Rc4_128` | RC4 with 128-bit key |
| `PdfEncryptionMethod.Aes128` | AES with 128-bit key |
| `PdfEncryptionMethod.Aes256` | AES with 256-bit key (recommended) |

### Permission Methods

| Method | Description |
|--------|-------------|
| `.AllowPrinting()` | Allow printing |
| `.AllowCopying()` | Allow copying text/images |
| `.AllowModifying()` | Allow document modification |
| `.AllowAnnotations()` | Allow adding annotations |
| `.AllowFormFilling()` | Allow filling form fields |
| `.AllowAll()` | Grant all permissions |
| `.DenyAll()` | Revoke all permissions |

---

## Metadata

```csharp
PdfDocumentBuilder.Create()
    .WithMetadata(meta => meta
        .Title("Annual Report 2024")
        .Author("Jane Smith")
        .Subject("Financial Summary")
        .Keywords("finance, annual, report")
        .Creator("My Application")
        .Producer("PDF Builder Library"))
    .AddPage(page => page.AddText("Content", 100, 700))
    .Save("report.pdf");
```

---

## Custom Fonts

Load TrueType or OpenType fonts:

```csharp
PdfDocumentBuilder.Create()
    .LoadFont("C:\\Fonts\\MyFont.ttf", "MyFont")
    .LoadFont("C:\\Fonts\\MyFont-Bold.ttf", "MyFontBold")
    .AddPage(page => page
        .AddText("Custom font text", 100, 700)
            .WithFont("MyFont")
            .WithSize(24))
    .Save("custom-font.pdf");
```

**Supported formats:**
- TrueType (.ttf)
- OpenType (.otf)
- TrueType Collection (.ttc)
- WOFF (.woff)
- WOFF2 (.woff2)

---

## Saving Documents

### Save to File

```csharp
builder.Save("output.pdf");
```

### Save to Stream

```csharp
using var stream = new FileStream("output.pdf", FileMode.Create);
builder.Save(stream);
```

### Get as Byte Array

```csharp
byte[] pdfBytes = builder.ToByteArray();
```

---

## Colors

### Predefined Colors

```csharp
PdfColor.Black
PdfColor.White
PdfColor.Red
PdfColor.Green
PdfColor.Blue
PdfColor.Yellow
PdfColor.Cyan
PdfColor.Magenta
PdfColor.Gray
PdfColor.LightGray
PdfColor.DarkGray
PdfColor.Orange
PdfColor.Purple
PdfColor.Pink
PdfColor.Brown
```

### Custom Colors

```csharp
// RGB (0-255)
var color = PdfColor.FromRgb(128, 64, 192);

// RGB (0.0-1.0)
var color = PdfColor.FromRgb(0.5, 0.25, 0.75);

// Grayscale (0.0-1.0)
var gray = PdfColor.FromGray(0.5);

// CMYK (0.0-1.0)
var cmyk = PdfColor.FromCmyk(0, 0.5, 1, 0);

// Hex string
var color = PdfColor.FromHex("#8040C0");

// Separation (Spot Color) - tint range: 0.0-1.0
var spotColor = PdfColor.FromSeparation("PANTONE 185 C", 1.0);  // Full tint
var lightSpot = PdfColor.FromSeparation("PANTONE 185 C", 0.5);  // 50% tint
```

---

## Complete Example

```csharp
using PdfLibrary.Builder;

PdfDocumentBuilder.Create()
    // Metadata
    .WithMetadata(meta => meta
        .Title("Product Catalog")
        .Author("Sales Team"))

    // Custom font
    .LoadFont("fonts/Roboto-Regular.ttf", "Roboto")

    // Layers
    .DefineLayer("Watermark", out var watermark)

    // Cover page
    .AddPage(PdfPageSize.Letter, page => page
        .FromTopLeft()
        .WithInches()
        .Layer(watermark, w => w
            .AddText("DRAFT", 2, 5)
                .WithFont("Helvetica-Bold")
                .WithSize(72)
                .WithColor(PdfColor.FromRgb(200, 200, 200)))
        .AddText("Product Catalog 2024", 1, 1)
            .WithFont("Roboto")
            .WithSize(36)
            .WithColor(PdfColor.Blue)
        .AddImageFromFile("logo.png", 1, 2, 2, 2)
            .PreserveAspectRatio())

    // Content page with form
    .AddPage(page => page
        .AddText("Contact Information", 100, 750)
            .WithSize(18)
            .Bold()
        .AddText("Name:", 100, 700)
        .AddTextField("name", 170, 695, 200, 25)
            .Required()
        .AddText("Email:", 100, 660)
        .AddTextField("email", 170, 655, 200, 25)
        .AddText("Subscribe to newsletter:", 100, 620)
        .AddCheckbox("subscribe", 250, 618, 18, 18)
            .Checked()
        .AddNote(400, 700, "Fill out this form to receive updates"))

    // Page labels
    .SetPageLabels(0, label => label.NoNumbering().WithPrefix("Cover"))
    .SetPageLabels(1, label => label.Decimal())

    // Bookmarks
    .AddBookmark("Cover", 0)
    .AddBookmark("Contact Form", 1)

    // Form settings
    .WithAcroForm(form => form.SetNeedAppearances(true))

    // Save
    .Save("catalog.pdf");
```

---

## API Reference Summary

### PdfDocumentBuilder

| Method | Description |
|--------|-------------|
| `Create()` | Create new builder |
| `AddPage(Action<PdfPageBuilder>)` | Add page with default size |
| `AddPage(PdfSize, Action<PdfPageBuilder>)` | Add page with specific size |
| `WithMetadata(Action<PdfMetadataBuilder>)` | Configure metadata |
| `WithEncryption(Action<PdfEncryptionSettings>)` | Configure encryption |
| `WithPassword(string)` | Simple password protection |
| `WithAcroForm(Action<PdfAcroFormBuilder>)` | Configure form settings |
| `DefineLayer(string, out PdfLayer)` | Define a layer |
| `AddBookmark(string, int)` | Add bookmark to page |
| `AddBookmark(string, Action<PdfBookmarkBuilder>)` | Add configured bookmark |
| `SetPageLabels(int, Action<PdfPageLabelBuilder>)` | Set page labels |
| `LoadFont(string, string)` | Load custom font |
| `Save(string)` | Save to file |
| `Save(Stream)` | Save to stream |
| `ToByteArray()` | Get as bytes |

### PdfPageBuilder

| Method | Description |
|--------|-------------|
| `WithUnit(PdfUnit)` | Set coordinate unit |
| `WithInches()` / `WithMillimeters()` / `WithCentimeters()` | Unit shortcuts |
| `FromTopLeft()` / `FromBottomLeft()` | Set origin |
| `AddText(string, double, double)` | Add text |
| `AddImage(byte[], double, double, double, double)` | Add image |
| `AddImageFromFile(string, double, double, double, double)` | Add image from file |
| `AddRectangle(double, double, double, double)` | Add rectangle |
| `AddRoundedRectangle(double, double, double, double, double)` | Add rounded rectangle |
| `AddLine(double, double, double, double)` | Add line |
| `AddCircle(double, double, double)` | Add circle |
| `AddEllipse(double, double, double, double)` | Add ellipse |
| `AddPath(Action<PdfPathBuilder>)` | Add custom path |
| `AddTextField(string, double, double, double, double)` | Add text field |
| `AddCheckbox(string, double, double, double, double)` | Add checkbox |
| `AddRadioGroup(string, Action<PdfRadioGroupBuilder>)` | Add radio buttons |
| `AddDropdown(string, double, double, double, double)` | Add dropdown |
| `AddSignatureField(string, double, double, double, double)` | Add signature field |
| `AddLink(double, double, double, double, int)` | Add internal link |
| `AddExternalLink(double, double, double, double, string)` | Add URL link |
| `AddNote(double, double, string)` | Add sticky note |
| `AddHighlight(double, double, double, double)` | Add highlight |
| `Layer(PdfLayer, Action<PdfPageBuilder>)` | Add layer content |
