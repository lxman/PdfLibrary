using System;

namespace ImageLibrary.Ccitt;

/// <summary>
/// Encodes bitmap data using CCITT Fax compression (Group 3 and Group 4).
/// </summary>
public class CcittEncoder
{
    private readonly CcittOptions _options;

    /// <summary>
    /// Creates a new CCITT encoder with the specified options.
    /// </summary>
    /// <param name="options">The encoding options.</param>
    public CcittEncoder(CcittOptions? options = null)
    {
        _options = options ?? CcittOptions.PdfDefault;
    }

    /// <summary>
    /// Encodes raw bitmap data using CCITT compression.
    /// </summary>
    /// <param name="data">Raw bitmap data (1 bit per pixel, packed into bytes, MSB first).</param>
    /// <param name="height">The image height in pixels.</param>
    /// <returns>CCITT compressed data.</returns>
    public byte[] Encode(byte[] data, int height)
    {
        if (data == null || data.Length == 0)
            return [];

        var writer = new CcittBitWriter();
        int bytesPerRow = (_options.Width + 7) / 8;

        // For 2D encoding, we need a reference line
        byte[]? referenceLine = null;

        // Group 4 starts with an imaginary white reference line
        if (_options.Group == CcittGroup.Group4 || _options.Group == CcittGroup.Group3TwoDimensional)
        {
            referenceLine = new byte[bytesPerRow];
            // BlackIs1=true: 1=black, 0=white, so all zeros = all white (default) ✓
            // BlackIs1=false: 0=black, 1=white, so we need all 1s for white
            if (!_options.BlackIs1)
            {
                for (var i = 0; i < bytesPerRow; i++)
                    referenceLine[i] = 0xFF;
            }
        }

        for (var row = 0; row < height; row++)
        {
            if (_options.EncodedByteAlign)
            {
                writer.AlignToByte();
            }

            // Extract current row
            var currentRow = new byte[bytesPerRow];
            int srcOffset = row * bytesPerRow;
            int copyLength = Math.Min(bytesPerRow, data.Length - srcOffset);
            if (copyLength > 0)
            {
                Array.Copy(data, srcOffset, currentRow, 0, copyLength);
            }

            switch (_options.Group)
            {
                case CcittGroup.Group3OneDimensional:
                    if (_options.EndOfLine)
                    {
                        writer.WriteEol();
                    }
                    EncodeGroup3_1DRow(writer, currentRow);
                    break;

                case CcittGroup.Group3TwoDimensional:
                    if (_options.EndOfLine)
                    {
                        writer.WriteEol();
                        // Write tag bit: 1 = 1D, 0 = 2D
                        bool is1D = (row % _options.K) == 0;
                        writer.WriteBit(is1D ? 1 : 0);

                        if (is1D || referenceLine == null)
                        {
                            EncodeGroup3_1DRow(writer, currentRow);
                        }
                        else
                        {
                            Encode2DRow(writer, currentRow, referenceLine);
                        }
                    }
                    else
                    {
                        // Without EOL markers
                        bool is1D = (row % _options.K) == 0;
                        if (is1D || referenceLine == null)
                        {
                            EncodeGroup3_1DRow(writer, currentRow);
                        }
                        else
                        {
                            Encode2DRow(writer, currentRow, referenceLine);
                        }
                    }
                    break;

                case CcittGroup.Group4:
                    Encode2DRow(writer, currentRow, referenceLine!);
                    break;
            }

            referenceLine = currentRow;
        }

        // Write end of block
        if (_options.EndOfBlock)
        {
            if (_options.Group == CcittGroup.Group4)
            {
                writer.WriteEofb();
            }
            else
            {
                writer.WriteRtc();
            }
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Encodes a row using Group 3 1D (Modified Huffman).
    /// </summary>
    private void EncodeGroup3_1DRow(CcittBitWriter writer, byte[] row)
    {
        var position = 0;
        var isWhite = true; // Always start with white

        while (position < _options.Width)
        {
            int runLength = GetRunLength(row, position, isWhite);
            writer.WriteRunLength(runLength, isWhite);
            position += runLength;
            isWhite = !isWhite;
        }
    }

    /// <summary>
    /// Encodes a row using 2D encoding (Group 3 2D or Group 4).
    /// </summary>
    private void Encode2DRow(CcittBitWriter writer, byte[] currentRow, byte[] referenceRow)
    {
        int a0 = -1;
        var a0Color = false; // White

        while (a0 < _options.Width)
        {
            // Find a1 (first changing element after a0 on current line)
            int a1 = FindChangingElement(currentRow, a0, a0Color);

            // Find a2 (next changing element after a1 on current line)
            int a2 = FindChangingElement(currentRow, a1, !a0Color);

            // Find b1 (first changing element on reference line to the right of a0, opposite color)
            int b1 = FindB1(referenceRow, a0, a0Color);

            // Find b2 (next changing element after b1 on reference line)
            int b2 = FindB2(referenceRow, b1);

            // Decide which mode to use
            if (b2 < a1)
            {
                // Pass mode: b2 is to the left of a1
                writer.WritePassMode();
                a0 = b2;
                // a0Color doesn't change in pass mode
            }
            else
            {
                int offset = a1 - b1;
                if (TwoDimensionalCodes.CanUseVerticalMode(offset))
                {
                    // Vertical mode
                    writer.WriteVerticalMode(offset);
                    a0 = a1;
                    a0Color = !a0Color; // Color changes at a1
                }
                else
                {
                    // Horizontal mode: encode two consecutive runs
                    writer.WriteHorizontalMode();

                    int run1 = a1 - (a0 < 0 ? 0 : a0);
                    int run2 = a2 - a1;

                    // run1 color matches a0Color (false=white, true=black)
                    bool run1IsWhite = !a0Color;

                    writer.WriteRunLength(run1, run1IsWhite);
                    writer.WriteRunLength(run2, !run1IsWhite);

                    a0 = a2;
                    // After a2, color returns to run1's color (since run2 is opposite)
                    a0Color = !run1IsWhite;
                }
            }
        }
    }

    /// <summary>
    /// Gets the length of a run of the specified color starting at the given position.
    /// </summary>
    private int GetRunLength(byte[] row, int start, bool isWhite)
    {
        var length = 0;

        for (int pos = start; pos < _options.Width; pos++)
        {
            bool pixelIsBlack = GetPixelColor(row, pos);
            bool pixelIsWhite = !pixelIsBlack;

            // Continue counting while pixel matches the target color
            if (pixelIsWhite == isWhite)
            {
                length++;
            }
            else
            {
                break;
            }
        }

        return length;
    }

    /// <summary>
    /// Finds the first changing element on the current line after position 'after'
    /// where color differs from currentColor.
    /// </summary>
    private int FindChangingElement(byte[] row, int after, bool currentColor)
    {
        int start = after < 0 ? 0 : after;

        for (int pos = start; pos < _options.Width; pos++)
        {
            bool pixelColor = GetPixelColor(row, pos);
            if (pixelColor != currentColor)
            {
                return pos;
            }
        }

        return _options.Width;
    }

    /// <summary>
    /// Finds b1 - the first changing element on the reference line to the right of a0
    /// that has the opposite color of a0.
    /// </summary>
    private int FindB1(byte[] referenceRow, int a0, bool a0Color)
    {
        int startPos = a0 < 0 ? 0 : a0 + 1;
        bool targetColor = !a0Color;

        for (int pos = startPos; pos < _options.Width; pos++)
        {
            bool pixelColor = GetPixelColor(referenceRow, pos);
            bool prevColor = pos > 0 ? GetPixelColor(referenceRow, pos - 1) : false;

            if (pos == startPos)
            {
                if (pixelColor == targetColor)
                {
                    return pos;
                }
            }
            else if (pixelColor != prevColor && pixelColor == targetColor)
            {
                return pos;
            }
        }

        return _options.Width;
    }

    /// <summary>
    /// Finds b2 - the next changing element after b1.
    /// </summary>
    private int FindB2(byte[] referenceRow, int b1)
    {
        if (b1 >= _options.Width)
            return _options.Width;

        bool b1Color = GetPixelColor(referenceRow, b1);

        for (int pos = b1 + 1; pos < _options.Width; pos++)
        {
            bool pixelColor = GetPixelColor(referenceRow, pos);
            if (pixelColor != b1Color)
            {
                return pos;
            }
        }

        return _options.Width;
    }

    /// <summary>
    /// Gets the raw bit value at a position.
    /// </summary>
    private bool GetPixelBit(byte[] row, int position)
    {
        if (position < 0 || position >= _options.Width)
            return false;

        int byteIndex = position / 8;
        int bitIndex = 7 - (position % 8);

        if (byteIndex >= row.Length)
            return false;

        return ((row[byteIndex] >> bitIndex) & 1) != 0;
    }

    /// <summary>
    /// Gets the pixel color at a position (false = white, true = black).
    /// </summary>
    private bool GetPixelColor(byte[] row, int position)
    {
        bool isSet = GetPixelBit(row, position);

        // BlackIs1=true: 1 bits are black, so isSet=true means black
        // BlackIs1=false: 0 bits are black (1=white), so isSet=true means white (not black)
        return _options.BlackIs1 ? isSet : !isSet;
    }
}