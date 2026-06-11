using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Tests.Segments;

/// <summary>
/// The SOF marker carries image dimensions and sampling factors from untrusted input. Sampling
/// factors are 4-bit nibbles limited to 1..4 (T.81 §A.1.1); 0 divides by zero in MCU sizing and
/// 5..15 inflates the MCU grid. Dimensions can force a multi-gigabyte raster. <see cref="FrameHeader"/>
/// must reject these before any raster is allocated.
/// </summary>
public class FrameHeaderGuardTests
{
    private static byte[] Sof(byte precision, ushort height, ushort width, params (byte id, byte sampling, byte tq)[] comps)
    {
        var p = new byte[6 + 3 * comps.Length];
        p[0] = precision;
        p[1] = (byte)(height >> 8); p[2] = (byte)height;
        p[3] = (byte)(width >> 8);  p[4] = (byte)width;
        p[5] = (byte)comps.Length;
        for (var i = 0; i < comps.Length; i++)
        {
            p[6 + 3 * i] = comps[i].id;
            p[6 + 3 * i + 1] = comps[i].sampling;
            p[6 + 3 * i + 2] = comps[i].tq;
        }
        return p;
    }

    [Fact]
    public void Zero_dimension_is_rejected()
    {
        byte[] payload = Sof(8, height: 0, width: 16, (1, 0x11, 0));
        Assert.Throws<InvalidOperationException>(() => FrameHeader.Parse(JpegMarker.Sof0, payload));
    }

    [Fact]
    public void Out_of_range_sampling_factor_is_rejected()
    {
        byte[] payload = Sof(8, height: 16, width: 16, (1, 0x00, 0)); // H=0, V=0
        Assert.Throws<InvalidOperationException>(() => FrameHeader.Parse(JpegMarker.Sof0, payload));
    }

    [Fact]
    public void Oversize_raster_is_rejected()
    {
        // 65535 × 65535 × 4 components ≈ 17 GB raster.
        byte[] payload = Sof(8, 0xFFFF, 0xFFFF, (1, 0x11, 0), (2, 0x11, 0), (3, 0x11, 0), (4, 0x11, 0));
        Assert.Throws<InvalidOperationException>(() => FrameHeader.Parse(JpegMarker.Sof0, payload));
    }

    [Fact]
    public void Valid_sof_parses()
    {
        byte[] payload = Sof(8, height: 16, width: 16, (1, 0x22, 0), (2, 0x11, 1), (3, 0x11, 1));
        FrameHeader fh = FrameHeader.Parse(JpegMarker.Sof0, payload);
        Assert.Equal(16, fh.Width);
        Assert.Equal(16, fh.Height);
        Assert.Equal(3, fh.NumberOfComponents);
    }
}
