using JpegCodec;
using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
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

    /// <summary>
    /// Builds a malformed FlateDecode image stream (garbage bytes that will throw during
    /// decode) with the correct dictionary shape to pass IsImageRecompressible.
    /// </summary>
    private static PdfStream MakeMalformedImageStream(int objNum)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")]         = new PdfName("Image"),
            [PdfName.Filter]                  = new PdfName("FlateDecode"),
            [PdfName.Width]                   = new PdfInteger(256),
            [PdfName.Height]                  = new PdfInteger(256),
            [PdfName.ColorSpace]              = new PdfName("DeviceRGB"),
            [PdfName.BitsPerComponent]        = new PdfInteger(8),
        };
        // Garbage bytes — zlib inflate will throw.
        var garbage = new byte[1024];
        for (var i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i & 0xFF);
        var s = new PdfStream(dict, garbage)
        {
            Data = garbage, // set as pre-encoded (the garbage is the "encoded" bytes)
            IsIndirect = true,
            ObjectNumber = objNum
        };
        return s;
    }

    // ── Tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fix 1 robustness: a document with one malformed image (garbage FlateDecode bytes)
    /// and one valid recompressible FlateDecode image. Optimize must not throw, and the
    /// valid image must still be recompressed to DCTDecode.
    /// </summary>
    [Fact]
    public void Optimize_RecompressImages_CorruptImage_DoesNotAbortAndValidImageStillRecompressed()
    {
        byte[] srcPdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("robustness test", 100, 700))
            .ToByteArray();

        var opts = new PdfOptimizationOptions
        {
            RecompressImages    = true,
            ImageJpegQuality    = 75,
            UseObjectStreams     = false,
            CompressStreams      = false,
            RemoveUnusedObjects = false,
        };

        // Load and inject both the malformed image and a valid FlateDecode image.
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(srcPdf));
        doc.MaterializeAllObjects();

        int nextObjNum = doc.Objects.Keys.Max() + 100;

        // Malformed: garbage bytes that will throw in GetDecodedData (FlateDecode inflate).
        PdfStream malformed = MakeMalformedImageStream(nextObjNum);
        doc.AddObject(nextObjNum, 0, malformed);

        // Valid: large RGB gradient as FlateDecode — should recompress to DCTDecode.
        int validObjNum = nextObjNum + 1;
        var validPixels = new byte[256 * 256 * 3];
        for (var y = 0; y < 256; y++)
            for (var x = 0; x < 256; x++)
            {
                int i = (y * 256 + x) * 3;
                validPixels[i]     = (byte)(x);
                validPixels[i + 1] = (byte)(y);
                validPixels[i + 2] = 128;
            }
        var validDict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Width]            = new PdfInteger(256),
            [PdfName.Height]           = new PdfInteger(256),
            [PdfName.ColorSpace]       = new PdfName("DeviceRGB"),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var validStream = new PdfStream(validDict, validPixels);
        validStream.SetEncodedData(validPixels, "FlateDecode");
        validStream.IsIndirect = true;
        validStream.ObjectNumber = validObjNum;
        doc.AddObject(validObjNum, 0, validStream);

        // Optimize must not throw even though one image is corrupt.
        using var outStream = new MemoryStream();
        Exception? ex = Record.Exception(() => PdfOptimizer.Optimize(doc, outStream, opts));
        Assert.Null(ex);

        // The output must still be a parseable PDF.
        outStream.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(outStream);
        Assert.Equal(1, reloaded.PageCount);

        // The valid FlateDecode image must have been recompressed to DCTDecode.
        // We check the in-memory stream object directly (it was mutated by RecompressImages).
        Assert.True(validStream.Dictionary.TryGetValue(PdfName.Filter, out PdfObject fObj),
            "Filter must be present on valid stream after optimization");
        Assert.Equal("DCTDecode", ((PdfName)fObj!).Value);
    }

    /// <summary>
    /// Control: with default options (RecompressImages=false) the image must be untouched.
    /// We verify the output PDF re-parses, has the same page count, and the image filter
    /// is unchanged from the original type (DCTDecode for a JPEG source).
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

        // Image filter must remain DCTDecode — the image was not touched.
        reloaded.MaterializeAllObjects();
        var foundDctImage = false;
        foreach (PdfObject obj in reloaded.Objects.Values)
        {
            if (obj is not PdfLibrary.Core.Primitives.PdfStream s) continue;
            if (!PdfLibrary.Document.PdfImage.IsImageXObject(s)) continue;
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Filter, out PdfObject fObj)) continue;
            if (fObj is PdfLibrary.Core.Primitives.PdfName { Value: "DCTDecode" })
                foundDctImage = true;
        }
        Assert.True(foundDctImage, "Control test: image filter must remain DCTDecode when RecompressImages=false");
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

        // Use a cap so the DCTDecode source triggers the downsample path (Fix 3).
        var opts = new PdfOptimizationOptions
        {
            RecompressImages  = true,
            ImageJpegQuality  = 75,
            MaxImagePixelDimension = 128, // 256 > 128, so DCTDecode source will be downsampled
            UseObjectStreams   = false, // isolate image effect by not also packing objects
            CompressStreams    = false,
            RemoveUnusedObjects = false,
        };

        long inputSize = srcPdf.Length;
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

        // 3. The image XObject in the reloaded document must be DCTDecode with W=128, H=128,
        //    BitsPerComponent=8, and DeviceRGB color space.
        reloaded.MaterializeAllObjects();
        var foundDctImage = false;
        foreach (PdfObject obj in reloaded.Objects.Values)
        {
            if (obj is not PdfLibrary.Core.Primitives.PdfStream s) continue;
            if (!PdfLibrary.Document.PdfImage.IsImageXObject(s)) continue;

            // Check filter
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Filter, out PdfObject filterObj)) continue;
            if (filterObj is not PdfLibrary.Core.Primitives.PdfName filterName) continue;
            if (filterName.Value != "DCTDecode") continue;

            // Check dimensions (downsampled to 128×128)
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Width, out PdfObject wObj)) continue;
            if (!s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.Height, out PdfObject hObj)) continue;
            int w = ((PdfLibrary.Core.Primitives.PdfInteger)wObj).Value;
            int h = ((PdfLibrary.Core.Primitives.PdfInteger)hObj).Value;
            Assert.True(w <= 128 && h <= 128,
                $"Expected dimensions ≤128×128 after downsample, got {w}×{h}");

            // Check BitsPerComponent = 8.
            if (s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.BitsPerComponent, out PdfObject bpcObj))
                Assert.Equal(8, ((PdfLibrary.Core.Primitives.PdfInteger)bpcObj).Value);

            // Check ColorSpace = DeviceRGB.
            if (s.Dictionary.TryGetValue(PdfLibrary.Core.Primitives.PdfName.ColorSpace, out PdfObject csObj) &&
                csObj is PdfLibrary.Core.Primitives.PdfName csName)
                Assert.Equal("DeviceRGB", csName.Value);

            foundDctImage = true;
            break;
        }

        Assert.True(foundDctImage, "Expected to find a DCTDecode image XObject in the optimized PDF");
    }

    // ── Fix 5c: DeviceGray end-to-end recompress ─────────────────────────────

    /// <summary>
    /// End-to-end recompress for a DeviceGray image embedded as FlateDecode.
    /// Uses a 512x512 gray gradient (large enough that JPEG Q75 beats FlateDecode),
    /// injected as an orphan stream and processed via RecompressImages directly.
    /// Verifies the stream's /Filter becomes DCTDecode and /ColorSpace stays DeviceGray.
    /// </summary>
    [Fact]
    public void Optimize_RecompressImages_DeviceGray_Recompressed()
    {
        byte[] basePdf = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("gray test", 100, 700))
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(basePdf));
        doc.MaterializeAllObjects();

        // 512x512 gray image with pseudo-random high-frequency noise pattern.
        // This has low compressibility for FlateDecode (high entropy) but is smooth
        // enough that JPEG Q75 still wins over zlib for a large image.
        // We use a sinusoidal pattern at multiple frequencies to simulate natural image content.
        const int W = 512, H = 512;
        var grayPixels = new byte[W * H];
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                double v = 128
                    + 60 * Math.Sin(x * Math.PI / 32.0)
                    + 40 * Math.Cos(y * Math.PI / 24.0)
                    + 20 * Math.Sin((x + y) * Math.PI / 16.0)
                    + 10 * Math.Cos(x * Math.PI / 8.0) * Math.Sin(y * Math.PI / 12.0);
                grayPixels[y * W + x] = (byte)Math.Clamp((int)v, 0, 255);
            }

        var imgDict = new PdfDictionary
        {
            [new PdfName("Subtype")]  = new PdfName("Image"),
            [PdfName.Width]            = new PdfInteger(W),
            [PdfName.Height]           = new PdfInteger(H),
            [PdfName.ColorSpace]       = new PdfName("DeviceGray"),
            [PdfName.BitsPerComponent] = new PdfInteger(8),
        };
        var imgStream = new PdfStream(imgDict, grayPixels);
        imgStream.SetEncodedData(grayPixels, "FlateDecode");
        int flatLen = imgStream.Length;

        int imgObjNum = doc.Objects.Keys.Max() + 10;
        doc.AddObject(imgObjNum, 0, imgStream);

        var opts = new PdfOptimizationOptions
        {
            RecompressImages    = true,
            ImageJpegQuality    = 75,
            UseObjectStreams     = false,
            CompressStreams      = false,
            RemoveUnusedObjects = false,
        };

        // Call RecompressImages directly so we can inspect the in-memory mutation.
        PdfOptimizer.RecompressImages(doc, opts);

        // The stream must now be DCTDecode.
        Assert.True(imgStream.Dictionary.TryGetValue(PdfName.Filter, out PdfObject fObj2),
            "Filter must be present after recompression");
        Assert.Equal("DCTDecode", ((PdfName)fObj2!).Value);

        // Encoded stream must be smaller than the original FlateDecode.
        Assert.True(imgStream.Length < flatLen,
            $"JPEG ({imgStream.Length}) should be smaller than FlateDecode ({flatLen}) for this gradient");

        // ColorSpace must still be DeviceGray.
        Assert.True(imgStream.Dictionary.TryGetValue(PdfName.ColorSpace, out PdfObject csObj2));
        Assert.Equal("DeviceGray", ((PdfName)csObj2!).Value);
    }
}
