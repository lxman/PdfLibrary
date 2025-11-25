#nullable enable

using System;
using System.Collections.Generic;

namespace Compressors.Ccitt
{
    /// <summary>
    /// Decodes CCITT Fax compressed image data (Group 3 and Group 4).
    /// </summary>
    public class CcittDecoder
    {
        private readonly CcittOptions _options;
        private readonly HuffmanDecoder _huffman;

        /// <summary>
        /// Creates a new CCITT decoder with the specified options.
        /// </summary>
        /// <param name="options">The decoding options.</param>
        public CcittDecoder(CcittOptions? options = null)
        {
            _options = options ?? CcittOptions.PdfDefault;
            _huffman = new HuffmanDecoder();
        }

        /// <summary>
        /// Decodes CCITT compressed data to raw bitmap data.
        /// </summary>
        /// <param name="data">The compressed data.</param>
        /// <returns>Decoded bitmap data (1 bit per pixel, packed into bytes).</returns>
        public byte[] Decode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<byte>();

            var reader = new CcittBitReader(data);
            var lines = new List<byte[]>();

            // Calculate bytes per row (rounded up to byte boundary)
            int bytesPerRow = (_options.Width + 7) / 8;

            // For 2D modes, we need to track the reference line
            byte[]? referenceLine = null;

            // Group 4 starts with an imaginary white reference line
            if (_options.Group == CcittGroup.Group4)
            {
                referenceLine = new byte[bytesPerRow];
                // All zeros = all white (in standard CCITT, 0 = white)
            }

            int rowCount = 0;
            int maxRows = _options.Height > 0 ? _options.Height : int.MaxValue;

            // Skip initial EOL if present and required
            if (_options.EndOfLine && _options.Group != CcittGroup.Group4)
            {
                SkipEolIfPresent(reader);
            }

            while (rowCount < maxRows && !reader.IsAtEnd)
            {
                if (_options.EncodedByteAlign)
                {
                    reader.AlignToByte();
                }

                byte[]? row = null;

                switch (_options.Group)
                {
                    case CcittGroup.Group3OneDimensional:
                        row = DecodeGroup3_1DRow(reader);
                        break;

                    case CcittGroup.Group3TwoDimensional:
                        // In Group 3 2D, EOL is followed by a tag bit
                        // 1 = 1D encoded line, 0 = 2D encoded line
                        bool is1D = true;
                        if (rowCount > 0 && _options.K > 0)
                        {
                            // Check if this should be a 2D line
                            // Every K lines, one is 1D, the rest are 2D
                            is1D = (rowCount % _options.K) == 0;
                        }

                        if (is1D || referenceLine == null)
                        {
                            row = DecodeGroup3_1DRow(reader);
                        }
                        else
                        {
                            row = Decode2DRow(reader, referenceLine);
                        }
                        break;

                    case CcittGroup.Group4:
                        row = Decode2DRow(reader, referenceLine!);
                        break;
                }

                if (row == null)
                {
                    // Check for end of block
                    if (CheckEndOfBlock(reader))
                        break;

                    // Error - try to continue if damaged rows allowed
                    if (_options.DamagedRowsBeforeError > 0)
                    {
                        _options.DamagedRowsBeforeError--;
                        row = new byte[bytesPerRow]; // White row
                    }
                    else
                    {
                        break;
                    }
                }

                lines.Add(row);
                referenceLine = row;
                rowCount++;
            }

            // Combine all rows into output
            return CombineRows(lines, bytesPerRow);
        }

        /// <summary>
        /// Decodes a single row using Group 3 1D (Modified Huffman).
        /// </summary>
        private byte[]? DecodeGroup3_1DRow(CcittBitReader reader)
        {
            int bytesPerRow = (_options.Width + 7) / 8;
            var row = new byte[bytesPerRow];
            int pixelPosition = 0;
            bool isWhite = true; // Always start with white

            while (pixelPosition < _options.Width)
            {
                int runLength = isWhite
                    ? _huffman.DecodeWhiteRunLength(reader)
                    : _huffman.DecodeBlackRunLength(reader);

                if (runLength == HuffmanDecoder.EolValue)
                {
                    // Unexpected EOL - pad rest of line with current color
                    break;
                }

                if (runLength < 0)
                {
                    // Error or end of data
                    return null;
                }

                // Clamp run length to remaining pixels
                if (pixelPosition + runLength > _options.Width)
                {
                    runLength = _options.Width - pixelPosition;
                }

                // Fill the run
                if (!isWhite) // Black pixels
                {
                    FillBlackRun(row, pixelPosition, runLength);
                }

                pixelPosition += runLength;
                isWhite = !isWhite;
            }

            // Check for EOL if required
            if (_options.EndOfLine)
            {
                SkipEolIfPresent(reader);
            }

            return row;
        }

        /// <summary>
        /// Decodes a single row using 2D encoding (Group 3 2D or Group 4).
        /// </summary>
        private byte[]? Decode2DRow(CcittBitReader reader, byte[] referenceLine)
        {
            int bytesPerRow = (_options.Width + 7) / 8;
            var row = new byte[bytesPerRow];
            int a0 = -1; // Current position (-1 means before the line)
            bool a0Color = false; // false = white, true = black (at position a0)

            while (a0 < _options.Width)
            {
                var mode = _huffman.Decode2DMode(reader);

                switch (mode)
                {
                    case TwoDimensionalMode.Pass:
                        // Pass mode: a0 moves to below b2
                        int b1Pass = FindB1(referenceLine, a0, a0Color);
                        int b2Pass = FindB2(referenceLine, b1Pass);
                        a0 = b2Pass;
                        // Color does NOT change in pass mode
                        break;

                    case TwoDimensionalMode.Horizontal:
                        // Horizontal mode: read two run lengths (a0a1 and a1a2)
                        // First run color is the color AFTER a0 (opposite of a0Color)
                        // If a0 < 0 (start of line), first run is white
                        bool firstRunIsBlack = a0 < 0 ? false : !a0Color;

                        // Read first run (a0a1)
                        int run1 = firstRunIsBlack
                            ? _huffman.DecodeBlackRunLength(reader)
                            : _huffman.DecodeWhiteRunLength(reader);

                        if (run1 < 0) return null;

                        // Read second run (a1a2) - opposite color
                        int run2 = firstRunIsBlack
                            ? _huffman.DecodeWhiteRunLength(reader)
                            : _huffman.DecodeBlackRunLength(reader);

                        if (run2 < 0) return null;

                        // Fill runs
                        int pos = a0 < 0 ? 0 : a0;

                        // First run
                        if (firstRunIsBlack)
                        {
                            FillBlackRun(row, pos, run1);
                        }
                        pos += run1;

                        // Second run (opposite color)
                        if (!firstRunIsBlack)
                        {
                            FillBlackRun(row, pos, run2);
                        }
                        pos += run2;

                        a0 = pos;
                        // After horizontal mode, a0Color is the color of the last run (second run)
                        // Second run is opposite of first run
                        a0Color = !firstRunIsBlack;
                        break;

                    case TwoDimensionalMode.Vertical0:
                    case TwoDimensionalMode.VerticalR1:
                    case TwoDimensionalMode.VerticalR2:
                    case TwoDimensionalMode.VerticalR3:
                    case TwoDimensionalMode.VerticalL1:
                    case TwoDimensionalMode.VerticalL2:
                    case TwoDimensionalMode.VerticalL3:
                        int offset = GetVerticalOffset(mode);
                        int b1Vert = FindB1(referenceLine, a0, a0Color);
                        int a1 = b1Vert + offset;

                        // a1 must be valid
                        if (a1 < 0) a1 = 0;
                        if (a1 > _options.Width) a1 = _options.Width;

                        // Vertical mode: a1 is a changing element
                        // The run from a0 to a1-1 has a certain color:
                        // - If a0 < 0 (start of line), line starts white
                        // - Otherwise, the run color is OPPOSITE of a0Color
                        //   (because a0 was a changing element, so color flipped there)
                        //
                        // Actually, a0Color represents the color AFTER the change at a0
                        // So the run from a0 to a1-1 has the same color as a0Color
                        // But at start (a0=-1), we haven't had a change yet, so first run is white
                        bool runColorIsBlack = a0 < 0 ? false : a0Color;

                        int fillStart = a0 < 0 ? 0 : a0;
                        int fillLength = a1 - fillStart;

                        if (runColorIsBlack && fillLength > 0)
                        {
                            FillBlackRun(row, fillStart, fillLength);
                        }

                        // After vertical mode:
                        // - a0 moves to a1 (the new changing element)
                        // - a0Color becomes the OPPOSITE (color changed at a1)
                        a0 = a1;
                        a0Color = !runColorIsBlack;
                        break;

                    case TwoDimensionalMode.Eol:
                        // End of line
                        return row;

                    case TwoDimensionalMode.Error:
                    default:
                        return null;
                }
            }

            return row;
        }

        /// <summary>
        /// Finds b1 - the first changing element on the reference line to the right of a0
        /// that has the opposite color of a0.
        /// </summary>
        private int FindB1(byte[] referenceLine, int a0, bool a0Color)
        {
            int startPos = a0 < 0 ? 0 : a0 + 1;
            bool searchForBlack = !a0Color;

            // First, skip pixels of the same color as a0
            // Then find the first pixel of opposite color

            for (int pos = startPos; pos < _options.Width; pos++)
            {
                bool pixelColor = GetPixel(referenceLine, pos);
                if (pos == startPos)
                {
                    // If starting pixel is already opposite color, find next change
                    if (pixelColor == searchForBlack)
                    {
                        // This is b1
                        return pos;
                    }
                }
                else
                {
                    bool prevColor = GetPixel(referenceLine, pos - 1);
                    if (pixelColor != prevColor && pixelColor == searchForBlack)
                    {
                        return pos;
                    }
                }
            }

            // b1 is at end of line if not found
            return _options.Width;
        }

        /// <summary>
        /// Finds b2 - the next changing element after b1.
        /// </summary>
        private int FindB2(byte[] referenceLine, int b1)
        {
            if (b1 >= _options.Width)
                return _options.Width;

            bool b1Color = GetPixel(referenceLine, b1);

            for (int pos = b1 + 1; pos < _options.Width; pos++)
            {
                bool pixelColor = GetPixel(referenceLine, pos);
                if (pixelColor != b1Color)
                {
                    return pos;
                }
            }

            return _options.Width;
        }

        /// <summary>
        /// Gets the pixel value at a position (false = white, true = black).
        /// </summary>
        private bool GetPixel(byte[] row, int position)
        {
            if (position < 0 || position >= _options.Width)
                return false; // White for out of bounds

            int byteIndex = position / 8;
            int bitIndex = 7 - (position % 8); // MSB first

            if (byteIndex >= row.Length)
                return false;

            bool isSet = ((row[byteIndex] >> bitIndex) & 1) != 0;

            // Standard CCITT: 0 = white, 1 = black
            // But BlackIs1 option can invert this
            return _options.BlackIs1 ? !isSet : isSet;
        }

        /// <summary>
        /// Fills a run of black pixels.
        /// </summary>
        private void FillBlackRun(byte[] row, int start, int length)
        {
            if (length <= 0) return;

            byte fillValue = _options.BlackIs1 ? (byte)0 : (byte)0xFF;
            byte clearValue = _options.BlackIs1 ? (byte)0xFF : (byte)0;

            for (int i = 0; i < length; i++)
            {
                int pos = start + i;
                if (pos >= _options.Width) break;

                int byteIndex = pos / 8;
                int bitIndex = 7 - (pos % 8);

                if (byteIndex < row.Length)
                {
                    if (_options.BlackIs1)
                    {
                        row[byteIndex] &= (byte)~(1 << bitIndex); // Clear bit for black
                    }
                    else
                    {
                        row[byteIndex] |= (byte)(1 << bitIndex); // Set bit for black
                    }
                }
            }
        }

        /// <summary>
        /// Gets the vertical offset for a vertical mode.
        /// </summary>
        private int GetVerticalOffset(TwoDimensionalMode mode)
        {
            switch (mode)
            {
                case TwoDimensionalMode.Vertical0: return 0;
                case TwoDimensionalMode.VerticalR1: return 1;
                case TwoDimensionalMode.VerticalR2: return 2;
                case TwoDimensionalMode.VerticalR3: return 3;
                case TwoDimensionalMode.VerticalL1: return -1;
                case TwoDimensionalMode.VerticalL2: return -2;
                case TwoDimensionalMode.VerticalL3: return -3;
                default: return 0;
            }
        }

        /// <summary>
        /// Checks for end of block marker.
        /// </summary>
        private bool CheckEndOfBlock(CcittBitReader reader)
        {
            if (!_options.EndOfBlock)
                return false;

            // Group 4: EOFB is two consecutive EOL codes
            // Group 3: RTC is six consecutive EOL codes
            int eolsNeeded = _options.Group == CcittGroup.Group4 ? 2 : 6;

            // Check if we can see the pattern
            int bitsNeeded = eolsNeeded * CcittConstants.EolBits;
            if (reader.BitsRemaining < bitsNeeded)
                return reader.IsAtEnd;

            // This is a simplified check - real implementation would be more thorough
            return reader.IsAtEnd;
        }

        /// <summary>
        /// Skips EOL pattern if present.
        /// </summary>
        private void SkipEolIfPresent(CcittBitReader reader)
        {
            // Look for 11+ zeros followed by a 1
            int startPos = reader.Position;
            int zeros = 0;

            while (!reader.IsAtEnd && zeros < 12)
            {
                int bit = reader.ReadBit();
                if (bit == 0)
                {
                    zeros++;
                }
                else if (bit == 1 && zeros >= 11)
                {
                    // Found EOL
                    return;
                }
                else
                {
                    // Not an EOL - restore position
                    reader.Seek(startPos);
                    return;
                }
            }
        }

        /// <summary>
        /// Combines rows into a single byte array.
        /// </summary>
        private byte[] CombineRows(List<byte[]> rows, int bytesPerRow)
        {
            var result = new byte[rows.Count * bytesPerRow];
            for (int i = 0; i < rows.Count; i++)
            {
                Array.Copy(rows[i], 0, result, i * bytesPerRow, Math.Min(rows[i].Length, bytesPerRow));
            }
            return result;
        }
    }
}
