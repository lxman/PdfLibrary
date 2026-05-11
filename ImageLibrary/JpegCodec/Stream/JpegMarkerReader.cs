using System;

namespace JpegCodec.Stream;

// Walks a JPEG byte stream at the segment level. Used outside of entropy
// segments — inside scans, JpegByteSource takes over.
//
// Per T.81 §B.1.1.2 and §B.1.1.3:
//   * Every marker is preceded by at least one 0xFF byte; multiple 0xFF
//     fill bytes are permitted.
//   * Stand-alone markers (SOI, EOI, RSTn, TEM) have no payload.
//   * All other markers are followed by a 2-byte big-endian length field
//     that includes the length bytes themselves.
internal sealed class JpegMarkerReader
{
    private readonly byte[] _data;
    private int _pos;

    public JpegMarkerReader(byte[] data, int offset = 0)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if ((uint)offset > (uint)data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _data = data;
        _pos = offset;
    }

    public int Position => _pos;

    public int Length => _data.Length;

    public int Remaining => _data.Length - _pos;

    // Skips zero or more 0xFF fill bytes, then reads the marker byte that
    // follows. Returns false if the stream ends before a marker is found.
    public bool TryReadMarker(out JpegMarker marker)
    {
        marker = JpegMarker.None;

        // Per T.81 each marker starts with at least one 0xFF.
        if (_pos >= _data.Length || _data[_pos] != 0xFF)
            return false;

        // Skip all 0xFF fill bytes.
        while (_pos < _data.Length && _data[_pos] == 0xFF)
            _pos++;

        if (_pos >= _data.Length) return false;

        byte code = _data[_pos++];
        if (code == 0x00)
        {
            // 0xFF00 is a stuffed literal, not a marker. Should not appear
            // here (marker reader runs outside entropy segments) — caller
            // bug.
            throw new InvalidOperationException(
                "0xFF00 byte sequence outside an entropy segment.");
        }

        marker = (JpegMarker)code;
        return true;
    }

    // Reads the 2-byte big-endian length field that immediately follows a
    // variable-length marker. Returns the payload length (i.e. the field
    // value minus the 2 bytes the field itself occupies).
    public int ReadPayloadLength()
    {
        if (_pos + 2 > _data.Length)
            throw new InvalidOperationException("Truncated segment length field.");
        ushort lengthField = BigEndian.ReadUInt16(_data, _pos);
        _pos += 2;
        if (lengthField < 2)
            throw new InvalidOperationException(
                $"Invalid segment length field {lengthField}; must be >= 2.");
        return lengthField - 2;
    }

    // Returns a span over the next 'length' bytes and advances past them.
    public ReadOnlySpan<byte> ReadPayload(int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (_pos + length > _data.Length)
            throw new InvalidOperationException("Truncated segment payload.");
        var slice = new ReadOnlySpan<byte>(_data, _pos, length);
        _pos += length;
        return slice;
    }

    public void Skip(int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_pos + count > _data.Length)
            throw new InvalidOperationException("Skip past end of stream.");
        _pos += count;
    }

    public byte[] Buffer => _data;

    // True if the marker is a stand-alone marker (SOI/EOI/RSTn/TEM) with
    // no length field or payload.
    public static bool IsStandalone(JpegMarker marker)
    {
        switch (marker)
        {
            case JpegMarker.Soi:
            case JpegMarker.Eoi:
            case JpegMarker.Tem:
            case JpegMarker.Rst0:
            case JpegMarker.Rst1:
            case JpegMarker.Rst2:
            case JpegMarker.Rst3:
            case JpegMarker.Rst4:
            case JpegMarker.Rst5:
            case JpegMarker.Rst6:
            case JpegMarker.Rst7:
                return true;
            default:
                return false;
        }
    }
}
