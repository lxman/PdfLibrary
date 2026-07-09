using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// A JPXDecode image whose PDF /ColorSpace nominates an ICCBased source profile must have that profile
/// honoured (ISO 32000 §7.4.9: the dictionary colour space overrides the JP2's own colr box). Covers the
/// degenerate <c>[/Indexed [/ICCBased s] hival lookup]</c> wrapper GWG172 uses around one palette entry.
/// </summary>
public class PdfImageToRgbaJpxTests
{
    private static PdfStream IccStream(int n)
    {
        var dict = new PdfDictionary { [new PdfName("N")] = new PdfInteger(n) };
        return new PdfStream(dict, new byte[] { 0, 0, 0, 0 }); // body unused by the resolver
    }

    private static PdfImage ImageWithColorSpace(PdfObject colorSpace)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = colorSpace,
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        return new PdfImage(new PdfStream(dict, new byte[4]));
    }

    [Fact]
    public void Indexed_ICCBased3_base_resolves_source_profile()
    {
        PdfStream icc = IccStream(3);
        var cs = new PdfArray(new PdfName("Indexed"),
            new PdfArray(new PdfName("ICCBased"), icc), new PdfInteger(0), new PdfString(new byte[] { 1, 2, 3 }));

        Assert.Same(icc, PdfImageToRgba.GetJpxSourceIccProfile(ImageWithColorSpace(cs), null));
    }

    [Fact]
    public void Direct_ICCBased3_resolves_source_profile()
    {
        PdfStream icc = IccStream(3);
        var cs = new PdfArray(new PdfName("ICCBased"), icc);

        Assert.Same(icc, PdfImageToRgba.GetJpxSourceIccProfile(ImageWithColorSpace(cs), null));
    }

    [Fact]
    public void Indexed_DeviceRGB_base_is_not_a_source_profile()
    {
        var cs = new PdfArray(new PdfName("Indexed"),
            new PdfName("DeviceRGB"), new PdfInteger(0), new PdfString(new byte[] { 1, 2, 3 }));

        Assert.Null(PdfImageToRgba.GetJpxSourceIccProfile(ImageWithColorSpace(cs), null));
    }

    [Fact]
    public void Non_three_channel_ICCBased_is_skipped()
    {
        // A 4-channel (CMYK) ICCBased base is not re-interpreted here — the JPX caller only reaches this
        // for a 3-component decode, and the CMYK plane path handles CMYK images.
        var cs = new PdfArray(new PdfName("Indexed"),
            new PdfArray(new PdfName("ICCBased"), IccStream(4)), new PdfInteger(0), new PdfString(new byte[] { 1 }));

        Assert.Null(PdfImageToRgba.GetJpxSourceIccProfile(ImageWithColorSpace(cs), null));
    }

    [Fact]
    public void Bare_DeviceRGB_name_has_no_source_profile()
        => Assert.Null(PdfImageToRgba.GetJpxSourceIccProfile(ImageWithColorSpace(new PdfName("DeviceRGB")), null));
}
