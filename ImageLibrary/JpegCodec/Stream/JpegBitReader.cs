using System;

namespace JpegCodec.Stream;

// MSB-first bit reader over a JpegByteSource. Implements T.81 §F.2.2.1
// NEXTBIT plus the §F.1.2.1 RECEIVE and EXTEND operations.
//
// Per T.81 §F.2.2.5, once the underlying byte source has reached a marker
// (i.e. AtMarker is true and no more bytes are available), the decoder
// "shall supply zero-valued bits" — used to flush trailing receive
// operations at the end of a scan.
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

    // Returns the next bit (0 or 1). After the byte source is exhausted at a
    // marker, returns 0 forever (T.81 §F.2.2.5).
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
            _buffer = (uint)b;
            _bitsInBuffer = 8;
        }
        _bitsInBuffer--;
        return (int)((_buffer >> _bitsInBuffer) & 1u);
    }

    // Pull n bits (0..16) as an unsigned integer, MSB first.
    public int ReadBits(int n)
    {
        if ((uint)n > 16u)
            throw new ArgumentOutOfRangeException(nameof(n), "ReadBits supports up to 16 bits.");

        var result = 0;
        for (var i = 0; i < n; i++)
        {
            result = (result << 1) | ReadBit();
        }
        return result;
    }

    // T.81 §F.1.2.1 RECEIVE(SSSS) followed by EXTEND. Reads SSSS bits and
    // returns the signed value: if the leading bit is 1 the value is the
    // unsigned integer; if the leading bit is 0 the value is
    // unsigned - (2^SSSS - 1).
    //
    // SSSS == 0 returns 0 (no bits consumed).
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

    // True once a marker has been seen AND the bit reader has had to
    // synthesize zero bits to satisfy a read. Useful for diagnostics.
    public bool Exhausted => _exhausted;

    // After the underlying byte source consumes a restart marker, the bit
    // reader's accumulator state must be flushed so the next ReadBit
    // pulls fresh bytes from the new entropy segment.
    public void ResetForNewSegment()
    {
        _buffer = 0;
        _bitsInBuffer = 0;
        _exhausted = false;
    }
}
