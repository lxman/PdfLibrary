using System;

namespace JpegCodec.Stream;

// Byte source for entropy-coded segments. Per ISO/IEC 10918-1 (T.81) §F.1.2.3:
//
//   * Within an entropy-coded segment, any literal 0xFF byte in the compressed
//     data is followed by a stuffed 0x00. The decoder must skip that 0x00 and
//     deliver only the 0xFF.
//   * If the byte after 0xFF is anything other than 0x00, it is a marker
//     (typically RSTn or EOI). The byte source stops, exposes the marker
//     code, and refuses to deliver further bytes until reset.
//
// This type is only used inside scans; segment headers are parsed by
// JpegMarkerReader directly off the raw byte stream.
internal sealed class JpegByteSource
{
    private readonly byte[] _data;
    private int _position;
    private bool _atMarker;
    private JpegMarker _marker;

    public JpegByteSource(byte[] data, int offset)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if ((uint)offset > (uint)data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));
        _data = data;
        _position = offset;
    }

    public int Position => _position;

    public bool AtMarker => _atMarker;

    public JpegMarker EncounteredMarker => _marker;

    // Returns 0..255 for a delivered byte, or -1 if we are at a marker / EOF.
    // On hitting 0xFF followed by non-zero, advances the position past both
    // bytes and records the marker for the caller.
    public int ReadByte()
    {
        if (_atMarker) return -1;
        if (_position >= _data.Length) return -1;

        byte b = _data[_position++];
        if (b != 0xFF) return b;

        // Found 0xFF. Per T.81 §B.1.1.2 a 0xFF byte may be padded by any
        // number of additional 0xFF "fill" bytes before the actual second
        // marker byte.
        byte next;
        do
        {
            if (_position >= _data.Length)
                throw new InvalidOperationException(
                    "Truncated entropy segment: 0xFF at end of stream.");
            next = _data[_position++];
        } while (next == 0xFF);

        if (next == 0x00)
        {
            // Stuffed zero — deliver the original 0xFF literal.
            return 0xFF;
        }

        // Real marker — stop here.
        _atMarker = true;
        _marker = (JpegMarker)next;
        return -1;
    }

    public bool TryReadFourCleanBytes(out uint packed)
    {
        packed = 0;
        if (_atMarker) return false;
        int remaining = _data.Length - _position;
        if (remaining < 4) return false;

        byte b0 = _data[_position];
        if (b0 == 0xFF) return false;
        byte b1 = _data[_position + 1];
        if (b1 == 0xFF) return false;
        byte b2 = _data[_position + 2];
        if (b2 == 0xFF) return false;
        byte b3 = _data[_position + 3];
        if (b3 == 0xFF) return false;

        packed = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
        _position += 4;
        return true;
    }

    // Step past the marker after the caller has acknowledged it.
    public void ConsumeMarker()
    {
        if (!_atMarker)
            throw new InvalidOperationException("Not at a marker.");
        _atMarker = false;
        _marker = JpegMarker.None;
    }
}
