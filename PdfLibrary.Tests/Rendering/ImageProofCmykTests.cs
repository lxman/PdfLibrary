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
