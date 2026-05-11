using System;
using JpegCodec.Segments;
using JpegCodec.Stream;

namespace JpegCodec.Decode;

// Pulls one canonical Huffman symbol from a JpegBitReader. Implements
// T.81 §F.2.2.3 DECODE — walk bit by bit until the accumulated code is
// within [MinCode[L], MaxCode[L]] for some length L.
//
// A faster 8-bit-prefix lookup (HuffmanCanonicalTable.FastLookup) exists
// but is unused on the hot path: it would need a peek-ahead bit reader to
// integrate cleanly. v1 prefers code clarity; performance optimisation
// can come after correctness is locked.
internal static class HuffmanDecoder
{
    public static int DecodeSymbol(JpegBitReader reader, HuffmanCanonicalTable table)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        if (table is null) throw new ArgumentNullException(nameof(table));

        var code = 0;
        for (var lengthIndex = 0; lengthIndex < 16; lengthIndex++)
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
