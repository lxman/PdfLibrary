using System;
using JpegCodec.Segments;

namespace JpegCodec.Encode;

// Wraps a HuffmanCanonicalTable for encoding: given a symbol, looks up
// its canonical (code, length) pair and writes it via BitWriter.
//
// HuffmanCanonicalTable already builds HuffCode + HuffSize indexed by
// HUFFVAL position. To encode, we need symbol → (code, length); build a
// 256-entry direct map from symbol byte to (code, length).
internal sealed class HuffmanEncoder
{
    private readonly int[] _codeBySymbol;
    private readonly byte[] _lengthBySymbol;

    public HuffmanEncoder(HuffmanCanonicalTable canonical)
    {
        if (canonical is null) throw new ArgumentNullException(nameof(canonical));
        _codeBySymbol = new int[256];
        _lengthBySymbol = new byte[256];
        for (var i = 0; i < canonical.Values.Length; i++)
        {
            byte sym = canonical.Values[i];
            _codeBySymbol[sym] = canonical.HuffCode[i];
            _lengthBySymbol[sym] = canonical.HuffSize[i];
        }
    }

    public void Encode(BitWriter writer, int symbol)
    {
        if ((uint)symbol > 255u)
            throw new ArgumentOutOfRangeException(nameof(symbol));
        byte len = _lengthBySymbol[symbol];
        if (len == 0)
            throw new InvalidOperationException(
                $"Symbol 0x{symbol:X2} has no code in this Huffman table.");
        writer.WriteBits(_codeBySymbol[symbol], len);
    }
}
