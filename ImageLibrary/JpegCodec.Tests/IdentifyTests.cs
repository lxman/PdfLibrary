using JpegCodec.Stream;
using JpegCodec.Tests.Segments;

namespace JpegCodec.Tests;

public class IdentifyTests
{
    [Fact]
    public void Identify_Baseline_3Component_420()
    {
        // Smallest possible "JPEG" carrying SOF0 metadata.
        // SOF0 payload: precision=8, height=8, width=16, Nf=3, Y(2,2)/Tq0, Cb(1,1)/Tq1, Cr(1,1)/Tq1
        byte[] sofPayload = [0x08, 0x00, 0x08, 0x00, 0x10, 0x03,
                             0x01, 0x22, 0x00,
                             0x02, 0x11, 0x01,
                             0x03, 0x11, 0x01];

        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xC0, sofPayload)
            .Eoi()
            .ToArray();

        var info = new JpegStreamDecoder().Identify(data);

        Assert.Equal(16, info.Width);
        Assert.Equal(8, info.Height);
        Assert.Equal(3, info.NumberOfComponents);
        Assert.Equal(8, info.Precision);
        Assert.Equal(JpegMarker.Sof0, info.StartOfFrame);
        Assert.False(info.HasAdobeMarker);
        Assert.False(info.HasJfif);
    }

    [Fact]
    public void Identify_DetectsJfif()
    {
        byte[] jfifPayload = [(byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00,
                              0x01, 0x02, 0x00, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00];
        byte[] sofPayload = [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00];

        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xE0, jfifPayload)
            .Segment(0xC0, sofPayload)
            .Eoi()
            .ToArray();

        var info = new JpegStreamDecoder().Identify(data);

        Assert.True(info.HasJfif);
    }

    [Fact]
    public void Identify_DetectsAdobeApp14_YcckTransform()
    {
        // YCCK CMYK with Adobe APP14 transform=2.
        byte[] adobePayload = [(byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e',
                                0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02];
        byte[] sofPayload = [0x08, 0x00, 0x01, 0x00, 0x01, 0x04,
                             0x01, 0x11, 0x00, 0x02, 0x11, 0x00,
                             0x03, 0x11, 0x00, 0x04, 0x11, 0x00];

        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xEE, adobePayload)
            .Segment(0xC0, sofPayload)
            .Eoi()
            .ToArray();

        var info = new JpegStreamDecoder().Identify(data);

        Assert.True(info.HasAdobeMarker);
        Assert.Equal(2, info.AdobeColorTransform);
        Assert.Equal(4, info.NumberOfComponents);
    }

    [Fact]
    public void Identify_DetectsProgressive_Sof2()
    {
        byte[] sofPayload = [0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00];

        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xC2, sofPayload)   // SOF2
            .Eoi()
            .ToArray();

        var info = new JpegStreamDecoder().Identify(data);

        Assert.Equal(JpegMarker.Sof2, info.StartOfFrame);
    }

    [Fact]
    public void Identify_Throws_WhenStreamDoesNotStartWithSoi()
    {
        byte[] data = [0x00, 0x00, 0x00];
        Assert.Throws<InvalidOperationException>(() => new JpegStreamDecoder().Identify(data));
    }

    [Fact]
    public void Identify_Throws_WhenSofIsMissing()
    {
        byte[] data = new SyntheticJpeg().Soi().Eoi().ToArray();
        Assert.Throws<InvalidOperationException>(() => new JpegStreamDecoder().Identify(data));
    }
}
