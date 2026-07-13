using System.Text;
using PdfLibrary.Document;
using PdfLibrary.Structure;
using SkiaSharp;
using Xunit;

namespace PdfLibrary.Rendering.Skia.Tests;

public class TilingPatternCoverageRegressionTests
{
    // A tiling pattern whose BBox (80) is far larger than its XStep/YStep (15) overlaps heavily, and
    // its visible mark sits near the TOP of the BBox. The tiles that paint the region's low-edge band
    // therefore have NEGATIVE lattice origins (origin ≈ -60): their 80-unit content reaches up into the
    // region even though the origin is outside it. A lattice loop that iterates only origins across the
    // region (floor(edge/step)..ceil(edge/step)) skips those tiles and drops a ~BBox-tall band at one
    // edge (observed on veraPDF 6-2-4-3-t02-pass-f: engine covered 87% of the fill height, poppler 99%).
    // The fill must cover the full region height.
    [Fact]
    public void Tiling_pattern_with_bbox_larger_than_step_covers_full_region()
    {
        byte[] pdf = TilingPatternPdf();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));
        PdfPage page = doc.GetPage(0)!;

        using SKImage img = page.RenderToImage(scale: 2.0);
        using SKBitmap bmp = SKBitmap.FromImage(img);

        int top = -1, bottom = -1;
        for (int y = 0; y < bmp.Height; y++)
        {
            var rowHasInk = false;
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor p = bmp.GetPixel(x, y);
                if (p.Red < 128 && p.Green < 128 && p.Blue < 128) { rowHasInk = true; break; }
            }
            if (!rowHasInk) continue;
            if (top < 0) top = y;
            bottom = y;
        }

        Assert.True(top >= 0, "no ink rendered at all");
        double coverage = (bottom - top) / (double)bmp.Height;
        Assert.True(coverage > 0.95,
            $"tiling ink covered only {coverage:P0} of the fill height (rows {top}..{bottom} of {bmp.Height}); " +
            "expected > 95% — a band was dropped at one edge.");
    }

    // Minimal 500x500 page filling [0 0 500 500] with a PatternType-1 tiling pattern: BBox 80x80,
    // XStep/YStep 15 (so tiles overlap), one black bar near the top of the BBox as the visible mark.
    private static byte[] TilingPatternPdf()
    {
        const string pageContent = "/Pattern cs /P0 scn\n0 0 500 500 re f\n";
        const string tileContent = "0 0 0 rg\n2 70 76 6 re f\n";   // bar near the TOP of the 80-tall BBox
        string[] bodies =
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 500 500] "
                + "/Resources << /Pattern << /P0 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {pageContent.Length} >>\nstream\n{pageContent}endstream",
            "<< /Type /Pattern /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 80 80] "
                + $"/XStep 15 /YStep 15 /Matrix [1 0 0 1 0 0] /Resources << >> /Length {tileContent.Length} >>"
                + $"\nstream\n{tileContent}endstream",
        };

        var bytes = new List<byte>();
        void Add(string s) => bytes.AddRange(Encoding.ASCII.GetBytes(s));
        Add("%PDF-1.4\n");
        var offsets = new int[bodies.Length];
        for (var i = 0; i < bodies.Length; i++)
        {
            offsets[i] = bytes.Count;
            Add($"{i + 1} 0 obj\n{bodies[i]}\nendobj\n");
        }
        int xref = bytes.Count, n = bodies.Length + 1;
        Add($"xref\n0 {n}\n0000000000 65535 f \n");
        foreach (int off in offsets) Add($"{off:D10} 00000 n \n");
        Add($"trailer\n<< /Size {n} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return bytes.ToArray();
    }
}
