using SkiaSharp;

public class PixelSampler
{
    public static void SampleAndCompare(string pdfiumPath, string pdfLibraryPath)
    {
        using var pdfiumBitmap = SKBitmap.Decode(pdfiumPath);
        using var pdfLibraryBitmap = SKBitmap.Decode(pdfLibraryPath);

        Console.WriteLine("\n=== Pixel Sampling Comparison ===\n");

        // Sample key points in the logo
        var samplePoints = new[]
        {
            (10, 10, "Top-left red area"),
            (25, 25, "Middle of red squares"),
            (50, 25, "Black text 'PDF'"),
            (70, 35, "Black text 'association'"),
            (45, 5, "Between red and black"),
            (5, 5, "Edge of red square"),
            (80, 25, "End of text"),
        };

        foreach (var (x, y, description) in samplePoints)
        {
            if (x >= pdfiumBitmap.Width || y >= pdfiumBitmap.Height ||
                x >= pdfLibraryBitmap.Width || y >= pdfLibraryBitmap.Height)
                continue;

            var pdfiumColor = pdfiumBitmap.GetPixel(x, y);
            var pdfLibraryColor = pdfLibraryBitmap.GetPixel(x, y);

            Console.WriteLine($"[{x}, {y}] {description}");
            Console.WriteLine($"  PDFium:     RGB({pdfiumColor.Red,3}, {pdfiumColor.Green,3}, {pdfiumColor.Blue,3})");
            Console.WriteLine($"  PdfLibrary: RGB({pdfLibraryColor.Red,3}, {pdfLibraryColor.Green,3}, {pdfLibraryColor.Blue,3})");

            int rDiff = Math.Abs(pdfiumColor.Red - pdfLibraryColor.Red);
            int gDiff = Math.Abs(pdfiumColor.Green - pdfLibraryColor.Green);
            int bDiff = Math.Abs(pdfiumColor.Blue - pdfLibraryColor.Blue);
            int totalDiff = rDiff + gDiff + bDiff;

            Console.WriteLine($"  Difference: ΔR={rDiff,3} ΔG={gDiff,3} ΔB={bDiff,3} Total={totalDiff,3}");
            Console.WriteLine();
        }
    }

    public static void SampleRawVsRendered(string rawPath, string renderedPath)
    {
        using var rawBitmap = SKBitmap.Decode(rawPath);
        using var renderedBitmap = SKBitmap.Decode(renderedPath);

        Console.WriteLine("\n=== Raw (300x160) vs Rendered (96x51) Sampling ===\n");

        // Sample the same logical positions (accounting for different resolutions)
        var samplePoints = new[]
        {
            (31, 16, 10, 5, "Top-left red area"),
            (78, 40, 25, 13, "Middle of red squares"),
            (156, 40, 50, 13, "Black text area"),
        };

        foreach (var (rawX, rawY, renderedX, renderedY, description) in samplePoints)
        {
            if (rawX >= rawBitmap.Width || rawY >= rawBitmap.Height ||
                renderedX >= renderedBitmap.Width || renderedY >= renderedBitmap.Height)
                continue;

            var rawColor = rawBitmap.GetPixel(rawX, rawY);
            var renderedColor = renderedBitmap.GetPixel(renderedX, renderedY);

            Console.WriteLine($"{description}");
            Console.WriteLine($"  Raw [{rawX},{rawY}]:      RGB({rawColor.Red,3}, {rawColor.Green,3}, {rawColor.Blue,3})");
            Console.WriteLine($"  Rendered [{renderedX},{renderedY}]: RGB({renderedColor.Red,3}, {renderedColor.Green,3}, {renderedColor.Blue,3})");

            int rDiff = Math.Abs(rawColor.Red - renderedColor.Red);
            int gDiff = Math.Abs(rawColor.Green - renderedColor.Green);
            int bDiff = Math.Abs(rawColor.Blue - renderedColor.Blue);

            Console.WriteLine($"  Difference: ΔR={rDiff,3} ΔG={gDiff,3} ΔB={bDiff,3}");
            Console.WriteLine();
        }
    }
}
