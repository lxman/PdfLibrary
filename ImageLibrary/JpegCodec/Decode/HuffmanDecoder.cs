using System;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Decode;

// Pulls one canonical Huffman symbol from a JpegBitReader. Uses an
// 8-bit-prefix fast lookup for codes ≤8 bits (the common case in
// real images), falling back to the bit-by-bit walk from T.81 §F.2.2.3
// for longer codes.
internal static class HuffmanDecoder
{
    public static int DecodeSymbol(JpegBitReader reader, HuffmanCanonicalTable table)
    {
        int peek = reader.PeekBits(HuffmanCanonicalTable.FastLookupBits);
        ushort entry = table.FastLookup[peek];
        if (entry != 0)
        {
            int len = entry >> 8;
            reader.SkipBits(len);
            return entry & 0xFF;
        }

        // Code is longer than 8 bits — consume the peeked bits,
        // then continue bit-by-bit.
        int code = peek;
        reader.SkipBits(HuffmanCanonicalTable.FastLookupBits);

        for (int lengthIndex = HuffmanCanonicalTable.FastLookupBits; lengthIndex < 16; lengthIndex++)
        {
            code = (code << 1) | reader.ReadBit();
            if (code <= table.MaxCode[lengthIndex])
            {
                int huffvalIndex = table.ValPtr[lengthIndex] + (code - table.MinCode[lengthIndex]);
                return table.Values[huffvalIndex];
            }
        }

        throw new InvalidOperationException(
            $"Huffman code not found in table — code accumulator after 16 bits = 0x{code:X4}. " +
            $"Table MaxCodes: [{string.Join(",", table.MaxCode)}]");
    }
}
