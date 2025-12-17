using System;

namespace ImageLibrary.Ccitt;

/// <summary>
/// Bit reader for CCITT compressed data.
/// Reads bits MSB (most significant bit) first, as required by CCITT.
/// </summary>
public class CcittBitReader
{
    private readonly byte[] _data;
    private int _bytePosition;
    private int _bitPosition; // 0-7, 0 = MSB

    /// <summary>
    /// Creates a new bit reader for the given data.
    /// </summary>
    /// <param name="data">The compressed data to read.</param>
    public CcittBitReader(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _bytePosition = 0;
        _bitPosition = 0;
    }

    /// <summary>
    /// Gets the current bit position in the stream.
    /// </summary>
    public int Position => (_bytePosition * 8) + _bitPosition;

    /// <summary>
    /// Gets whether the end of data has been reached.
    /// </summary>
    public bool IsAtEnd => _bytePosition >= _data.Length;

    /// <summary>
    /// Gets the total number of bits available.
    /// </summary>
    public int TotalBits => _data.Length * 8;

    /// <summary>
    /// Gets the number of bits remaining.
    /// </summary>
    public int BitsRemaining => TotalBits - Position;

    /// <summary>
    /// Reads a single bit.
    /// </summary>
    /// <returns>The bit value (0 or 1), or -1 if at end of data.</returns>
    public int ReadBit()
    {
        if (_bytePosition >= _data.Length)
            return -1;

        int bit = (_data[_bytePosition] >> (7 - _bitPosition)) & 1;

        _bitPosition++;
        if (_bitPosition >= 8)
        {
            _bitPosition = 0;
            _bytePosition++;
        }

        return bit;
    }

    /// <summary>
    /// Reads multiple bits as an integer (MSB first).
    /// </summary>
    /// <param name="count">Number of bits to read (1-32).</param>
    /// <returns>The bits as an integer, or -1 if insufficient data.</returns>
    public int ReadBits(int count)
    {
        if (count < 1 || count > 32)
            throw new ArgumentOutOfRangeException(nameof(count));

        if (BitsRemaining < count)
            return -1;

        var result = 0;
        for (var i = 0; i < count; i++)
        {
            int bit = ReadBit();
            if (bit < 0)
                return -1;
            result = (result << 1) | bit;
        }

        return result;
    }

    /// <summary>
    /// Peeks at the next bits without advancing the position.
    /// </summary>
    /// <param name="count">Number of bits to peek (1-32).</param>
    /// <returns>The bits as an integer, or -1 if insufficient data.</returns>
    public int PeekBits(int count)
    {
        int savedBytePos = _bytePosition;
        int savedBitPos = _bitPosition;

        int result = ReadBits(count);

        _bytePosition = savedBytePos;
        _bitPosition = savedBitPos;

        return result;
    }

    /// <summary>
    /// Skips the specified number of bits.
    /// </summary>
    /// <param name="count">Number of bits to skip.</param>
    public void SkipBits(int count)
    {
        int newPosition = Position + count;
        _bytePosition = newPosition / 8;
        _bitPosition = newPosition % 8;
    }

    /// <summary>
    /// Aligns to the next byte boundary.
    /// </summary>
    public void AlignToByte()
    {
        if (_bitPosition > 0)
        {
            _bitPosition = 0;
            _bytePosition++;
        }
    }

    /// <summary>
    /// Resets the reader to the beginning.
    /// </summary>
    public void Reset()
    {
        _bytePosition = 0;
        _bitPosition = 0;
    }

    /// <summary>
    /// Seeks to an absolute bit position.
    /// </summary>
    /// <param name="bitPosition">The bit position to seek to.</param>
    public void Seek(int bitPosition)
    {
        if (bitPosition < 0 || bitPosition > TotalBits)
            throw new ArgumentOutOfRangeException(nameof(bitPosition));

        _bytePosition = bitPosition / 8;
        _bitPosition = bitPosition % 8;
    }

    /// <summary>
    /// Checks if the next bits match a specific pattern.
    /// </summary>
    /// <param name="pattern">The pattern to match.</param>
    /// <param name="bitCount">Number of bits in the pattern.</param>
    /// <returns>True if the pattern matches.</returns>
    public bool CheckPattern(int pattern, int bitCount)
    {
        int bits = PeekBits(bitCount);
        return bits == pattern;
    }

    /// <summary>
    /// Searches for the EOL pattern (000000000001).
    /// </summary>
    /// <returns>True if EOL was found and position is after it.</returns>
    public bool FindAndSkipEol()
    {
        // Look for 11 zeros followed by a 1
        var zeroCount = 0;

        while (!IsAtEnd)
        {
            int bit = ReadBit();
            if (bit < 0)
                return false;

            if (bit == 0)
            {
                zeroCount++;
            }
            else // bit == 1
            {
                if (zeroCount >= 11)
                {
                    // Found EOL
                    return true;
                }
                zeroCount = 0;
            }
        }

        return false;
    }
}