using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// PdfImageToCmyk produces a native DeviceCMYK plane only for DEVICE CMYK (its samples are output ink).
/// An ICCBased CMYK image carries a source profile that must be ICC-transformed to the output space, so it
/// must NOT be treated as raw output CMYK — doing so ignored the source profile and produced GWG130's red X.
/// TryToCmyk returns null for it, so the caller falls back to the source-profile-managed RGBA path.
/// </summary>
public class ImageToCmykSourceProfileTests
{
    private static PdfName N(string s) => new(s);

    private static PdfImage CmykImage(PdfObject colorSpace) =>
        new(new PdfStream(new PdfDictionary
        {
            [N("Type")] = N("XObject"),
            [N("Subtype")] = N("Image"),
            [N("Width")] = new PdfInteger(1),
            [N("Height")] = new PdfInteger(1),
            [N("ColorSpace")] = colorSpace,
            [N("BitsPerComponent")] = new PdfInteger(8),
        }, [10, 20, 30, 40]));

    [Fact]
    public void DeviceCmyk_image_yields_a_native_cmyk_plane()
    {
        byte[]? cmyk = PdfImageToCmyk.TryToCmyk(CmykImage(N("DeviceCMYK")), null, out _, out _);
        Assert.NotNull(cmyk);
        Assert.Equal([10, 20, 30, 40], cmyk);
    }

    [Fact]
    public void IccBased_cmyk_image_has_no_native_plane()
    {
        var icc = new PdfStream(new PdfDictionary { [N("N")] = new PdfInteger(4) }, new byte[1]);
        Assert.Null(PdfImageToCmyk.TryToCmyk(CmykImage(new PdfArray(N("ICCBased"), icc)), null, out _, out _));
    }
}
