using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Rendering;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// The DeviceCMYK image path must honour the PDF /Decode array (that is how genuinely-inverted
/// Adobe CMYK JPEGs signal inversion, now that DCTDecode no longer guesses from the Adobe marker).
/// </summary>
public class CmykDecodeArrayTests
{
    private static PdfImage CmykPixel(byte[] cmyk, PdfObject? decode)
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("XObject"),
            [new PdfName("Subtype")] = new PdfName("Image"),
            [new PdfName("Width")] = new PdfInteger(1),
            [new PdfName("Height")] = new PdfInteger(1),
            [new PdfName("ColorSpace")] = new PdfName("DeviceCMYK"),
            [new PdfName("BitsPerComponent")] = new PdfInteger(8),
        };
        if (decode is not null) dict[new PdfName("Decode")] = decode;
        return new PdfImage(new PdfStream(dict, cmyk));
    }

    [Fact]
    public void DeviceCmyk_DecodeArray_InvertsChannels()
    {
        // CMYK [0,0,0,0] = white. /Decode [1 0 1 0 1 0 1 0] maps it to [1,1,1,1] = black.
        PdfImage inverted = CmykPixel([0, 0, 0, 0],
            new PdfArray(new PdfInteger(1), new PdfInteger(0), new PdfInteger(1), new PdfInteger(0),
                         new PdfInteger(1), new PdfInteger(0), new PdfInteger(1), new PdfInteger(0)));
        byte[] px = PdfImageToRgba.ToRgba(inverted, null)!.Value.Rgba;
        Assert.True(px[0] < 5 && px[1] < 5 && px[2] < 5, $"inverted white->black, got ({px[0]},{px[1]},{px[2]})");

        // The same pixel with no /Decode stays white.
        PdfImage plain = CmykPixel([0, 0, 0, 0], null);
        byte[] px2 = PdfImageToRgba.ToRgba(plain, null)!.Value.Rgba;
        Assert.True(px2[0] > 250 && px2[1] > 250 && px2[2] > 250, $"plain white, got ({px2[0]},{px2[1]},{px2[2]})");
    }
}
