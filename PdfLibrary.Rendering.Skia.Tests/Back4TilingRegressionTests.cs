using PdfLibrary.Document;
using PdfLibrary.Structure;
using SkiaSharp;
using Xunit;

namespace PdfLibrary.Rendering.Skia.Tests;

public class Back4TilingRegressionTests
{
    // The cairo "back" arrow is filled with a light-cyan tiling pattern. The engine's OLD renderer
    // dropped the fill (outline only) → almost no cyan pixels. The moved walker replays the tile,
    // so a filled arrow yields many cyan-family pixels. Coordinate-free: count cyan pixels page-wide.
    [Fact]
    public void Back4_arrow_tiling_fill_is_present()
    {
        string pdf = Path.Combine(AppContext.BaseDirectory, "Assets", "back__4.pdf");
        using var doc = PdfDocument.Load(pdf);
        PdfPage page = doc.GetPage(0)!;

        using SKImage img = page.RenderToImage(scale: 2.0);
        using SKBitmap bmp = SKBitmap.FromImage(img);

        int cyan = 0;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            SKColor p = bmp.GetPixel(x, y);
            // light cyan: green & blue high, clearly above red (blue-green dominant, red suppressed)
            if (p.Green > 150 && p.Blue > 150 && p.Red + 20 < p.Green && p.Red + 20 < p.Blue) cyan++;
        }

        // Outline-only rendering cannot reach this floor; a filled arrow clears it easily.
        Assert.True(cyan > 500, $"cyan-fill pixel count was {cyan}; expected > 500 (fill dropped?)");
    }
}
