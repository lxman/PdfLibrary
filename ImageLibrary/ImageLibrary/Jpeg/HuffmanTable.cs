namespace ImageLibrary.Jpeg;

/// <summary>
/// A built Huffman table ready for decoding.
/// Uses a two-level lookup table for fast decoding.
/// </summary>
internal class HuffmanTable
{
    // First-level lookup table (8-bit lookahead)
    // Entry format: (symbol << 8) | codeLength, or 0 if code is longer than 8 bits
    private readonly ushort[] _lookupTable = new ushort[256];

    // For codes longer than 8 bits, we use the traditional tree-walk approach
    private readonly int[] _maxCode = new int[17];   // Max code value for each length
    private readonly int[] _valPtr = new int[17];    // Index into symbols for each length
    private readonly byte[] _symbols;                 // Symbol values in order

    /// <summary>
    /// Builds a Huffman table from the specification read from DHT marker.
    /// </summary>
    public HuffmanTable(HuffmanTableSpec spec)
    {
        _symbols = spec.Symbols;

        // Generate Huffman codes using the algorithm from ITU-T T.81 Annex C
        byte[] huffSize = GenerateHuffmanSizes(spec.CodeCounts);
        ushort[] huffCode = GenerateHuffmanCodes(huffSize);

        // Build the lookup tables
        BuildLookupTable(spec.CodeCounts, huffCode);
        BuildDecodeTables(spec.CodeCounts, huffCode);
    }

    /// <summary>
    /// Generates the list of code sizes (lengths) for each symbol.
    /// JPEG spec: Figure C.1 - Generation of table of Huffman code sizes
    /// </summary>
    private static byte[] GenerateHuffmanSizes(byte[] codeCounts)
    {
        // Count total symbols
        var total = 0;
        for (var i = 0; i < 16; i++)
        {
            total += codeCounts[i];
        }

        var huffSize = new byte[total + 1]; // +1 for terminating 0
        var k = 0;

        for (var i = 1; i <= 16; i++)
        {
            for (var j = 0; j < codeCounts[i - 1]; j++)
            {
                huffSize[k++] = (byte)i;
            }
        }

        huffSize[k] = 0; // Terminating zero

        return huffSize;
    }

    /// <summary>
    /// Generates the Huffman codes from the sizes.
    /// JPEG spec: Figure C.2 - Generation of table of Huffman codes
    /// </summary>
    private static ushort[] GenerateHuffmanCodes(byte[] huffSize)
    {
        var huffCode = new ushort[huffSize.Length];

        var k = 0;
        var code = 0;
        int si = huffSize[0];

        while (huffSize[k] != 0)
        {
            while (huffSize[k] == si)
            {
                huffCode[k++] = (ushort)code;
                code++;
            }

            // Shift code to next bit length
            code <<= 1;
            si++;
        }

        return huffCode;
    }

    /// <summary>
    /// Builds the first-level 8-bit lookup table for fast decoding.
    /// </summary>
    private void BuildLookupTable(byte[] codeCounts, ushort[] huffCode)
    {
        var symbolIndex = 0;

        for (var codeLen = 1; codeLen <= 8; codeLen++)
        {
            for (var i = 0; i < codeCounts[codeLen - 1]; i++)
            {
                // Get the code and symbol for this entry
                ushort code = huffCode[symbolIndex];
                byte symbol = _symbols[symbolIndex];

                // Fill all lookup entries that start with this code
                // If code is 'codeLen' bits, we need to fill 2^(8-codeLen) entries
                int shift = 8 - codeLen;
                int baseIndex = code << shift;
                int count = 1 << shift;

                for (var j = 0; j < count; j++)
                {
                    // Store symbol in high byte, length in low byte
                    _lookupTable[baseIndex + j] = (ushort)((symbol << 8) | codeLen);
                }

                symbolIndex++;
            }
        }

        // Skip symbols with code length > 8 (handled by slow path)
        // But we still need to count them to track symbolIndex
        for (var codeLen = 9; codeLen <= 16; codeLen++)
        {
            symbolIndex += codeCounts[codeLen - 1];
        }
    }

    /// <summary>
    /// Builds tables for decoding codes longer than 8 bits.
    /// JPEG spec: Figure F.15 - Decoder table generation
    /// </summary>
    private void BuildDecodeTables(byte[] codeCounts, ushort[] huffCode)
    {
        var symbolIndex = 0;

        for (var i = 1; i <= 16; i++)
        {
            if (codeCounts[i - 1] != 0)
            {
                _valPtr[i] = symbolIndex;
                _maxCode[i] = huffCode[symbolIndex + codeCounts[i - 1] - 1];
                symbolIndex += codeCounts[i - 1];
            }
            else
            {
                _maxCode[i] = -1; // No codes of this length
            }
        }
    }

    /// <summary>
    /// Decodes a single symbol from the bit reader.
    /// Returns the decoded symbol value.
    /// </summary>
    public byte DecodeSymbol(BitReader reader)
    {
        // Try fast 8-bit lookup first
        int peek = reader.PeekBits(8);
        ushort entry = _lookupTable[peek];

        if ((entry & 0xFF) != 0)
        {
            // Fast path: code was 8 bits or less
            int length = entry & 0xFF;
            reader.SkipBits(length);
            return (byte)(entry >> 8);
        }

        // Slow path: code is longer than 8 bits
        return DecodeSymbolSlow(reader);
    }

    /// <summary>
    /// Slow path for decoding symbols with codes longer than 8 bits.
    /// </summary>
    private byte DecodeSymbolSlow(BitReader reader)
    {
        int code = reader.PeekBits(8);
        reader.SkipBits(8);

        var codeLen = 8;

        while (code > _maxCode[codeLen] || _maxCode[codeLen] == -1)
        {
            code = (code << 1) | reader.ReadBit();
            codeLen++;

            if (codeLen > 16)
            {
                throw new JpegException("Invalid Huffman code - exceeds 16 bits");
            }
        }

        int index = _valPtr[codeLen] + code - (_maxCode[codeLen] - (GetCodeCount(codeLen) - 1));
        return _symbols[index];
    }

    private int GetCodeCount(int length)
    {
        // Count codes of exactly this length by looking at valPtr differences
        if (length >= 16)
        {
            return _symbols.Length - _valPtr[length];
        }

        // Find next length that has codes
        for (int i = length + 1; i <= 16; i++)
        {
            if (_maxCode[i] != -1)
            {
                return _valPtr[i] - _valPtr[length];
            }
        }

        return _symbols.Length - _valPtr[length];
    }

    /// <summary>
    /// Gets the symbols array for testing/debugging.
    /// </summary>
    public byte[] Symbols => _symbols;

    /// <summary>
    /// Gets the lookup table for testing/debugging.
    /// </summary>
    public ushort[] LookupTable => _lookupTable;
}
