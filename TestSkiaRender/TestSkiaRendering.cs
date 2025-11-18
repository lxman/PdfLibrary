using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;
using SkiaSharp;

/// <summary>
/// Simple test to verify SkiaSharpRenderTarget basic functionality
/// </summary>
public class TestSkiaRendering
{
    public static void Main(string[] args)
    {
        try
        {
            Console.WriteLine("=== SkiaSharp Rendering Test ===\n");

            // Load a PDF
            string pdfPath = @"C:\Users\jorda\RiderProjects\PDF\PDF Standards\PDF20_AN002-AF.pdf";
            Console.WriteLine($"Loading PDF: {pdfPath}");

            if (!File.Exists(pdfPath))
            {
                Console.WriteLine($"ERROR: PDF file not found: {pdfPath}");
                return;
            }

            using var stream = File.OpenRead(pdfPath);
            var document = PdfDocument.Load(stream);

            Console.WriteLine($"✓ PDF loaded successfully");
            Console.WriteLine($"  Version: {document.Version}");

            // Get the first page
            var catalog = document.GetCatalog();
            if (catalog == null)
            {
                Console.WriteLine("ERROR: Could not get PDF catalog");
                return;
            }

            var pageTree = catalog.GetPageTree();
            if (pageTree == null)
            {
                Console.WriteLine("ERROR: Could not get page tree");
                return;
            }

            var pages = pageTree.GetPages();
            if (pages == null || pages.Count == 0)
            {
                Console.WriteLine("ERROR: No pages found in PDF");
                return;
            }

            var firstPage = pages[0];
            Console.WriteLine($"✓ Found {pages.Count} page(s)");
            Console.WriteLine($"  Page 1 size: {firstPage.Width} x {firstPage.Height} points");

            // Create SkiaSharp render target
            int width = (int)firstPage.Width;
            int height = (int)firstPage.Height;
            Console.WriteLine($"\nCreating render target: {width} x {height}");

            var renderTarget = new SkiaSharpRenderTarget(width, height);

            // Create renderer and render the page
            Console.WriteLine("Rendering page 1...");
            var renderer = new PdfRenderer(renderTarget, firstPage.GetResources(), null, document);
            renderer.RenderPage(firstPage);

            Console.WriteLine("✓ Page rendered successfully");

            // Save the output
            string outputPath = "TestSkiaRendering_Output.png";
            renderTarget.SaveToFile(outputPath, SKEncodedImageFormat.Png, 100);
            Console.WriteLine($"✓ Saved output to: {outputPath}");

            Console.WriteLine("\n=== Test completed successfully ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ ERROR: {ex.Message}");
            Console.WriteLine($"\nStack trace:\n{ex.StackTrace}");
        }
    }
}
