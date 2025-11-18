using SkiaSharp;

public class ColorHistogram
{
    public static void AnalyzeAndCompare(string pdfiumPath, string pdfLibraryPath)
    {
        Console.WriteLine("\n=== Color Histogram Analysis ===\n");

        using var pdfiumBitmap = SKBitmap.Decode(pdfiumPath);
        using var pdfLibraryBitmap = SKBitmap.Decode(pdfLibraryPath);

        Console.WriteLine($"Image dimensions: {pdfiumBitmap.Width}×{pdfiumBitmap.Height}");
        Console.WriteLine($"Total pixels: {pdfiumBitmap.Width * pdfiumBitmap.Height}\n");

        // Count colors in PDFium output
        var pdfiumColors = CountColors(pdfiumBitmap);
        Console.WriteLine($"PDFium: {pdfiumColors.Count} unique colors");
        PrintColorHistogram("PDFium", pdfiumColors);

        // Count colors in PdfLibrary output
        var pdfLibraryColors = CountColors(pdfLibraryBitmap);
        Console.WriteLine($"\nPdfLibrary: {pdfLibraryColors.Count} unique colors");
        PrintColorHistogram("PdfLibrary", pdfLibraryColors);

        // Compare color sets
        Console.WriteLine("\n=== Color Set Comparison ===");
        var pdfiumSet = new HashSet<uint>(pdfiumColors.Keys);
        var pdfLibrarySet = new HashSet<uint>(pdfLibraryColors.Keys);

        var commonColors = new HashSet<uint>(pdfiumSet);
        commonColors.IntersectWith(pdfLibrarySet);

        var onlyInPdfium = new HashSet<uint>(pdfiumSet);
        onlyInPdfium.ExceptWith(pdfLibrarySet);

        var onlyInPdfLibrary = new HashSet<uint>(pdfLibrarySet);
        onlyInPdfLibrary.ExceptWith(pdfiumSet);

        Console.WriteLine($"Common colors: {commonColors.Count}");
        Console.WriteLine($"Only in PDFium: {onlyInPdfium.Count}");
        Console.WriteLine($"Only in PdfLibrary: {onlyInPdfLibrary.Count}");

        // Show top differences
        Console.WriteLine("\n=== Top Color Mismatches ===");
        if (onlyInPdfium.Count > 0)
        {
            Console.WriteLine("\nTop colors only in PDFium:");
            foreach (var colorKey in onlyInPdfium.OrderByDescending(k => pdfiumColors[k]).Take(10))
            {
                var (r, g, b) = UnpackColor(colorKey);
                Console.WriteLine($"  RGB({r,3}, {g,3}, {b,3}): {pdfiumColors[colorKey],4} pixels");
            }
        }

        if (onlyInPdfLibrary.Count > 0)
        {
            Console.WriteLine("\nTop colors only in PdfLibrary:");
            foreach (var colorKey in onlyInPdfLibrary.OrderByDescending(k => pdfLibraryColors[k]).Take(10))
            {
                var (r, g, b) = UnpackColor(colorKey);
                Console.WriteLine($"  RGB({r,3}, {g,3}, {b,3}): {pdfLibraryColors[colorKey],4} pixels");
            }
        }

        // Compare counts for common colors
        Console.WriteLine("\n=== Common Color Count Differences ===");
        var countDiffs = new List<(uint color, int pdfiumCount, int pdfLibraryCount, int diff)>();
        foreach (var colorKey in commonColors)
        {
            int pdfiumCount = pdfiumColors[colorKey];
            int pdfLibraryCount = pdfLibraryColors[colorKey];
            int diff = Math.Abs(pdfiumCount - pdfLibraryCount);
            if (diff > 0)
            {
                countDiffs.Add((colorKey, pdfiumCount, pdfLibraryCount, diff));
            }
        }

        foreach (var (colorKey, pdfiumCount, pdfLibraryCount, diff) in countDiffs.OrderByDescending(x => x.diff).Take(10))
        {
            var (r, g, b) = UnpackColor(colorKey);
            Console.WriteLine($"RGB({r,3}, {g,3}, {b,3}): PDFium={pdfiumCount,4} PdfLibrary={pdfLibraryCount,4} Δ={diff,4}");
        }
    }

    private static Dictionary<uint, int> CountColors(SKBitmap bitmap)
    {
        var colorCounts = new Dictionary<uint, int>();

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                uint colorKey = PackColor(color.Red, color.Green, color.Blue);

                if (colorCounts.ContainsKey(colorKey))
                    colorCounts[colorKey]++;
                else
                    colorCounts[colorKey] = 1;
            }
        }

        return colorCounts;
    }

    private static void PrintColorHistogram(string label, Dictionary<uint, int> colorCounts)
    {
        // Show top 15 most common colors
        var topColors = colorCounts.OrderByDescending(kvp => kvp.Value).Take(15);

        Console.WriteLine($"\nTop colors in {label}:");
        int rank = 1;
        foreach (var kvp in topColors)
        {
            var (r, g, b) = UnpackColor(kvp.Key);
            string colorName = GetColorName(r, g, b);
            Console.WriteLine($"{rank,2}. RGB({r,3}, {g,3}, {b,3}) {colorName,-15}: {kvp.Value,4} pixels");
            rank++;
        }
    }

    private static uint PackColor(byte r, byte g, byte b)
    {
        return ((uint)r << 16) | ((uint)g << 8) | b;
    }

    private static (byte r, byte g, byte b) UnpackColor(uint colorKey)
    {
        byte r = (byte)((colorKey >> 16) & 0xFF);
        byte g = (byte)((colorKey >> 8) & 0xFF);
        byte b = (byte)(colorKey & 0xFF);
        return (r, g, b);
    }

    private static string GetColorName(byte r, byte g, byte b)
    {
        if (r == 255 && g == 255 && b == 255) return "[White]";
        if (r == 0 && g == 0 && b == 0) return "[Black]";
        if (r > 200 && g < 100 && b < 100) return "[Red]";
        if (r > 150 && g < 150 && b < 150) return "[Dark Red]";
        if (r < 50 && g < 50 && b < 50) return "[Near Black]";
        if (r > 200 && g > 200 && b > 200) return "[Near White]";
        return "";
    }
}
