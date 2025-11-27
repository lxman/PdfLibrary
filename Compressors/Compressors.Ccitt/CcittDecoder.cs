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
                // BlackIs1=true: 1=black, 0=white, so all zeros = all white (default byte array is zeros) ✓
                // BlackIs1=false: 0=black, 1=white, so we need all 1s for white
                if (!_options.BlackIs1)
                {
                    for (int i = 0; i < bytesPerRow; i++)
                        referenceLine[i] = 0xFF;
                }
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

                Trace($"Row {rowCount}: starting at bit {reader.Position}");

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

                    // Debug: log error position
                    Console.WriteLine($"[CCITT DEBUG] Row {rowCount} decode failed at bit position {reader.Position}, bits remaining: {reader.BitsRemaining}");

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
            var row = CreateWhiteRow(bytesPerRow);
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
        /// Enables detailed tracing for debugging.
        /// </summary>
        public bool EnableTracing { get; set; }

        /// <summary>
        /// Traces a message if tracing is enabled.
        /// </summary>
        private void Trace(string message)
        {
            if (EnableTracing)
                Console.WriteLine(message);
        }

        /// <summary>
        /// Decodes a single row using 2D encoding (Group 3 2D or Group 4).
        /// </summary>
        private byte[]? Decode2DRow(CcittBitReader reader, byte[] referenceLine)
        {
            int bytesPerRow = (_options.Width + 7) / 8;
            var row = CreateWhiteRow(bytesPerRow);
            int a0 = -1; // Current position (-1 means before the line)
            bool a0Color = false; // false = white, true = black (at position a0)
            int modeCount = 0;

            while (a0 < _options.Width)
            {
                int preModeBitPos = reader.Position;
                var mode = _huffman.Decode2DMode(reader);
                modeCount++;
                Trace($"  Mode {modeCount}: {mode} at bit {preModeBitPos}, a0={a0}, a0Color={(a0Color ? "black" : "white")}");

                switch (mode)
                {
                    case TwoDimensionalMode.Pass:
                        // Pass mode: a0 moves to below b2
                        // The region from a0 to b2 has no changing element, so it's all a0Color
                        int b1Pass = FindB1(referenceLine, a0, a0Color);
                        int b2Pass = FindB2(referenceLine, b1Pass);

                        // Fill the region from a0 to b2 with a0Color
                        int passFillStart = a0 < 0 ? 0 : a0;
                        int passFillLength = b2Pass - passFillStart;
                        if (a0Color && passFillLength > 0)
                        {
                            FillBlackRun(row, passFillStart, passFillLength);
                        }

                        Trace($"    Pass: b1={b1Pass}, b2={b2Pass}, fill {(a0Color ? "black" : "white")} from {passFillStart} len {passFillLength}, a0 moves {a0} -> {b2Pass}");
                        a0 = b2Pass;
                        // Color does NOT change in pass mode
                        break;

                    case TwoDimensionalMode.Horizontal:
                        // Horizontal mode: read two run lengths (a0a1 and a1a2)
                        // First run color is the color at a0 (a0Color represents color at position a0)
                        // If a0 < 0 (start of line), first run is white
                        bool firstRunIsBlack = a0 < 0 ? false : a0Color;

                        // Read first run (a0a1)
                        int run1 = firstRunIsBlack
                            ? _huffman.DecodeBlackRunLength(reader)
                            : _huffman.DecodeWhiteRunLength(reader);

                        if (run1 < 0)
                        {
                            Trace($"    Horizontal: failed to read run1 (isBlack={firstRunIsBlack})");
                            return null;
                        }

                        // Read second run (a1a2) - opposite color
                        int run2 = firstRunIsBlack
                            ? _huffman.DecodeWhiteRunLength(reader)
                            : _huffman.DecodeBlackRunLength(reader);

                        if (run2 < 0)
                        {
                            Trace($"    Horizontal: failed to read run2 (isBlack={!firstRunIsBlack})");
                            return null;
                        }

                        Trace($"    Horizontal: {(firstRunIsBlack ? "black" : "white")}{run1} + {(!firstRunIsBlack ? "black" : "white")}{run2}, a0 moves {a0} -> {(a0 < 0 ? 0 : a0) + run1 + run2}");

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
                        // After horizontal mode, a0Color is the color of the NEXT run (starting at new a0)
                        // Horizontal fills: run1 (firstRunIsBlack) + run2 (!firstRunIsBlack)
                        // Next run is opposite of run2, which is same as firstRunIsBlack
                        a0Color = firstRunIsBlack;
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

                        Trace($"    Vertical{offset}: b1={b1Vert}, a1={a1}, fill {(runColorIsBlack ? "black" : "white")} from {fillStart} len {fillLength}, a0 moves {a0} -> {a1}");

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
                        Console.WriteLine($"[CCITT DEBUG] 2D decode error: mode={mode}, a0={a0}/{_options.Width}, a0Color={a0Color}, bitPos={preModeBitPos}, modes decoded this row={modeCount}");
                        return null;
                }
            }

            return row;
        }

        /// <summary>
        /// Finds b1 - the first changing element on the reference line to the right of a0
        /// that has the opposite color of a0.
        ///
        /// b1 is defined as: "The first changing element on the reference line to the right
        /// of a0 and of opposite colour to a0."
        ///
        /// A "changing element" is a position where the color CHANGES from the previous pixel.
        /// So b1 is the first TRANSITION TO the opposite color, not just any pixel of that color.
        /// </summary>
        private int FindB1(byte[] referenceLine, int a0, bool a0Color)
        {
            int startPos = a0 < 0 ? 0 : a0 + 1;
            bool targetColor = !a0Color; // The opposite color we're looking for

            // We need to find the first CHANGING ELEMENT to the target color after startPos.
            // A changing element is where color transitions. So we look for a position where:
            // - The pixel at that position is targetColor
            // - The pixel at the previous position is NOT targetColor (a transition occurred)

            // Special case: at startPos, check if it's a changing element by comparing to position before startPos
            if (startPos > 0)
            {
                bool prevPixel = GetPixel(referenceLine, startPos - 1);
                bool currPixel = GetPixel(referenceLine, startPos);
                if (EnableTracing && startPos >= 1960 && startPos <= 1970)
                {
                    Console.WriteLine($"      FindB1: startPos={startPos}, prevPixel={prevPixel}, currPixel={currPixel}, targetColor={targetColor}");
                }
                if (currPixel == targetColor && prevPixel != targetColor)
                {
                    // startPos is a changing element to targetColor
                    return startPos;
                }
            }
            else if (startPos == 0)
            {
                // At start of line, check if first pixel is the target color
                // (imaginary pixel before line is white, so if targetColor=black and pixel0=black, it's a change)
                bool firstPixel = GetPixel(referenceLine, 0);
                if (firstPixel == targetColor)
                {
                    return 0;
                }
            }

            // Scan forward looking for a transition TO targetColor
            for (int pos = startPos + 1; pos < _options.Width; pos++)
            {
                bool prevPixel = GetPixel(referenceLine, pos - 1);
                bool currPixel = GetPixel(referenceLine, pos);

                if (EnableTracing && pos >= 1960 && pos <= 1970)
                {
                    Console.WriteLine($"      FindB1: pos={pos}, prevPixel={prevPixel}, currPixel={currPixel}, targetColor={targetColor}");
                }

                // Check if this is a changing element to the target color
                if (currPixel == targetColor && prevPixel != targetColor)
                {
                    return pos;
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
        /// Creates a row initialized to all white pixels.
        /// </summary>
        private byte[] CreateWhiteRow(int bytesPerRow)
        {
            var row = new byte[bytesPerRow];
            // BlackIs1=true: 1=black, 0=white, so all zeros = all white (default) ✓
            // BlackIs1=false: 0=black, 1=white, so we need all 1s for white
            if (!_options.BlackIs1)
            {
                for (int i = 0; i < bytesPerRow; i++)
                    row[i] = 0xFF;
            }
            return row;
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

            // BlackIs1=true: 1 bits are black, so isSet=true means black
            // BlackIs1=false: 0 bits are black (1=white), so isSet=true means white (not black)
            return _options.BlackIs1 ? isSet : !isSet;
        }

        /// <summary>
        /// Fills a run of black pixels.
        /// </summary>
        private void FillBlackRun(byte[] row, int start, int length)
        {
            if (length <= 0) return;

            for (int i = 0; i < length; i++)
            {
                int pos = start + i;
                if (pos >= _options.Width) break;

                int byteIndex = pos / 8;
                int bitIndex = 7 - (pos % 8);

                if (byteIndex < row.Length)
                {
                    // BlackIs1=true: 1 bits are black, so SET bit for black
                    // BlackIs1=false: 0 bits are black (1=white), so CLEAR bit for black
                    if (_options.BlackIs1)
                    {
                        row[byteIndex] |= (byte)(1 << bitIndex); // Set bit for black
                    }
                    else
                    {
                        row[byteIndex] &= (byte)~(1 << bitIndex); // Clear bit for black
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
