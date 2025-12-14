using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;

// ==================== CONFIGURATION ====================
const string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\showcase.pdf";
const string outputPath = @"C:\Users\jorda\RiderProjects\PDF\PdfLibrary.Examples\TestPdfs\showcase_page1.png";
const double scale = 2.0; // 2.0 = 144 DPI (higher quality)
const int pageNumber = 1; // 1-based page number

Console.WriteLine("PDF to Image Renderer Example\n");
Console.WriteLine($"Input PDF:  {pdfPath}");
Console.WriteLine($"Output PNG: {outputPath}");
Console.WriteLine($"Scale:      {scale}x (144 DPI)");
Console.WriteLine($"Page:       {pageNumber}\n");

// ==================== LOAD PDF DOCUMENT ====================
Console.WriteLine("Loading PDF document...");
using var stream = File.OpenRead(pdfPath);
using var document = PdfDocument.Load(stream);

Console.WriteLine($"  ✓ Loaded {document.PageCount} page(s)\n");

// ==================== GET PAGE ====================
int pageIndex = pageNumber - 1; // Convert to 0-based index
PdfPage page = document.GetPage(pageIndex)
    ?? throw new InvalidOperationException($"Page {pageNumber} not found");

Console.WriteLine($"Page dimensions: {page.GetCropBox().Width:F2} x {page.GetCropBox().Height:F2} points");

// ==================== CALCULATE OUTPUT DIMENSIONS ====================
PdfRectangle cropBox = page.GetCropBox();
int width = (int)(cropBox.Width * scale);
int height = (int)(cropBox.Height * scale);

Console.WriteLine($"Output size:     {width} x {height} pixels\n");

// ==================== RENDER PAGE ====================
Console.WriteLine("Rendering page...");
using var renderTarget = new SkiaSharpRenderTarget(width, height, document);
page.Render(renderTarget, pageNumber, scale);

// ==================== SAVE TO FILE ====================
Console.WriteLine("Saving to file...");
renderTarget.SaveToFile(outputPath);

Console.WriteLine($"  ✓ Saved to {outputPath}\n");
Console.WriteLine("Done!");
