using JpegCodec.Stream;
using JpegCodec.Tests.Segments;

namespace JpegCodec.Tests.Stream;

public class MarkerReaderTests
{
    [Fact]
    public void Walk_FindsAllSegments_InMinimalJpeg()
    {
        // SOI, DQT (payload 1 byte), SOF0 (payload 1 byte), DHT (payload 1 byte), EOI
        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xDB, 0x00)          // DQT
            .Segment(0xC0, 0x00)          // SOF0
            .Segment(0xC4, 0x00)          // DHT
            .Eoi()
            .ToArray();

        var reader = new JpegMarkerReader(data);
        var seen = new List<JpegMarker>();
        while (reader.TryReadMarker(out JpegMarker m))
        {
            seen.Add(m);
            if (m == JpegMarker.Eoi) break;
            if (!JpegMarkerReader.IsStandalone(m))
            {
                int len = reader.ReadPayloadLength();
                reader.Skip(len);
            }
        }

        Assert.Equal(
            new[] { JpegMarker.Soi, JpegMarker.Dqt, JpegMarker.Sof0, JpegMarker.Dht, JpegMarker.Eoi },
            seen);
    }

    [Fact]
    public void Walk_SkipsApp0_Length()
    {
        // APP0 with 14-byte payload (length field = 16) followed by EOI.
        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xE0, new byte[14])
            .Eoi()
            .ToArray();

        var reader = new JpegMarkerReader(data);

        Assert.True(reader.TryReadMarker(out JpegMarker soi));
        Assert.Equal(JpegMarker.Soi, soi);

        Assert.True(reader.TryReadMarker(out JpegMarker app0));
        Assert.Equal(JpegMarker.App0, app0);
        int payloadLen = reader.ReadPayloadLength();
        Assert.Equal(14, payloadLen);
        reader.Skip(payloadLen);

        Assert.True(reader.TryReadMarker(out JpegMarker eoi));
        Assert.Equal(JpegMarker.Eoi, eoi);
    }

    [Fact]
    public void Walk_HandlesCom_WithEmbeddedBytes()
    {
        // COM segment carrying arbitrary bytes (no entropy semantics, so
        // 0xFF inside is illegal — exclude those).
        byte[] comPayload = [0x68, 0x69, 0x21]; // "hi!"
        byte[] data = new SyntheticJpeg()
            .Soi()
            .Segment(0xFE, comPayload)
            .Eoi()
            .ToArray();

        var reader = new JpegMarkerReader(data);
        reader.TryReadMarker(out _);   // SOI

        Assert.True(reader.TryReadMarker(out JpegMarker com));
        Assert.Equal(JpegMarker.Com, com);
        int len = reader.ReadPayloadLength();
        Assert.Equal(3, len);
        ReadOnlySpan<byte> payload = reader.ReadPayload(len);
        Assert.Equal(comPayload, payload.ToArray());
    }

    [Fact]
    public void IsStandalone_RecognizesRstAndSoiEoi()
    {
        Assert.True(JpegMarkerReader.IsStandalone(JpegMarker.Soi));
        Assert.True(JpegMarkerReader.IsStandalone(JpegMarker.Eoi));
        Assert.True(JpegMarkerReader.IsStandalone(JpegMarker.Rst0));
        Assert.True(JpegMarkerReader.IsStandalone(JpegMarker.Rst7));
        Assert.True(JpegMarkerReader.IsStandalone(JpegMarker.Tem));
        Assert.False(JpegMarkerReader.IsStandalone(JpegMarker.Sof0));
        Assert.False(JpegMarkerReader.IsStandalone(JpegMarker.Dqt));
    }
}
