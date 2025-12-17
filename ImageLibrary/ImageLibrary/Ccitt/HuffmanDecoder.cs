namespace ImageLibrary.Ccitt;

/// <summary>
/// Decodes Huffman-encoded run lengths for CCITT compression.
/// Uses a lookup tree for efficient decoding.
/// </summary>
public class HuffmanDecoder
{
    /// <summary>
    /// Special value indicating the code is a makeup code (add to further codes).
    /// </summary>
    public const int MakeupFlag = 0x10000;

    /// <summary>
    /// Special value indicating EOL was found.
    /// </summary>
    public const int EolValue = -1;

    /// <summary>
    /// Special value indicating an invalid code.
    /// </summary>
    public const int InvalidCode = -2;

    /// <summary>
    /// Special value indicating end of data.
    /// </summary>
    public const int EndOfData = -3;

    // Decode tree nodes
    // Each node has two children (for bit 0 and bit 1)
    // Leaf nodes store the run length (or special values)
    private class DecodeNode
    {
        public DecodeNode? Zero;
        public DecodeNode? One;
        public int Value = InvalidCode;
        public bool IsLeaf => Zero == null && One == null;
    }

    private readonly DecodeNode _whiteRoot;
    private readonly DecodeNode _blackRoot;
    private readonly DecodeNode _twoDimensionalRoot;

    /// <summary>
    /// Creates a new Huffman decoder with pre-built decode trees.
    /// </summary>
    public HuffmanDecoder()
    {
        _whiteRoot = new DecodeNode();
        _blackRoot = new DecodeNode();
        _twoDimensionalRoot = new DecodeNode();

        BuildWhiteTree();
        BuildBlackTree();
        Build2DTree();
    }

    private void AddCode(DecodeNode root, int code, int bitLength, int value)
    {
        DecodeNode? node = root;
        for (int i = bitLength - 1; i >= 0; i--)
        {
            int bit = (code >> i) & 1;
            if (bit == 0)
            {
                if (node.Zero == null)
                    node.Zero = new DecodeNode();
                node = node.Zero;
            }
            else
            {
                if (node.One == null)
                    node.One = new DecodeNode();
                node = node.One;
            }
        }
        node.Value = value;
    }

    private void BuildWhiteTree()
    {
        // Terminating codes (0-63)
        for (var i = 0; i < HuffmanTables.WhiteTerminatingCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.WhiteTerminatingCodes[i];
            AddCode(_whiteRoot, code.Code, code.BitLength, i);
        }

        // Makeup codes (64, 128, 192, ... 1728)
        for (var i = 0; i < HuffmanTables.WhiteMakeupCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.WhiteMakeupCodes[i];
            int runLength = (i + 1) * 64;
            AddCode(_whiteRoot, code.Code, code.BitLength, runLength | MakeupFlag);
        }

        // Extended makeup codes (1792-2560)
        for (var i = 0; i < HuffmanTables.ExtendedMakeupCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.ExtendedMakeupCodes[i];
            int runLength = 1792 + (i * 64);
            AddCode(_whiteRoot, code.Code, code.BitLength, runLength | MakeupFlag);
        }

        // EOL code
        AddCode(_whiteRoot, CcittConstants.EolCode, CcittConstants.EolBits, EolValue);
    }

    private void BuildBlackTree()
    {
        // Terminating codes (0-63)
        for (var i = 0; i < HuffmanTables.BlackTerminatingCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.BlackTerminatingCodes[i];
            AddCode(_blackRoot, code.Code, code.BitLength, i);
        }

        // Makeup codes (64, 128, 192, ... 1728)
        for (var i = 0; i < HuffmanTables.BlackMakeupCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.BlackMakeupCodes[i];
            int runLength = (i + 1) * 64;
            AddCode(_blackRoot, code.Code, code.BitLength, runLength | MakeupFlag);
        }

        // Extended makeup codes (1792-2560) - same for black and white
        for (var i = 0; i < HuffmanTables.ExtendedMakeupCodes.Length; i++)
        {
            HuffmanTables.HuffmanCode code = HuffmanTables.ExtendedMakeupCodes[i];
            int runLength = 1792 + (i * 64);
            AddCode(_blackRoot, code.Code, code.BitLength, runLength | MakeupFlag);
        }

        // EOL code
        AddCode(_blackRoot, CcittConstants.EolCode, CcittConstants.EolBits, EolValue);
    }

    private void Build2DTree()
    {
        // Pass mode: 0001
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.PassCode, TwoDimensionalCodes.PassCodeBits, (int)TwoDimensionalMode.Pass);

        // Horizontal mode: 001
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.HorizontalCode, TwoDimensionalCodes.HorizontalCodeBits, (int)TwoDimensionalMode.Horizontal);

        // Vertical modes
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.V0Code, TwoDimensionalCodes.Vertical.V0Bits, (int)TwoDimensionalMode.Vertical0);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VR1Code, TwoDimensionalCodes.Vertical.VR1Bits, (int)TwoDimensionalMode.VerticalR1);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VR2Code, TwoDimensionalCodes.Vertical.VR2Bits, (int)TwoDimensionalMode.VerticalR2);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VR3Code, TwoDimensionalCodes.Vertical.VR3Bits, (int)TwoDimensionalMode.VerticalR3);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VL1Code, TwoDimensionalCodes.Vertical.VL1Bits, (int)TwoDimensionalMode.VerticalL1);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VL2Code, TwoDimensionalCodes.Vertical.VL2Bits, (int)TwoDimensionalMode.VerticalL2);
        AddCode(_twoDimensionalRoot, TwoDimensionalCodes.Vertical.VL3Code, TwoDimensionalCodes.Vertical.VL3Bits, (int)TwoDimensionalMode.VerticalL3);

        // EOL for Group 3 2D (after EOL, there's a tag bit)
        AddCode(_twoDimensionalRoot, CcittConstants.EolCode, CcittConstants.EolBits, (int)TwoDimensionalMode.Eol);
    }

    /// <summary>
    /// Decodes a white run length from the bit reader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <returns>The run length, or a special value (EOL, InvalidCode, EndOfData).</returns>
    public int DecodeWhiteRunLength(CcittBitReader reader)
    {
        return DecodeRunLength(reader, _whiteRoot);
    }

    /// <summary>
    /// Decodes a black run length from the bit reader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <returns>The run length, or a special value (EOL, InvalidCode, EndOfData).</returns>
    public int DecodeBlackRunLength(CcittBitReader reader)
    {
        return DecodeRunLength(reader, _blackRoot);
    }

    /// <summary>
    /// Decodes a run length (handles makeup codes automatically).
    /// </summary>
    private int DecodeRunLength(CcittBitReader reader, DecodeNode root)
    {
        var totalRunLength = 0;

        while (true)
        {
            int value = DecodeValue(reader, root);

            if (value < 0)
            {
                // Special value (EOL, invalid, end of data)
                return value;
            }

            if ((value & MakeupFlag) != 0)
            {
                // Makeup code - add to total and continue reading
                totalRunLength += (value & ~MakeupFlag);
            }
            else
            {
                // Terminating code - add to total and return
                return totalRunLength + value;
            }
        }
    }

    /// <summary>
    /// Decodes a single Huffman value from the bit reader.
    /// </summary>
    private int DecodeValue(CcittBitReader reader, DecodeNode root)
    {
        DecodeNode? node = root;

        while (!node.IsLeaf)
        {
            int bit = reader.ReadBit();
            if (bit < 0)
                return EndOfData;

            node = bit == 0 ? node.Zero : node.One;

            if (node == null)
                return InvalidCode;
        }

        return node.Value;
    }

    /// <summary>
    /// Decodes a 2D mode code from the bit reader.
    /// </summary>
    /// <param name="reader">The bit reader.</param>
    /// <returns>The 2D mode.</returns>
    public TwoDimensionalMode Decode2DMode(CcittBitReader reader)
    {
        int startPos = reader.Position;
        int value = DecodeValue(reader, _twoDimensionalRoot);

        if (value < 0)
        {
            return TwoDimensionalMode.Error;
        }

        return (TwoDimensionalMode)value;
    }
}

/// <summary>
/// Two-dimensional encoding modes for Group 3 2D and Group 4.
/// </summary>
public enum TwoDimensionalMode
{
    /// <summary>Pass mode - b2 is left of a1.</summary>
    Pass = 0,
    /// <summary>Horizontal mode - encode a0a1 and a1a2 run lengths.</summary>
    Horizontal = 1,
    /// <summary>Vertical mode - a1 is directly under b1.</summary>
    Vertical0 = 2,
    /// <summary>Vertical mode - a1 is 1 pixel right of b1.</summary>
    VerticalR1 = 3,
    /// <summary>Vertical mode - a1 is 2 pixels right of b1.</summary>
    VerticalR2 = 4,
    /// <summary>Vertical mode - a1 is 3 pixels right of b1.</summary>
    VerticalR3 = 5,
    /// <summary>Vertical mode - a1 is 1 pixel left of b1.</summary>
    VerticalL1 = 6,
    /// <summary>Vertical mode - a1 is 2 pixels left of b1.</summary>
    VerticalL2 = 7,
    /// <summary>Vertical mode - a1 is 3 pixels left of b1.</summary>
    VerticalL3 = 8,
    /// <summary>End of line marker.</summary>
    Eol = 9,
    /// <summary>Error or invalid code.</summary>
    Error = -1
}