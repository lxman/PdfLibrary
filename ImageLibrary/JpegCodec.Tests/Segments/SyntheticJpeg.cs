using System.Collections.Generic;

namespace JpegCodec.Tests.Segments;

// Small builder for inline synthetic JPEG byte streams. Keeps the tests
// readable — every test states its bytes directly. No interpretation logic
// lives here, only marker-and-length plumbing.
internal sealed class SyntheticJpeg
{
    private readonly List<byte> _bytes = [];

    public SyntheticJpeg Soi() { Marker(0xD8); return this; }
    public SyntheticJpeg Eoi() { Marker(0xD9); return this; }

    public SyntheticJpeg Marker(byte code)
    {
        _bytes.Add(0xFF);
        _bytes.Add(code);
        return this;
    }

    public SyntheticJpeg Segment(byte markerCode, params byte[] payload)
    {
        Marker(markerCode);
        int totalLength = payload.Length + 2;
        _bytes.Add((byte)(totalLength >> 8));
        _bytes.Add((byte)totalLength);
        _bytes.AddRange(payload);
        return this;
    }

    public byte[] ToArray() => _bytes.ToArray();
}
