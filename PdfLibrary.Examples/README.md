# PdfLibrary Examples

This project demonstrates how to use the PdfLibrary to work with PDF files.

## Page Access and Text Extraction Example

The main example shows how to:
- Load a PDF document
- Access document metadata
- Get page count
- Iterate through pages
- Access page properties (size, rotation, media box, crop box)
- List fonts and XObjects used on each page
- Access content streams
- Extract text from pages
- Get text with position and formatting information
- Display document information

## Running the Example

```bash
dotnet run --project PdfLibrary.Examples <path-to-pdf-file>
```

Example:
```bash
dotnet run --project PdfLibrary.Examples "C:\Documents\sample.pdf"
```

## Expected Output

```
PdfLibrary - Page Access Example
=================================

Loading PDF: sample.pdf
PDF Version: 1.7
Total Objects: 150
XRef Entries: 150

Document Catalog:
  Page Layout: SinglePage
  Page Mode: UseNone
  Language: en-US

Total Pages: 10

Page 1:
  Size: 612.00 x 792.00 points
  Rotation: 0°
  MediaBox: [0, 0, 612, 792] (612x792)
  CropBox: [0, 0, 612, 792] (612x792)
  Fonts: F1, F2, F3
  XObjects: Im1, Im2
  Content Streams: 1
    - Length: 5432 bytes
      Filter: FlateDecode

... and 9 more pages

Document Information:
  Title: Sample Document
  Author: John Doe
  Creator: Microsoft Word
  Producer: Adobe PDF Library

✓ PDF loaded and analyzed successfully!
```

## API Usage

### Load a PDF Document

```csharp
using PdfLibrary.Structure;

using var document = PdfDocument.Load("path/to/file.pdf");
```

### Get Page Count

```csharp
int pageCount = document.GetPageCount();
```

### Access Pages

```csharp
// Get all pages
var pages = document.GetPages();

// Get specific page (0-based index)
var firstPage = document.GetPage(0);
```

### Page Properties

```csharp
var page = document.GetPage(0);

// Size in points (1/72 inch)
double width = page.Width;
double height = page.Height;

// Rotation (0, 90, 180, 270)
int rotation = page.Rotate;

// Page boxes
var mediaBox = page.GetMediaBox();
var cropBox = page.GetCropBox();

// Content streams
var contents = page.GetContents();
foreach (var stream in contents)
{
    byte[] data = stream.GetDecodedData(); // Automatically decompresses
}
```

### Resources

```csharp
var resources = page.GetResources();

// List fonts
var fontNames = resources.GetFontNames();

// Get specific font
var font = resources.GetFont("F1");

// List XObjects (images, forms)
var xobjectNames = resources.GetXObjectNames();

// Get specific XObject
var image = resources.GetXObject("Im1");
```

### Document Catalog

```csharp
var catalog = document.GetCatalog();

// Page tree
var pageTree = catalog.GetPageTree();

// Metadata
var metadata = catalog.GetMetadata();

// Forms
var acroForm = catalog.GetAcroForm();

// Bookmarks
var outlines = catalog.GetOutlines();
```

### Text Extraction

```csharp
// Simple text extraction
var page = document.GetPage(0);
string text = page.ExtractText();
Console.WriteLine(text);

// Extract text with position and formatting information
var (extractedText, fragments) = page.ExtractTextWithFragments();
foreach (var fragment in fragments)
{
    Console.WriteLine($"Text: {fragment.Text}");
    Console.WriteLine($"Position: ({fragment.X}, {fragment.Y})");
    Console.WriteLine($"Font: {fragment.FontName}, Size: {fragment.FontSize}pt");
}

// Extract text from content stream directly
using PdfLibrary.Content;

var contentStream = page.GetContents()[0];
var decodedData = contentStream.GetDecodedData();
string extractedText = PdfTextExtractor.ExtractText(decodedData);
```

### Content Stream Processing

```csharp
using PdfLibrary.Content;

// Parse content stream into operators
var contentData = page.GetContents()[0].GetDecodedData();
var operators = PdfContentParser.Parse(contentData);

// Process operators with custom processor
public class MyProcessor : PdfContentProcessor
{
    protected override void OnShowText(PdfString text)
    {
        Console.WriteLine($"Text operator: {text.Value}");
        Console.WriteLine($"Position: {CurrentState.GetTextPosition()}");
        Console.WriteLine($"Font: {CurrentState.FontName} at {CurrentState.FontSize}pt");
    }
}

var processor = new MyProcessor();
processor.ProcessOperators(operators);
```
