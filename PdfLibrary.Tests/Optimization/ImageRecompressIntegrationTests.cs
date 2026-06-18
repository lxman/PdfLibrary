using JpegCodec;
using PdfLibrary.Builder;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Optimization;

/// <summary>
/// Integration tests for RecompressImages via PdfOptimizer.Optimize.
/// Builds a PDF containing a high-quality JPEG image, then verifies that
/// optimizing with RecompressImages=true produces a smaller, valid PDF
/// whose image XObject is DCTDecode with consistent Width/Height.
/// </summary>
public class ImageRecompressIntegrationTests
{
    // ── Fixture helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a 256x256 RGB gradient JPEG at the given quality.
    /// The builder only accepts JPEG, so we pre-encode with JpegStreamEncoder.
    /// </summary>
    private static byte[] MakeJpeg(int w, int h, int quality)
    {
        var pixels = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                pixels[i]     = (byte)(x * 255 / (w - 1));
                pixels[i + 1] = (byte)(y * 255 / (h - 1));
                pixels[i + 2] = 128;
            }
        }

        return new JpegStreamEncoder().Encode(pixels, new JpegEncodeOptions
        {
            Width              = w,
            Height             = h,
            NumberOfComponents = 3,
            Quality            = quality,
            ChromaSubsampling  = ChromaSubsampling.Yuv444,
        });
    }

    /// <summary>
    /// Builds a single-page PDF embedding a JPEG image.
    /// </summary>
    private static byte[] BuildPdfWithJpeg(byte[] jpeg)
    {
        return PdfDocumentBuilder.Create()
            .AddPage(p => p.AddImage(jpeg, 50, 50, 400, 400))
            .ToByteArray();
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Control: with default options (RecompressImages=false) the image must be untouched.
    /// We verify the output PDF re-parses and has the same page count.
    /// </summary>
    [Fact]
    public void Optimize_Default_ImageUntouched()
    {
        byte[] jpeg = MakeJpeg(256, 256, quality: 95);
        byte[] srcPdf = BuildPdfWithJpeg(jpeg);

        using var outStream = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(srcPdf)))
            PdfOptimizer.Optimize(doc, outStream, PdfOptimizationOptions.Default);

        outStream.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(outStream); // must not throw
        Assert.Equal(1, reloaded.PageCount);
    }

    /// <summary>
    /// Main integration test: high-quality JPEG → optimize with RecompressImages=true (quality 75)
    /// → output is smaller, re-parses cleanly, image is DCTDecode with matching W/H.
    /// </summary>
    [Fact]
    public void Optimize_RecompressImages_ProducesSmallerValidPdf()
    {
        // Build source PDF with a quality-95 JPEG (larger than what quality-75 produces).
        byte[] jpeg = MakeJpeg(256, 256, quality: 95);
        byte[] srcPdf = BuildPdfWithJpeg(jpeg);
        long inputSize = srcPdf.Length;

        var opts = new PdfOptimizationOptions
        {
            RecompressImages  = true,
            ImageJpegQuality  = 75,
            UseObjectStreams   = false, // isolate image effect by not also packing objects
            CompressStreams    = false,
            RemoveUnusedObjects = false,
        };

        using var outStream = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(srcPdf)))
            PdfOptimizer.Optimize(doc, outStream, opts);

        long outputSize = outStream.Length;

        // 1. Output must be strictly smaller.
        Assert.True(outputSize < inputSize,
            $"Optimized PDF ({outputSize} bytes) should be smaller than source ({inputSize} bytes)");

        // 2. Output must re-parse cleanly.
        outStream.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(outStream);
        Assert.Equal(1, reloaded.PageCount);

        // 3. The image XObject in the reloaded document must be DCTDecode with W=256, H=256.
        reloaded.MaterializeAllObjects();
        bool foundDctImage = false;
        foreach (var obj in reloaded.Objects.Values)
        {
            if (obj is not PdfLibrary.Core.Primitives.PdfStream s) continue;
            if (!PdfLibrary.Document.PdfImage.IsImageXObject(s)) continue;

            // Check filter
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Filter, out var filterObj)) continue;
            if (filterObj is not PdfLibrary.Core.Primitives.PdfName filterName) continue;
            if (filterName.Value != "DCTDecode") continue;

            // Check dimensions
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Width, out var wObj)) continue;
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Height, out var hObj)) continue;
            int w = ((PdfLibrary.Core.Primitives.PdfInteger)wObj).Value;
            int h = ((PdfLibrary.Core.Primitives.PdfInteger)hObj).Value;
            Assert.Equal(256, w);
            Assert.Equal(256, h);

            foundDctImage = true;
            break;
        }

        Assert.True(foundDctImage, "Expected to find a DCTDecode image XObject in the optimized PDF");
    }
}
