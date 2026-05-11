using System;

namespace JpegCodec.Segments;

// Canonical Huffman code table built from BITS+HUFFVAL per T.81 Annex C.2.
// Encodes the spec's HUFFCODE/HUFFSIZE arrays plus the decoder helpers
// MINCODE/MAXCODE/VALPTR from Annex F.2.2.3.
//
// Also provides an 8-bit fast lookup for codes whose canonical length is
// ≤8 bits — the common case in real images.
internal sealed class HuffmanCanonicalTable
{
    public byte[] Bits { get; }
    public byte[] Values { get; }

    // Per-entry canonical code and length, indexed by HUFFVAL position.
    public int[] HuffCode { get; }
    public byte[] HuffSize { get; }

    // Decoder helpers, 1-indexed in spec; we use 0-indexed (index i =
    // codes of length i+1, i in [0..15]).
    //   MinCode[i] = smallest code of length (i+1), or 0 if no such codes
    //   MaxCode[i] = largest code of length (i+1), or -1 if none
    //   ValPtr[i] = first HUFFVAL index for codes of length (i+1)
    public int[] MinCode { get; }
    public int[] MaxCode { get; }
    public int[] ValPtr { get; }

    // 256-entry 8-bit-prefix fast path. Each entry encodes either:
    //   * (length << 8) | symbol     for codes of length 1..8 that match
    //                                this prefix's top bits
    //   * 0                          if no code of length ≤8 matches the
    //                                prefix (fall back to slow path)
    public ushort[] FastLookup { get; }

    public const int FastLookupBits = 8;

    private HuffmanCanonicalTable(
        byte[] bits, byte[] values, int[] huffCode, byte[] huffSize,
        int[] minCode, int[] maxCode, int[] valPtr, ushort[] fastLookup)
    {
        Bits = bits;
        Values = values;
        HuffCode = huffCode;
        HuffSize = huffSize;
        MinCode = minCode;
        MaxCode = maxCode;
        ValPtr = valPtr;
        FastLookup = fastLookup;
    }

    public static HuffmanCanonicalTable Build(HuffmanTable table)
    {
        if (table is null) throw new ArgumentNullException(nameof(table));
        return Build(table.Bits, table.Values);
    }

    public static HuffmanCanonicalTable Build(byte[] bits, byte[] values)
    {
        if (bits is null) throw new ArgumentNullException(nameof(bits));
        if (values is null) throw new ArgumentNullException(nameof(values));
        if (bits.Length != 16)
            throw new ArgumentException("BITS must have exactly 16 entries.", nameof(bits));

        var totalSymbols = 0;
        for (var i = 0; i < 16; i++) totalSymbols += bits[i];
        if (totalSymbols != values.Length)
            throw new InvalidOperationException(
                $"Huffman BITS sum ({totalSymbols}) != HUFFVAL length ({values.Length}).");
        if (totalSymbols == 0)
            throw new InvalidOperationException("Huffman table has no entries.");

        var huffCode = new int[totalSymbols];
        var huffSize = new byte[totalSymbols];
        var minCode = new int[16];
        var maxCode = new int[16];
        var valPtr = new int[16];
        for (var i = 0; i < 16; i++)
        {
            minCode[i] = 0;
            maxCode[i] = -1;
            valPtr[i] = 0;
        }

        // T.81 §C.2 Figure C.1 / C.2 — generate canonical codes.
        var j = 0;
        var code = 0;
        for (var lengthIndex = 0; lengthIndex < 16; lengthIndex++)
        {
            int length = lengthIndex + 1;
            int count = bits[lengthIndex];
            if (count > 0)
            {
                valPtr[lengthIndex] = j;
                minCode[lengthIndex] = code;
                for (var k = 0; k < count; k++)
                {
                    huffCode[j] = code;
                    huffSize[j] = (byte)length;
                    code++;
                    j++;
                }
                maxCode[lengthIndex] = code - 1;
            }
            // Check oversubscription: code must fit in 'length' bits.
            // After we increment 'code', it can be at most 2^length (all
            // codes of this length used). Shifting again must not push it
            // beyond 2^16.
            if (code > 1 << length)
                throw new InvalidOperationException(
                    $"Huffman BITS oversubscribed at length {length}: code exceeded 2^{length}.");
            code <<= 1;
        }

        // 8-bit fast lookup.
        var fast = new ushort[1 << FastLookupBits];
        for (var idx = 0; idx < totalSymbols; idx++)
        {
            byte len = huffSize[idx];
            if (len > FastLookupBits) continue;
            int prefix = huffCode[idx] << (FastLookupBits - len);
            int span = 1 << (FastLookupBits - len);
            var entry = (ushort)((len << 8) | values[idx]);
            for (var p = 0; p < span; p++)
                fast[prefix + p] = entry;
        }

        return new HuffmanCanonicalTable(bits, values, huffCode, huffSize, minCode, maxCode, valPtr, fast);
    }

    // Extract the symbol from a fast-lookup entry. Returns 0 length if the
    // prefix does not match any code ≤8 bits.
    public static (byte length, byte symbol) DecodeFastEntry(ushort entry)
        => ((byte)(entry >> 8), (byte)(entry & 0xFF));
}
