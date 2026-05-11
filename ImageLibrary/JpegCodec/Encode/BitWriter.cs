using System;
using System.Collections.Generic;

namespace JpegCodec.Encode;

// MSB-first bit writer with T.81 §F.1.2.3 byte stuffing — every 0xFF byte
// in the emitted entropy stream is followed by a 0x00 to disambiguate
// from marker bytes.
internal sealed class BitWriter
{
    private readonly List<byte> _bytes;
    private uint _buffer;
    private int _bitsInBuffer;

    public BitWriter(List<byte> output)
    {
        _bytes = output ?? throw new ArgumentNullException(nameof(output));
    }

    // Write n bits (0..16) MSB-first. value's top (n) bits are written.
    public void WriteBits(int value, int n)
    {
        if ((uint)n > 16u)
            throw new ArgumentOutOfRangeException(nameof(n));
        if (n == 0) return;
        // Mask so we only see the lower n bits.
        var masked = (uint)(value & ((1 << n) - 1));
        _buffer = (_buffer << n) | masked;
        _bitsInBuffer += n;
        while (_bitsInBuffer >= 8)
        {
            _bitsInBuffer -= 8;
            var b = (byte)((_buffer >> _bitsInBuffer) & 0xFF);
            Emit(b);
        }
    }

    // Flush remaining bits, padding LSBs with 1 per T.81 §F.1.2.3.
    public void Flush()
    {
        if (_bitsInBuffer == 0) return;
        int pad = 8 - _bitsInBuffer;
        var b = (byte)(((_buffer << pad) | ((1u << pad) - 1)) & 0xFF);
        _bitsInBuffer = 0;
        _buffer = 0;
        Emit(b);
    }

    public bool IsAtByteBoundary => _bitsInBuffer == 0;

    public int CurrentBitOffset => _bitsInBuffer;

    private void Emit(byte b)
    {
        _bytes.Add(b);
        if (b == 0xFF) _bytes.Add(0x00);
    }
}
