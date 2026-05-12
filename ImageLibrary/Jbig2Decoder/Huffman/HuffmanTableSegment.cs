using System;
using System.Collections.Generic;

namespace Jbig2Decoder.Huffman
{
    /// <summary>
    /// Parser for the JBIG2 Huffman-table segment (type 53, T.88 §7.4.18 + Annex B.2).
    ///
    /// A user-defined Huffman table is delivered as its own segment and referenced
    /// by symbol-dictionary or text-region segments through their referred-to-
    /// segments list whenever a selector field is set to <c>3</c> ("user-defined").
    /// The encoded form lays out:
    ///   - 1 byte flags: HTOOB | (HTPS-1)&lt;&lt;1 | (HTRS-1)&lt;&lt;4
    ///   - 4 bytes HTLOW (signed) — lower bound of the first regular range
    ///   - 4 bytes HTHIGH (signed) — one past the upper bound of the last regular range
    ///   - Then a stream of bit-packed lines:
    ///       * regular lines until <c>CURRANGELOW &gt;= HTHIGH</c>: PREFLEN, RANGELEN
    ///       * one LOWER line: PREFLEN, RANGELEN=32, RANGELOW=HTLOW-1
    ///       * one UPPER line: PREFLEN, RANGELEN=32, RANGELOW=HTHIGH
    ///       * if HTOOB: one OOB line: PREFLEN, RANGELEN=0, RANGELOW=0
    /// </summary>
    internal static class HuffmanTableSegment
    {
        public static HuffmanParams Parse(byte[] data, int offset, int length)
        {
            if (length < 9)
                throw new InvalidOperationException("Huffman table segment too short");

            byte flags = data[offset];
            bool htOob = (flags & 0x01) != 0;
            int htps = ((flags >> 1) & 0x07) + 1;
            int htrs = ((flags >> 4) & 0x07) + 1;
            int htLow  = BigEndianI32(data, offset + 1);
            int htHigh = BigEndianI32(data, offset + 5);

            if (htLow >= htHigh)
                throw new InvalidOperationException(
                    $"Huffman table segment: HTLOW ({htLow}) must be < HTHIGH ({htHigh})");

            int linesStart = offset + 9;
            long linesBitLen = (long)(length - 9) * 8;
            var reader = new HuffmanBitReader(data, linesStart, length - 9);
            long bitsRead = 0;

            var lines = new List<HuffmanLine>();

            // Regular lines — repeat until we've covered [HTLOW, HTHIGH).
            int curRangeLow = htLow;
            while (curRangeLow < htHigh)
            {
                if (bitsRead + htps + htrs > linesBitLen)
                    throw new InvalidOperationException("Huffman table segment truncated mid-range-line");
                var prefLen = (int)reader.ReadBits(htps);
                var rangeLen = (int)reader.ReadBits(htrs);
                bitsRead += htps + htrs;
                lines.Add(new HuffmanLine(prefLen, rangeLen, curRangeLow));
                if (rangeLen >= 31)
                    throw new InvalidOperationException(
                        $"Huffman table segment: regular RANGELEN {rangeLen} would overflow");
                curRangeLow += 1 << rangeLen;
            }

            // LOWER line.
            if (bitsRead + htps > linesBitLen)
                throw new InvalidOperationException("Huffman table segment truncated before LOWER line");
            var lowerPrefLen = (int)reader.ReadBits(htps);
            bitsRead += htps;
            lines.Add(new HuffmanLine(lowerPrefLen, 32, htLow - 1));

            // UPPER line.
            if (bitsRead + htps > linesBitLen)
                throw new InvalidOperationException("Huffman table segment truncated before UPPER line");
            var upperPrefLen = (int)reader.ReadBits(htps);
            bitsRead += htps;
            lines.Add(new HuffmanLine(upperPrefLen, 32, htHigh));

            // OOB line (optional).
            if (htOob)
            {
                if (bitsRead + htps > linesBitLen)
                    throw new InvalidOperationException("Huffman table segment truncated before OOB line");
                var oobPrefLen = (int)reader.ReadBits(htps);
                lines.Add(new HuffmanLine(oobPrefLen, 0, 0));
            }

            return new HuffmanParams { HtOob = htOob, Lines = lines.ToArray() };
        }

        private static int BigEndianI32(byte[] data, int offset)
        {
            return (data[offset] << 24)
                 | (data[offset + 1] << 16)
                 | (data[offset + 2] << 8)
                 |  data[offset + 3];
        }
    }
}
