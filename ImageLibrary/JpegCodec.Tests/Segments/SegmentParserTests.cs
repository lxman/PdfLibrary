using System.Linq;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Tests.Segments;

public class SegmentParserTests
{
    [Fact]
    public void Dqt_ParsesEightBitTable_Identity()
    {
        // Pq=0 (8-bit), Tq=0, 64 entries all 0x01.
        var payload = new byte[1 + 64];
        payload[0] = 0x00; // Pq=0, Tq=0
        for (var i = 0; i < 64; i++) payload[1 + i] = 0x01;

        var tables = QuantizationTable.ParseAll(payload);

        Assert.Single(tables);
        var t = tables[0];
        Assert.Equal(0, t.TableId);
        Assert.Equal(0, t.Precision);
        Assert.All(t.Values, v => Assert.Equal((ushort)1, v));
    }

    [Fact]
    public void Dqt_ParsesSixteenBitTable_BigEndian()
    {
        // Pq=1 (16-bit), Tq=1, 64 entries = [1, 256, 1, 256, ...].
        var payload = new byte[1 + 128];
        payload[0] = 0x11; // Pq=1, Tq=1
        for (var i = 0; i < 64; i++)
        {
            var v = (ushort)((i & 1) == 0 ? 1 : 256);
            BigEndian.WriteUInt16(payload.AsSpan(), 1 + 2 * i, v);
        }

        var tables = QuantizationTable.ParseAll(payload);

        Assert.Single(tables);
        Assert.Equal(1, tables[0].Precision);
        Assert.Equal(1, tables[0].TableId);
        Assert.Equal(1, tables[0].Values[0]);
        Assert.Equal(256, tables[0].Values[1]);
    }

    [Fact]
    public void Dqt_ParsesMultipleTablesInOneSegment()
    {
        var payload = new byte[(1 + 64) * 2];
        payload[0] = 0x00;
        for (var i = 0; i < 64; i++) payload[1 + i] = 5;
        payload[65] = 0x01;
        for (var i = 0; i < 64; i++) payload[66 + i] = 7;

        var tables = QuantizationTable.ParseAll(payload);

        Assert.Equal(2, tables.Length);
        Assert.Equal(0, tables[0].TableId);
        Assert.All(tables[0].Values, v => Assert.Equal((ushort)5, v));
        Assert.Equal(1, tables[1].TableId);
        Assert.All(tables[1].Values, v => Assert.Equal((ushort)7, v));
    }

    [Fact]
    public void Dht_ParsesTableHeader_AndValues()
    {
        // Class=0 (DC), Th=0, BITS = [0,1,5,1,1,1,1,1,1,0,0,0,0,0,0,0] (T.81
        // Table K.3 luma DC), HUFFVAL = 0..11.
        byte[] bits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
        byte[] huffval = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        var payload = new byte[1 + 16 + 12];
        payload[0] = 0x00; // Tc=0, Th=0
        bits.CopyTo(payload, 1);
        huffval.CopyTo(payload, 17);

        var tables = HuffmanTable.ParseAll(payload);

        Assert.Single(tables);
        var t = tables[0];
        Assert.Equal(0, t.Class);
        Assert.Equal(0, t.TableId);
        Assert.Equal(bits, t.Bits);
        Assert.Equal(huffval, t.Values);
    }

    [Fact]
    public void Sof0_ParsesBaseline_3Component_420()
    {
        // Width=16, Height=16, Nf=3, Y H/V=(2,2), Cb H/V=(1,1), Cr H/V=(1,1)
        // Tq: Y=0, Cb=1, Cr=1.
        var payload = new byte[]
        {
            0x08,                  // Precision = 8
            0x00, 0x10,            // Height = 16
            0x00, 0x10,            // Width = 16
            0x03,                  // Nf = 3
            0x01, 0x22, 0x00,      // C1: id=1, H=2 V=2, Tq=0  (Y)
            0x02, 0x11, 0x01,      // C2: id=2, H=1 V=1, Tq=1  (Cb)
            0x03, 0x11, 0x01,      // C3: id=3, H=1 V=1, Tq=1  (Cr)
        };

        var sof = FrameHeader.Parse(JpegMarker.Sof0, payload);

        Assert.Equal(JpegMarker.Sof0, sof.Marker);
        Assert.Equal(8, sof.Precision);
        Assert.Equal(16, sof.Width);
        Assert.Equal(16, sof.Height);
        Assert.Equal(3, sof.NumberOfComponents);
        Assert.Equal(2, sof.Components[0].HorizontalSampling);
        Assert.Equal(2, sof.Components[0].VerticalSampling);
        Assert.Equal(1, sof.Components[1].HorizontalSampling);
        Assert.Equal(1, sof.Components[1].VerticalSampling);
        Assert.Equal(0, sof.Components[0].QuantizationTableId);
        Assert.Equal(1, sof.Components[2].QuantizationTableId);
    }

    [Fact]
    public void Sof2_ParsesProgressive()
    {
        var payload = new byte[]
        {
            0x08, 0x00, 0x08, 0x00, 0x08, 0x01,
            0x01, 0x11, 0x00,
        };

        var sof = FrameHeader.Parse(JpegMarker.Sof2, payload);

        Assert.Equal(JpegMarker.Sof2, sof.Marker);
        Assert.Equal(1, sof.NumberOfComponents);
    }

    [Fact]
    public void Sos_ParsesScanHeader_3Component_Baseline()
    {
        // Ns=3, components: (Cs=1,Td/Ta=0/0), (2,1/1), (3,1/1)
        // Ss=0, Se=63, Ah=0, Al=0.
        var payload = new byte[]
        {
            0x03,
            0x01, 0x00,
            0x02, 0x11,
            0x03, 0x11,
            0x00, 0x3F, 0x00,
        };

        var sos = ScanHeader.Parse(payload);

        Assert.Equal(3, sos.NumberOfComponents);
        Assert.Equal(0, sos.Components[0].DcTableId);
        Assert.Equal(0, sos.Components[0].AcTableId);
        Assert.Equal(1, sos.Components[1].DcTableId);
        Assert.Equal(1, sos.Components[1].AcTableId);
        Assert.Equal(0, sos.SpectralStart);
        Assert.Equal(63, sos.SpectralEnd);
        Assert.Equal(0, sos.ApproxHigh);
        Assert.Equal(0, sos.ApproxLow);
    }

    [Fact]
    public void App14_AdobeMarker_ColorTransform0()
    {
        var payload = new byte[] { (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e',
                                    0x00, 0x64,    // DCTEncodeVersion = 100
                                    0x00, 0x00,    // Flags0
                                    0x00, 0x00,    // Flags1
                                    0x00 };        // ColorTransform = 0

        Assert.True(AdobeApp14.TryParse(payload, out var adobe));
        Assert.Equal(0, adobe!.ColorTransform);
    }

    [Fact]
    public void App14_AdobeMarker_ColorTransform2_Ycck()
    {
        var payload = new byte[] { (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e',
                                    0x00, 0x64, 0x00, 0x00, 0x00, 0x00, 0x02 };

        Assert.True(AdobeApp14.TryParse(payload, out var adobe));
        Assert.Equal(2, adobe!.ColorTransform);
    }

    [Fact]
    public void App14_RejectsNonAdobe()
    {
        var payload = new byte[] { (byte)'X', (byte)'M', (byte)'P', 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        Assert.False(AdobeApp14.TryParse(payload, out var adobe));
        Assert.Null(adobe);
    }

    [Fact]
    public void App0_Jfif_DetectsJfif()
    {
        var payload = new byte[] { (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00 };
        Assert.True(Jfif.IsJfif(payload));
    }

    [Fact]
    public void App0_Jfif_RejectsJfxx()
    {
        // JFXX is a different APP0 extension and must not be treated as JFIF.
        var payload = new byte[] { (byte)'J', (byte)'F', (byte)'X', (byte)'X', 0x00 };
        Assert.False(Jfif.IsJfif(payload));
    }

    [Fact]
    public void Dri_ParsesTwoByteInterval()
    {
        Assert.Equal(16, RestartInterval.Parse([0x00, 0x10]));
        Assert.Equal(0x100, RestartInterval.Parse([0x01, 0x00]));
        Assert.Equal(0, RestartInterval.Parse([0x00, 0x00]));
    }
}
