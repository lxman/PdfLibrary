using PDFiumSharp;
using SkiaSharp;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

/// <summary>
/// Tool to compare PDFium vs PdfLibrary logo rendering
/// </summary>
public class Program
{
    // Logo region coordinates (based on PDF coordinate system)
    // The logo appears at approximately x=250, y=81 in the rendered output
    private const int LogoX = 250;
    private const int LogoY = 81;
    private const int LogoWidth = 96;
    private const int LogoHeight = 51;

    // Output directory for comparison images
    private static string _outputDir = AppContext.BaseDirectory;

    public static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("=== Image Comparison Tool ===\n");

            string pdfPath = args.Length > 0
                ? args[0]
                : @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";
            Console.WriteLine($"Loading PDF: {pdfPath}\n");

            if (!File.Exists(pdfPath))
            {
                Console.WriteLine($"ERROR: PDF file not found: {pdfPath}");
                return;
            }

            // Render with PDFium
            Console.WriteLine("Rendering with PDFium...");
            var (pdfiumLogo, pdfiumFull) = RenderWithPDFium(pdfPath);
            if (pdfiumLogo == null)
            {
                Console.WriteLine("ERROR: PDFium rendering failed");
                return;
            }
            Console.WriteLine($"✓ PDFium logo extracted: {pdfiumLogo.Width}x{pdfiumLogo.Height}\n");

            // Render with PdfLibrary
            Console.WriteLine("Rendering with PdfLibrary...");
            var (pdfLibraryLogo, pdfLibraryFull) = RenderWithPdfLibrary(pdfPath);
            if (pdfLibraryLogo == null)
            {
                Console.WriteLine("ERROR: PdfLibrary rendering failed");
                return;
            }
            Console.WriteLine($"✓ PdfLibrary logo extracted: {pdfLibraryLogo.Width}x{pdfLibraryLogo.Height}\n");

            // Save full page renders for debugging
            SaveBitmap(pdfiumFull, "PDFium_FullPage.png");
            SaveBitmap(pdfLibraryFull, "PdfLibrary_FullPage.png");

            // Save individual logos
            SaveBitmap(pdfiumLogo, "PDFium_Logo.png");
            SaveBitmap(pdfLibraryLogo, "PdfLibrary_Logo.png");

            // Compare and generate difference map
            Console.WriteLine("Comparing images...");
            var (diffMap, avgDiff, maxDiff, similarityPercent) = CompareImages(pdfiumLogo, pdfLibraryLogo);
            SaveBitmap(diffMap, "Difference_Map.png");

            // Report results
            Console.WriteLine("\n=== Comparison Results ===");
            Console.WriteLine($"Average pixel difference: {avgDiff:F2}");
            Console.WriteLine($"Maximum pixel difference: {maxDiff}");
            Console.WriteLine($"Similarity: {similarityPercent:F2}%");
            Console.WriteLine("\nOutput files:");
            Console.WriteLine("  - PDFium_Logo.png");
            Console.WriteLine("  - PdfLibrary_Logo.png");
            Console.WriteLine("  - Difference_Map.png");

            // Analyze color histograms
            ColorHistogram.AnalyzeAndCompare(
                Path.Combine(_outputDir, "PDFium_Logo.png"),
                Path.Combine(_outputDir, "PdfLibrary_Logo.png"));

            pdfiumLogo.Dispose();
            pdfLibraryLogo.Dispose();
            diffMap.Dispose();

            Console.WriteLine("\n=== Comparison completed ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ ERROR: {ex.Message}");
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
        }
    }

    private static (SKBitmap? logo, SKBitmap? fullPage) RenderWithPDFium(string pdfPath)
    {
        try
        {
            // Load PDF with PDFium
            using var doc = new PDFiumSharp.PdfDocument(pdfPath);
            var page = doc.Pages[0];

            int width = (int)page.Width;
            int height = (int)page.Height;

            // Create PDFium bitmap
            using var pdfiumBitmap = new PDFiumSharp.PDFiumBitmap(width, height, true);

            // Fill with white
            pdfiumBitmap.FillRectangle(0, 0, width, height, 0xFFFFFFFF);

            // Render page to bitmap
            page.Render(pdfiumBitmap, (0, 0, width, height), PDFiumSharp.Enums.PageOrientations.Normal, PDFiumSharp.Enums.RenderingFlags.None);

            // Save to temporary PNG file and reload with SkiaSharp
            string tempPath = Path.GetTempFileName() + ".png";
            try
            {
                pdfiumBitmap.Save(tempPath);
                using var fullPageBitmap = SKBitmap.Decode(tempPath);
                var fullPageCopy = fullPageBitmap.Copy();
                var logo = CropLogoRegion(fullPageCopy.Copy());
                return (logo, fullPageCopy);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PDFium error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return (null, null);
        }
    }

    private static SKBitmap? CropLogoRegion(SKBitmap fullPageBitmap)
    {
        try
        {
            var logoRect = new SKRectI(LogoX, LogoY, LogoX + LogoWidth, LogoY + LogoHeight);
            var logoBitmap = new SKBitmap(LogoWidth, LogoHeight);
            fullPageBitmap.ExtractSubset(logoBitmap, logoRect);
            return logoBitmap;
        }
        finally
        {
            fullPageBitmap.Dispose();
        }
    }

    private static (SKBitmap? logo, SKBitmap? fullPage) RenderWithPdfLibrary(string pdfPath)
    {
        try
        {
            using var stream = File.OpenRead(pdfPath);
            var document = PdfLibrary.Structure.PdfDocument.Load(stream);

            var catalog = document.GetCatalog();
            if (catalog == null) return (null, null);

            var pageTree = catalog.GetPageTree();
            if (pageTree == null) return (null, null);

            var pages = pageTree.GetPages();
            if (pages == null || pages.Count == 0) return (null, null);

            var firstPage = pages[0];
            int width = (int)firstPage.Width;
            int height = (int)firstPage.Height;

            // Create render target for full page (pass document for SMask support)
            var renderTarget = new SkiaSharpRenderTarget(width, height, document);
            var renderer = new PdfRenderer(renderTarget, firstPage.GetResources(), null, document);
            renderer.RenderPage(firstPage);

            // Get the full page image and convert to bitmap
            using var fullPageImage = renderTarget.GetImage();
            var fullPageBitmap = SKBitmap.FromImage(fullPageImage);

            var logo = CropLogoRegion(fullPageBitmap.Copy());
            return (logo, fullPageBitmap);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"PdfLibrary error: {ex.Message}");
            return (null, null);
        }
    }

    private static (SKBitmap diffMap, double avgDiff, int maxDiff, double similarity) CompareImages(SKBitmap img1, SKBitmap img2)
    {
        if (img1.Width != img2.Width || img1.Height != img2.Height)
        {
            throw new ArgumentException("Images must have the same dimensions");
        }

        int width = img1.Width;
        int height = img1.Height;
        var diffMap = new SKBitmap(width, height);

        long totalDiff = 0;
        int maxDiff = 0;
        int pixelCount = width * height;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pixel1 = img1.GetPixel(x, y);
                var pixel2 = img2.GetPixel(x, y);

                // Calculate absolute difference for each channel
                int diffR = Math.Abs(pixel1.Red - pixel2.Red);
                int diffG = Math.Abs(pixel1.Green - pixel2.Green);
                int diffB = Math.Abs(pixel1.Blue - pixel2.Blue);

                // Use max channel difference as the pixel difference
                int pixelDiff = Math.Max(Math.Max(diffR, diffG), diffB);
                totalDiff += pixelDiff;
                maxDiff = Math.Max(maxDiff, pixelDiff);

                // Create difference map: red = different, green = similar
                if (pixelDiff > 10) // Threshold for visible difference
                {
                    // Show difference in red, scaled by magnitude
                    byte intensity = (byte)Math.Min(255, pixelDiff * 2);
                    diffMap.SetPixel(x, y, new SKColor(intensity, 0, 0));
                }
                else
                {
                    // Show similarity in grayscale
                    byte gray = pixel1.Red; // Use original pixel
                    diffMap.SetPixel(x, y, new SKColor(gray, gray, gray));
                }
            }
        }

        double avgDiff = totalDiff / (double)pixelCount;
        double similarity = 100.0 * (1.0 - avgDiff / 255.0);

        return (diffMap, avgDiff, maxDiff, similarity);
    }

    private static void SaveBitmap(SKBitmap bitmap, string filename)
    {
        string fullPath = Path.Combine(_outputDir, filename);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(fullPath);
        data.SaveTo(stream);
        Console.WriteLine($"✓ Saved: {filename}");
    }
}
