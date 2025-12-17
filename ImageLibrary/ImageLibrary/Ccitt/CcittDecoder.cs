using System;
using System.Collections.Generic;

namespace ImageLibrary.Ccitt;

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
            return [];

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
                for (var i = 0; i < bytesPerRow; i++)
                    referenceLine[i] = 0xFF;
            }
        }

        var rowCount = 0;
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
                    var is1D = true;
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
        byte[] row = CreateWhiteRow(bytesPerRow);
        var pixelPosition = 0;
        var isWhite = true; // Always start with white

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
        // Tracing disabled
    }

    /// <summary>
    /// Decodes a single row using 2D encoding (Group 3 2D or Group 4).
    /// </summary>
    private byte[]? Decode2DRow(CcittBitReader reader, byte[] referenceLine)
    {
        int bytesPerRow = (_options.Width + 7) / 8;
        byte[] row = CreateWhiteRow(bytesPerRow);
        int a0 = -1; // Current position (-1 means before the line)
        var a0Color = false; // false = white, true = black (at position a0)
        var modeCount = 0;

        while (a0 < _options.Width)
        {
            int preModeBitPos = reader.Position;
            TwoDimensionalMode mode = _huffman.Decode2DMode(reader);
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
    ///
    /// Optimized to use byte-level scanning for improved performance.
    /// </summary>
    private int FindB1(byte[] referenceLine, int a0, bool a0Color)
    {
        int startPos = a0 < 0 ? 0 : a0 + 1;
        bool targetColor = !a0Color; // The opposite color we're looking for

        if (startPos >= _options.Width)
            return _options.Width;

        // Determine what byte pattern indicates "all same color" (no transitions)
        // BlackIs1=true: 1=black, 0=white. So 0xFF=all black, 0x00=all white
        // BlackIs1=false: 0=black, 1=white. So 0x00=all black, 0xFF=all white
        // We're looking for a transition TO targetColor
        // If targetColor is black, we're looking for 0->1 (BlackIs1) or 1->0 (!BlackIs1)
        // Note: We don't use allTargetColor/allOppositeColor for FindB1 because
        // we need to detect transitions, not just find a color. A uniform byte
        // still needs boundary checking for transitions.

        // Special case: at startPos, check if it's a changing element
        if (startPos > 0)
        {
            bool prevPixel = GetPixelFast(referenceLine, startPos - 1);
            bool currPixel = GetPixelFast(referenceLine, startPos);
            if (currPixel == targetColor && prevPixel != targetColor)
            {
                return startPos;
            }
        }
        else if (startPos == 0)
        {
            bool firstPixel = GetPixelFast(referenceLine, 0);
            if (firstPixel == targetColor)
            {
                return 0;
            }
        }

        // Start scanning from the next position
        int pos = startPos + 1;

        // Align to byte boundary for fast scanning
        int byteIndex = pos >> 3;
        int bitInByte = pos & 7;

        // First, handle partial byte if not aligned
        if (bitInByte != 0 && byteIndex < referenceLine.Length)
        {
            // Scan remaining bits in current byte
            byte currentByte = referenceLine[byteIndex];
            for (int bit = 7 - bitInByte; bit >= 0 && pos < _options.Width; bit--, pos++)
            {
                bool currPixel = GetPixelFromByte(currentByte, bit);
                bool prevPixel = GetPixelFast(referenceLine, pos - 1);

                if (currPixel == targetColor && prevPixel != targetColor)
                {
                    return pos;
                }
            }
            byteIndex++;
        }

        // Fast path: scan full bytes looking for any byte that's not all-same-color
        // A byte with all same color cannot contain a transition TO targetColor
        // (unless the transition happens at the byte boundary)
        while (byteIndex < referenceLine.Length && pos < _options.Width)
        {
            byte b = referenceLine[byteIndex];

            // Check byte boundary transition first
            bool prevPixel = GetPixelFast(referenceLine, pos - 1);
            bool firstPixelInByte = GetPixelFromByte(b, 7);

            if (firstPixelInByte == targetColor && prevPixel != targetColor)
            {
                return pos;
            }

            // If byte is uniform (all 0s or all 1s), skip it entirely
            // But only if we didn't find a transition at the boundary
            if (b == 0x00 || b == 0xFF)
            {
                pos += 8;
                if (pos > _options.Width) pos = _options.Width;
                byteIndex++;
                continue;
            }

            // Byte has mixed colors - scan bit by bit
            for (var bit = 6; bit >= 0 && pos + (6 - bit) + 1 < _options.Width; bit--)
            {
                int currentPos = pos + (6 - bit) + 1;
                bool currPixel = GetPixelFromByte(b, bit);
                bool prev = GetPixelFromByte(b, bit + 1);

                if (currPixel == targetColor && prev != targetColor)
                {
                    return currentPos;
                }
            }

            pos += 8;
            if (pos > _options.Width) pos = _options.Width;
            byteIndex++;
        }

        return _options.Width;
    }

    /// <summary>
    /// Gets pixel value from a byte at the specified bit position (0-7, where 7 is MSB/leftmost).
    /// </summary>
    private bool GetPixelFromByte(byte b, int bitIndex)
    {
        bool isSet = ((b >> bitIndex) & 1) != 0;
        return _options.BlackIs1 ? isSet : !isSet;
    }

    /// <summary>
    /// Fast pixel access without bounds checking (caller must ensure valid position).
    /// </summary>
    private bool GetPixelFast(byte[] row, int position)
    {
        int byteIndex = position >> 3;
        int bitIndex = 7 - (position & 7);
        bool isSet = ((row[byteIndex] >> bitIndex) & 1) != 0;
        return _options.BlackIs1 ? isSet : !isSet;
    }

    /// <summary>
    /// Finds b2 - the next changing element after b1.
    /// Optimized to use byte-level scanning.
    /// </summary>
    private int FindB2(byte[] referenceLine, int b1)
    {
        if (b1 >= _options.Width)
            return _options.Width;

        bool b1Color = GetPixelFast(referenceLine, b1);

        // Byte pattern for "all b1Color"
        byte allSameColor = b1Color
            ? (_options.BlackIs1 ? (byte)0xFF : (byte)0x00)  // all black
            : (_options.BlackIs1 ? (byte)0x00 : (byte)0xFF); // all white

        int pos = b1 + 1;
        int byteIndex = pos >> 3;
        int bitInByte = pos & 7;

        // Handle partial byte if not aligned
        if (bitInByte != 0 && byteIndex < referenceLine.Length)
        {
            byte currentByte = referenceLine[byteIndex];
            for (int bit = 7 - bitInByte; bit >= 0 && pos < _options.Width; bit--, pos++)
            {
                bool pixelColor = GetPixelFromByte(currentByte, bit);
                if (pixelColor != b1Color)
                {
                    return pos;
                }
            }
            byteIndex++;
        }

        // Fast path: scan full bytes
        while (byteIndex < referenceLine.Length && pos < _options.Width)
        {
            byte b = referenceLine[byteIndex];

            // If byte is uniform and matches b1Color, skip it
            if (b == allSameColor)
            {
                pos += 8;
                if (pos > _options.Width) pos = _options.Width;
                byteIndex++;
                continue;
            }

            // Byte has different color or is mixed - scan bit by bit
            int bitsToCheck = Math.Min(8, _options.Width - pos);
            for (var i = 0; i < bitsToCheck; i++)
            {
                int bit = 7 - i;
                bool pixelColor = GetPixelFromByte(b, bit);
                if (pixelColor != b1Color)
                {
                    return pos + i;
                }
            }

            pos += 8;
            byteIndex++;
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
            for (var i = 0; i < bytesPerRow; i++)
                row[i] = 0xFF;
        }
        return row;
    }

    /// <summary>
    /// Gets the pixel value at a position (false = white, true = black).
    /// Uses bit shifts for improved performance.
    /// </summary>
    private bool GetPixel(byte[] row, int position)
    {
        if (position < 0 || position >= _options.Width)
            return false; // White for out of bounds

        int byteIndex = position >> 3;       // position / 8
        int bitIndex = 7 - (position & 7);   // 7 - (position % 8), MSB first

        if (byteIndex >= row.Length)
            return false;

        bool isSet = ((row[byteIndex] >> bitIndex) & 1) != 0;

        // BlackIs1=true: 1 bits are black, so isSet=true means black
        // BlackIs1=false: 0 bits are black (1=white), so isSet=true means white (not black)
        return _options.BlackIs1 ? isSet : !isSet;
    }

    /// <summary>
    /// Fills a run of black pixels using byte-aligned optimization.
    /// </summary>
    private void FillBlackRun(byte[] row, int start, int length)
    {
        if (length <= 0) return;

        // Clamp to image width
        int end = start + length;
        if (end > _options.Width)
            end = _options.Width;
        if (start >= end) return;

        // Use bit shifts for byte/bit indices (faster than division/modulo)
        int firstByte = start >> 3;        // start / 8
        int lastByte = (end - 1) >> 3;     // (end - 1) / 8
        int firstBitInByte = start & 7;    // start % 8
        int lastBitInByte = (end - 1) & 7; // (end - 1) % 8

        // Bounds check
        if (firstByte >= row.Length) return;
        if (lastByte >= row.Length)
            lastByte = row.Length - 1;

        // Determine fill values based on BlackIs1 setting
        // BlackIs1=true: 1 bits are black, so SET bits (OR with mask)
        // BlackIs1=false: 0 bits are black, so CLEAR bits (AND with ~mask)
        bool setForBlack = _options.BlackIs1;

        if (firstByte == lastByte)
        {
            // All pixels within one byte
            // Create mask for bits from firstBitInByte to lastBitInByte (MSB first)
            // Bit 7 is leftmost (position 0), bit 0 is rightmost (position 7)
            int numBits = end - start;
            // Mask: bits from (7 - firstBitInByte) down to (7 - lastBitInByte)
            var mask = (byte)(((1 << numBits) - 1) << (7 - lastBitInByte));

            if (setForBlack)
                row[firstByte] |= mask;
            else
                row[firstByte] &= (byte)~mask;
        }
        else
        {
            // Spans multiple bytes

            // Handle partial first byte (if not byte-aligned)
            if (firstBitInByte > 0)
            {
                // Mask for bits from firstBitInByte to 7 (rest of byte)
                // e.g., firstBitInByte=3 means positions 3,4,5,6,7 -> bits 4,3,2,1,0
                var mask = (byte)((1 << (8 - firstBitInByte)) - 1);

                if (setForBlack)
                    row[firstByte] |= mask;
                else
                    row[firstByte] &= (byte)~mask;

                firstByte++;
            }

            // Fill full bytes in the middle
            byte fullByte = setForBlack ? (byte)0xFF : (byte)0x00;
            for (int b = firstByte; b < lastByte; b++)
            {
                row[b] = fullByte;
            }

            // Handle partial last byte (if not byte-aligned)
            // lastBitInByte is the last bit position (0-7) within the byte
            if (lastBitInByte < 7)
            {
                // Mask for bits from 0 to lastBitInByte
                // e.g., lastBitInByte=3 means positions 0,1,2,3 -> bits 7,6,5,4
                var mask = (byte)(0xFF << (7 - lastBitInByte));

                if (setForBlack)
                    row[lastByte] |= mask;
                else
                    row[lastByte] &= (byte)~mask;
            }
            else
            {
                // Last byte is fully covered
                row[lastByte] = fullByte;
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
        var zeros = 0;

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
        for (var i = 0; i < rows.Count; i++)
        {
            Array.Copy(rows[i], 0, result, i * bytesPerRow, Math.Min(rows[i].Length, bytesPerRow));
        }
        return result;
    }
}