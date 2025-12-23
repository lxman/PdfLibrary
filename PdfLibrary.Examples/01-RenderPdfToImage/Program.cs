using PdfLibrary.Document;
using PdfLibrary.Rendering.SkiaSharp;
using PdfLibrary.Structure;

// ==================== CONFIGURATION ====================
const string pdfPath = @"C:\temp\page2-only.pdf";
const string outputPath = @"C:\temp\our-render.png";
const double scale = 1.0; // 1.0 = 72 DPI
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

// ==================== COMPARE WITH REFERENCE ====================
string referencePath = @"C:\temp\mutool-render.png";
if (File.Exists(referencePath))
{
    Console.WriteLine("Comparing with mutool reference...");
    using var refBitmap = SkiaSharp.SKBitmap.Decode(referencePath);
    using var ourBitmap = SkiaSharp.SKBitmap.Decode(outputPath);

    int compareWidth = Math.Min(refBitmap.Width, ourBitmap.Width);
    int compareHeight = Math.Min(refBitmap.Height, ourBitmap.Height);

    long totalPixels = 0;
    long differentPixels = 0;
    long totalError = 0;

    for (int y = 0; y < compareHeight; y++)
    {
        for (int x = 0; x < compareWidth; x++)
        {
            totalPixels++;
            var refPixel = refBitmap.GetPixel(x, y);
            var ourPixel = ourBitmap.GetPixel(x, y);

            int dr = Math.Abs(refPixel.Red - ourPixel.Red);
            int dg = Math.Abs(refPixel.Green - ourPixel.Green);
            int db = Math.Abs(refPixel.Blue - ourPixel.Blue);
            int error = dr + dg + db;

            if (error > 0)
            {
                differentPixels++;
                totalError += error;

                if (differentPixels <= 10)
                {
                    Console.WriteLine($"  Pixel ({x},{y}): ref=({refPixel.Red},{refPixel.Green},{refPixel.Blue}) our=({ourPixel.Red},{ourPixel.Green},{ourPixel.Blue}) error={error}");
                }
            }
        }
    }

    double errorPercent = (differentPixels * 100.0) / totalPixels;
    double avgError = differentPixels > 0 ? (totalError / (double)differentPixels / 3.0) : 0;

    Console.WriteLine($"\nComparison Results:");
    Console.WriteLine($"  Total pixels:      {totalPixels:N0}");
    Console.WriteLine($"  Different pixels:  {differentPixels:N0} ({errorPercent:F2}%)");
    Console.WriteLine($"  Average error:     {avgError:F2} (out of 255)");
}

Console.WriteLine("\nDone!");
