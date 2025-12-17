using System;
using System.Collections.Generic;

namespace ImageLibrary.Ccitt;

/// <summary>
/// Bit writer for CCITT compressed data.
/// Writes bits MSB (most significant bit) first, as required by CCITT.
/// </summary>
public class CcittBitWriter
{
    private readonly List<byte> _data;
    private byte _currentByte;
    private int _bitPosition; // 0-7, 0 = MSB, how many bits written to current byte

    /// <summary>
    /// Creates a new bit writer.
    /// </summary>
    public CcittBitWriter()
    {
        _data = [];
        _currentByte = 0;
        _bitPosition = 0;
    }

    /// <summary>
    /// Gets the current bit position in the stream.
    /// </summary>
    public int Position => (_data.Count * 8) + _bitPosition;

    /// <summary>
    /// Writes a single bit.
    /// </summary>
    /// <param name="bit">The bit to write (0 or 1).</param>
    public void WriteBit(int bit)
    {
        if (bit != 0 && bit != 1)
            throw new ArgumentOutOfRangeException(nameof(bit));

        // Set the bit at the current position (MSB first)
        if (bit == 1)
        {
            _currentByte |= (byte)(0x80 >> _bitPosition);
        }

        _bitPosition++;
        if (_bitPosition >= 8)
        {
            _data.Add(_currentByte);
            _currentByte = 0;
            _bitPosition = 0;
        }
    }

    /// <summary>
    /// Writes multiple bits from an integer (MSB first).
    /// </summary>
    /// <param name="value">The value containing the bits.</param>
    /// <param name="bitCount">Number of bits to write (1-32).</param>
    public void WriteBits(int value, int bitCount)
    {
        if (bitCount < 1 || bitCount > 32)
            throw new ArgumentOutOfRangeException(nameof(bitCount));

        // Write bits from MSB to LSB
        for (int i = bitCount - 1; i >= 0; i--)
        {
            WriteBit((value >> i) & 1);
        }
    }

    /// <summary>
    /// Writes a Huffman code.
    /// </summary>
    /// <param name="code">The Huffman code.</param>
    public void WriteCode(HuffmanTables.HuffmanCode code)
    {
        WriteBits(code.Code, code.BitLength);
    }

    /// <summary>
    /// Writes an EOL (End of Line) code.
    /// </summary>
    public void WriteEol()
    {
        WriteBits(CcittConstants.EolCode, CcittConstants.EolBits);
    }

    /// <summary>
    /// Writes EOFB (End of Facsimile Block) - two EOL codes.
    /// </summary>
    public void WriteEofb()
    {
        WriteEol();
        WriteEol();
    }

    /// <summary>
    /// Writes RTC (Return to Control) - six EOL codes.
    /// </summary>
    public void WriteRtc()
    {
        for (var i = 0; i < CcittConstants.RtcEolCount; i++)
        {
            WriteEol();
        }
    }

    /// <summary>
    /// Writes the pass mode code.
    /// </summary>
    public void WritePassMode()
    {
        WriteBits(TwoDimensionalCodes.PassCode, TwoDimensionalCodes.PassCodeBits);
    }

    /// <summary>
    /// Writes the horizontal mode code.
    /// </summary>
    public void WriteHorizontalMode()
    {
        WriteBits(TwoDimensionalCodes.HorizontalCode, TwoDimensionalCodes.HorizontalCodeBits);
    }

    /// <summary>
    /// Writes a vertical mode code.
    /// </summary>
    /// <param name="offset">The offset (-3 to +3).</param>
    public void WriteVerticalMode(int offset)
    {
        if (!TwoDimensionalCodes.TryGetVerticalCode(offset, out int code, out int bitLength))
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be between -3 and +3");
        }
        WriteBits(code, bitLength);
    }

    /// <summary>
    /// Writes a run length using the appropriate Huffman codes.
    /// </summary>
    /// <param name="runLength">The run length.</param>
    /// <param name="isWhite">True for white run, false for black.</param>
    public void WriteRunLength(int runLength, bool isWhite)
    {
        // Handle very long runs with multiple makeup codes
        while (runLength > CcittConstants.MaxExtendedMakeupRunLength)
        {
            // Write the maximum extended makeup code (2560)
            WriteCode(HuffmanTables.ExtendedMakeupCodes[HuffmanTables.ExtendedMakeupCodes.Length - 1]);
            runLength -= CcittConstants.MaxExtendedMakeupRunLength;
        }

        HuffmanTables.GetRunLengthCodes(runLength, isWhite, out HuffmanTables.HuffmanCode makeupCode, out HuffmanTables.HuffmanCode terminatingCode);

        if (makeupCode.BitLength > 0)
        {
            WriteCode(makeupCode);
        }

        WriteCode(terminatingCode);
    }

    /// <summary>
    /// Pads to byte boundary with zeros.
    /// </summary>
    public void AlignToByte()
    {
        if (_bitPosition > 0)
        {
            _data.Add(_currentByte);
            _currentByte = 0;
            _bitPosition = 0;
        }
    }

    /// <summary>
    /// Gets the written data.
    /// </summary>
    /// <returns>The compressed data.</returns>
    public byte[] ToArray()
    {
        // Flush any remaining bits
        if (_bitPosition > 0)
        {
            var result = new byte[_data.Count + 1];
            _data.CopyTo(result);
            result[_data.Count] = _currentByte;
            return result;
        }

        return _data.ToArray();
    }

    /// <summary>
    /// Clears all written data.
    /// </summary>
    public void Clear()
    {
        _data.Clear();
        _currentByte = 0;
        _bitPosition = 0;
    }
}