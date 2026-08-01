using System.IO;
using ICCSharp;
using ICCSharp.Profile;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Rendering.Icc;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// ICC-managed images (ICCBased N=3 8-bit here) should carry a proof-target CMYK plane alongside the
/// decoded RGBA, produced from the SOURCE samples through <see cref="ProofCmykResolver"/> — for images
/// whose colour space is a device space (no embedded profile), no proof leg runs and the plane stays null.
/// </summary>
public class ImageProofCmykTests
{
    private static PdfImage IccRgbImage(byte[] interleavedRgb, int width, int height)
    {
        PdfStream iccStream = new(
            new PdfDictionary { [new PdfName("N")] = new PdfInteger(3) },
            BuiltInProfiles.Srgb.Bytes.ToArray());

        var colorSpace = new PdfArray(new PdfName("ICCBased"), iccStream);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(width),
            [new PdfName("Height")] = new PdfInteger(height),
            [new PdfName("ColorSpace")] = colorSpace,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, interleavedRgb));
    }

    private static PdfImage DeviceRgbImage(byte[] interleavedRgb, int width, int height)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(width),
            [new PdfName("Height")] = new PdfInteger(height),
            [new PdfName("ColorSpace")] = new PdfName("DeviceRGB"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, interleavedRgb));
    }

    // Task 4: image /Intent proof-CMYK differentiation. Same escape hatch as ProofCmykResolverTests /
    // ProofCmykStateTests — the bundled default CMYK profile's A2B tables are byte-identical across
    // intents, so this reuses ProofCmykResolverTests' exact /OutputIntents fixture (RSWOP.icm) and
    // skip-if-absent guard rather than inventing a second one.
    private static readonly string RswopIccPath =
        @"C:\Windows\System32\spool\drivers\color\RSWOP.icm";

    private static PdfLibrary.Structure.PdfDocument DocWithCmykOutputIntent(byte[] destProfileBytes)
    {
        var doc = new PdfLibrary.Structure.PdfDocument();
        var intentDict = new PdfDictionary { [new PdfName("S")] = new PdfName("GTS_PDFA1") };
        doc.AddObject(2, 0, new PdfStream(new PdfDictionary(), destProfileBytes));
        intentDict[new PdfName("DestOutputProfile")] = new PdfIndirectReference(2, 0);
        var intents = new PdfArray { intentDict };
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("OutputIntents")] = intents,
        };
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    [Fact]
    public void Iccbased_image_intent_perceptual_differs_from_default()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfLibrary.Structure.PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        var resolver = new ProofCmykResolver(doc);
        byte[] samples = { 0, 0, 255, 0, 0, 255 };   // 2x1 RGB blue — shows a real per-intent delta
                                                       // through RSWOP.icm (see ProofCmykResolverTests).
        PdfImage image = IccRgbImage(samples, width: 2, height: 1);

        PdfImageToRgba.RgbaImage? decodedDefault = PdfImageToRgba.ToRgba(image, doc: null, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: null, resolver, out byte[]? proofDefault);
        PdfImageToRgba.RgbaImage? decodedPerceptual = PdfImageToRgba.ToRgba(image, doc: null, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: "Perceptual", resolver, out byte[]? proofPerceptual);

        Assert.NotNull(decodedDefault);
        Assert.NotNull(decodedPerceptual);
        Assert.NotNull(proofDefault);
        Assert.NotNull(proofPerceptual);
        Assert.Equal(proofDefault!.Length, proofPerceptual!.Length);

        var maxDelta = 0;
        for (var i = 0; i < proofDefault.Length; i++)
            maxDelta = Math.Max(maxDelta, Math.Abs(proofDefault[i] - proofPerceptual[i]));
        // > 0.005 on the 0..1 scale used elsewhere in this suite == > ~1.3 on the 0..255 byte plane.
        Assert.True(maxDelta > 1, $"expected per-intent difference, max byte delta was {maxDelta}");
    }

    [Fact]
    public void Iccbased_rgb_image_gets_proof_plane()
    {
        var resolver = new ProofCmykResolver(null);
        byte[] samples = { 255, 0, 0, 0, 255, 0 };   // 2x1 RGB
        PdfImage image = IccRgbImage(samples, width: 2, height: 1);

        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, doc: null, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: null, resolver, out byte[]? proof);

        Assert.NotNull(decoded);
        Assert.NotNull(proof);
        Assert.Equal(2 * 1 * 4, proof!.Length);
    }

    [Fact]
    public void Device_rgb_image_gets_no_proof_plane()
    {
        var resolver = new ProofCmykResolver(null);
        byte[] samples = { 255, 0, 0, 0, 255, 0 };   // 2x1 RGB
        PdfImage image = DeviceRgbImage(samples, width: 2, height: 1);

        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, doc: null, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: null, resolver, out byte[]? proof);

        Assert.NotNull(decoded);
        Assert.Null(proof);
    }

    // Defect B (B-2 Phase C Task 5 debug report, panel 13.0): Indexed-over-ICCBased images use a
    // wholly separate palette-resolution path (PdfImage.GetIndexedPalette/TransformIccPalette) that
    // Phase C never wired to renderingIntent/ProofCmykResolver — the "Indexed" case in
    // PdfImageToRgba.cs never assigned proofCmyk. Builds a small (2-entry palette, 2x1 pixel) Indexed
    // image whose base is ICCBased (N=3, sRGB) and drives it through the full ToRgba pipeline exactly
    // like the direct-ICCBased tests above, plus a palette-expansion correctness check against a direct
    // TryIccToProofCmyk conversion of the palette entries.
    private static PdfImage IndexedIccImage(byte[] palette2Entries3Comp, byte[] pixelIndices, int width, int height, int hival)
    {
        PdfStream iccStream = new(
            new PdfDictionary { [new PdfName("N")] = new PdfInteger(3) },
            BuiltInProfiles.Srgb.Bytes.ToArray());

        var indexedColorSpace = new PdfArray(
            new PdfName("Indexed"),
            new PdfArray(new PdfName("ICCBased"), iccStream),
            new PdfInteger(hival),
            new PdfString(palette2Entries3Comp));

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(width),
            [new PdfName("Height")] = new PdfInteger(height),
            [new PdfName("ColorSpace")] = indexedColorSpace,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, pixelIndices));
    }

    [Fact]
    public void Indexed_over_iccbased_image_intent_perceptual_differs_from_default()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfLibrary.Structure.PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        var resolver = new ProofCmykResolver(doc);

        // 2-entry palette: pure blue, pure red (same colours ProofCmykResolverTests/ImageProofCmykTests
        // use elsewhere to show a real RSWOP per-intent delta). Pixels: index 0, index 1.
        byte[] palette = { 0, 0, 255, 255, 0, 0 };
        byte[] pixels = { 0, 1 };
        PdfImage image = IndexedIccImage(palette, pixels, width: 2, height: 1, hival: 1);

        PdfImageToRgba.RgbaImage? decodedDefault = PdfImageToRgba.ToRgba(image, doc, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: null, resolver, out byte[]? proofDefault);
        PdfImageToRgba.RgbaImage? decodedPerceptual = PdfImageToRgba.ToRgba(image, doc, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: "Perceptual", resolver, out byte[]? proofPerceptual);

        Assert.NotNull(decodedDefault);
        Assert.NotNull(decodedPerceptual);
        Assert.NotNull(proofDefault);
        Assert.NotNull(proofPerceptual);
        Assert.Equal(proofDefault!.Length, proofPerceptual!.Length);

        var maxDelta = 0;
        for (var i = 0; i < proofDefault.Length; i++)
            maxDelta = Math.Max(maxDelta, Math.Abs(proofDefault[i] - proofPerceptual[i]));
        Assert.True(maxDelta > 1, $"expected per-intent difference, max byte delta was {maxDelta}");
    }

    [Fact]
    public void Indexed_over_iccbased_image_gets_proof_plane_with_correct_palette_expansion()
    {
        if (!File.Exists(RswopIccPath)) return;
        PdfLibrary.Structure.PdfDocument doc = DocWithCmykOutputIntent(File.ReadAllBytes(RswopIccPath));
        var resolver = new ProofCmykResolver(doc);

        // 2-entry palette: pure blue (index 0), pure red (index 1). 2x1 pixels: [1, 0] — deliberately
        // reversed from palette order so a position bug (e.g. accidentally using pixel order instead of
        // palette index) would be caught.
        byte[] palette = { 0, 0, 255, 255, 0, 0 };
        byte[] pixels = { 1, 0 };
        PdfImage image = IndexedIccImage(palette, pixels, width: 2, height: 1, hival: 1);

        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, doc, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: "Perceptual", resolver, out byte[]? proof);

        Assert.NotNull(decoded);
        Assert.NotNull(proof);
        Assert.Equal(2 * 1 * 4, proof!.Length);

        // Independent oracle: convert the SAME two palette entries directly via TryIccToProofCmyk
        // (ColorSpaceResolver's underlying single-colour path), through the same ICC stream/intent.
        PdfStream iccStream = new(
            new PdfDictionary { [new PdfName("N")] = new PdfInteger(3) },
            BuiltInProfiles.Srgb.Bytes.ToArray());
        double[]? expectedBlue = resolver.TryIccToProofCmyk(iccStream, [0.0 / 255, 0.0 / 255, 255.0 / 255], "Perceptual");
        double[]? expectedRed = resolver.TryIccToProofCmyk(iccStream, [255.0 / 255, 0.0 / 255, 0.0 / 255], "Perceptual");
        Assert.NotNull(expectedBlue);
        Assert.NotNull(expectedRed);

        // Pixel 0 (palette index 1 = red), pixel 1 (palette index 0 = blue).
        AssertProofTupleMatches(proof, pixelOffset: 0, expectedRed!);
        AssertProofTupleMatches(proof, pixelOffset: 4, expectedBlue!);
    }

    private static void AssertProofTupleMatches(byte[] proof, int pixelOffset, double[] expected01)
    {
        for (var c = 0; c < 4; c++)
        {
            var actualByte = proof[pixelOffset + c];
            var expectedByte = (byte)Math.Round(Math.Clamp(expected01[c], 0.0, 1.0) * 255.0);
            Assert.True(Math.Abs(actualByte - expectedByte) <= 1,
                $"channel {c} at pixelOffset {pixelOffset}: actual={actualByte}, expected={expectedByte}");
        }
    }

    [Fact]
    public void Null_proof_resolver_yields_null_plane()
    {
        byte[] samples = { 255, 0, 0, 0, 255, 0 };
        PdfImage image = IccRgbImage(samples, width: 2, height: 1);

        PdfImageToRgba.RgbaImage? decoded = PdfImageToRgba.ToRgba(image, doc: null, imageMaskColor: null,
            blackPointCompensation: false, renderingIntent: null, proofResolver: null, out byte[]? proof);

        Assert.NotNull(decoded);
        Assert.Null(proof);
    }
}
