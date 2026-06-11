using JpegCodec;
using PdfLibrary.Filters;

namespace PdfLibrary.Tests.Rendering;

/// <summary>
/// Guards the DCTDecode CMYK path: a 4-component (CMYK) JPEG must decode back to four channels,
/// not get flattened to RGB. If it were flattened, the embedded ICC profile could never be applied
/// (the CMYK would be gone), and CMYK press images would fall back to a naive conversion. Keeping
/// four channels is what lets ImageRenderer route the buffer through the profile.
/// </summary>
public class DctDecodeCmykTests
{
    [Fact]
    public void Cmyk_jpeg_decodes_to_four_channels_so_icc_can_run()
    {
        const int w = 16, h = 8;
        byte[] cmyk = new byte[w * h * 4];
        // A constant CMYK fill round-trips through JPEG near-losslessly (no edges → DC-only blocks).
        for (int i = 0; i < w * h; i++)
        {
            cmyk[i * 4 + 0] = 200; // C
            cmyk[i * 4 + 1] = 100; // M
            cmyk[i * 4 + 2] = 50;  // Y
            cmyk[i * 4 + 3] = 30;  // K
        }

        byte[] jpeg = new JpegStreamEncoder().Encode(cmyk,
            new JpegEncodeOptions { Width = w, Height = h, NumberOfComponents = 4, Quality = 95, EmitAdobeMarker = false });

        byte[] decoded = new DctDecodeFilter().Decode(jpeg);

        // Four channels preserved — NOT flattened to RGB (which would be w*h*3).
        Assert.Equal(w * h * 4, decoded.Length);

        // Centre pixel round-trips within JPEG tolerance, in CMYK order.
        Assert.InRange(decoded[0], 185, 215);
        Assert.InRange(decoded[1], 85, 115);
        Assert.InRange(decoded[2], 35, 65);
        Assert.InRange(decoded[3], 15, 45);
    }
}
