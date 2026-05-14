using System;

namespace JpegCodec.Stream;

// MSB-first bit reader over a JpegByteSource. Implements T.81 §F.2.2.1
// NEXTBIT plus the §F.1.2.1 RECEIVE and EXTEND operations.
//
// Uses a 32-bit MSB-aligned accumulator for batch extraction and
// peek-ahead (needed for fast Huffman lookup).
internal sealed class JpegBitReader
{
    private readonly JpegByteSource _source;
    private uint _buffer;
    private int _bitsInBuffer;
    private bool _exhausted;

    public JpegBitReader(JpegByteSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        _source = source;
    }

    public bool AtMarker => _source.AtMarker;

    private void Fill()
    {
        while (_bitsInBuffer <= 24)
        {
            int b = _source.ReadByte();
            if (b < 0)
            {
                _exhausted = true;
                return;
            }
            _buffer |= (uint)b << (24 - _bitsInBuffer);
            _bitsInBuffer += 8;
        }
    }

    public int ReadBit()
    {
        if (_bitsInBuffer == 0)
        {
            int b = _source.ReadByte();
            if (b < 0)
            {
                _exhausted = true;
                return 0;
            }
            _buffer = (uint)b << 24;
            _bitsInBuffer = 8;
        }
        int bit = (int)(_buffer >> 31);
        _buffer <<= 1;
        _bitsInBuffer--;
        return bit;
    }

    public int PeekBits(int n)
    {
        if (_bitsInBuffer < n) Fill();
        return (int)(_buffer >> (32 - n));
    }

    public void SkipBits(int n)
    {
        _buffer <<= n;
        _bitsInBuffer -= n;
    }

    public int ReadBits(int n)
    {
        if ((uint)n > 16u)
            throw new ArgumentOutOfRangeException(nameof(n), "ReadBits supports up to 16 bits.");
        if (n == 0) return 0;

        if (_bitsInBuffer < n) Fill();

        if (_bitsInBuffer >= n)
        {
            int val = (int)(_buffer >> (32 - n));
            _buffer <<= n;
            _bitsInBuffer -= n;
            return val;
        }

        // Not enough bits after fill (near marker/EOF) — drain what we have.
        var result = 0;
        for (var i = 0; i < n; i++)
            result = (result << 1) | ReadBit();
        return result;
    }

    // T.81 §F.1.2.1 RECEIVE(SSSS) followed by EXTEND.
    public int Receive(int ssss)
    {
        if ((uint)ssss > 16u)
            throw new ArgumentOutOfRangeException(nameof(ssss), "Receive supports up to 16 bits.");
        if (ssss == 0) return 0;

        int v = ReadBits(ssss);
        return Extend(v, ssss);
    }

    // T.81 §F.2.1.3.1 EXTEND.
    internal static int Extend(int v, int t)
    {
        int vt = 1 << (t - 1);
        if (v < vt)
            v += (-1 << t) + 1;
        return v;
    }

    public bool Exhausted => _exhausted;

    public void ResetForNewSegment()
    {
        _buffer = 0;
        _bitsInBuffer = 0;
        _exhausted = false;
    }
}
