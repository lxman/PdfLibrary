using System;
using System.IO;

namespace Compressors.Jpeg;

/// <summary>
/// Reads bits from a byte stream for Huffman decoding.
/// Handles JPEG byte stuffing (0xFF 0x00 sequences).
/// </summary>
public class BitReader
{
    private readonly Stream _stream;
    private int _bitBuffer;
    private int _bitsInBuffer;
    private bool _endOfData;

    /// <summary>
    /// Creates a new BitReader for the specified stream.
    /// </summary>
    public BitReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _bitBuffer = 0;
        _bitsInBuffer = 0;
        _endOfData = false;
    }

    /// <summary>
    /// Gets whether the end of data has been reached.
    /// </summary>
    public bool EndOfData => _endOfData;

    /// <summary>
    /// Gets the number of bits currently in the buffer.
    /// </summary>
    public int BitsInBuffer => _bitsInBuffer;

    /// <summary>
    /// Reads a single bit from the stream.
    /// </summary>
    /// <returns>0 or 1, or -1 if end of data</returns>
    public int ReadBit()
    {
        if (_bitsInBuffer == 0 && !_endOfData)
        {
            if (!FillBuffer())
            {
                // Could not read more data
            }
        }

        if (_bitsInBuffer == 0)
            return -1;

        _bitsInBuffer--;
        return (_bitBuffer >> _bitsInBuffer) & 1;
    }

    /// <summary>
    /// Reads multiple bits from the stream.
    /// </summary>
    /// <param name="count">Number of bits to read (1-16)</param>
    /// <returns>The bits as an integer, or -1 if end of data</returns>
    public int ReadBits(int count)
    {
        if (count < 1 || count > 16)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 16");

        // Try to get enough bits
        while (_bitsInBuffer < count && !_endOfData)
        {
            if (!FillBuffer())
                break;
        }

        // If we don't have enough bits, return -1
        if (_bitsInBuffer < count)
            return -1;

        _bitsInBuffer -= count;
        return (_bitBuffer >> _bitsInBuffer) & ((1 << count) - 1);
    }

    /// <summary>
    /// Peeks at the next bits without consuming them.
    /// </summary>
    /// <param name="count">Number of bits to peek (1-16)</param>
    /// <returns>The bits as an integer, or -1 if not enough data available</returns>
    public int PeekBits(int count)
    {
        if (count < 1 || count > 16)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 16");

        // Try to get enough bits
        while (_bitsInBuffer < count && !_endOfData)
        {
            if (!FillBuffer())
                break;
        }

        // If we don't have enough bits, return -1
        if (_bitsInBuffer < count)
            return -1;

        return (_bitBuffer >> (_bitsInBuffer - count)) & ((1 << count) - 1);
    }

    /// <summary>
    /// Skips the specified number of bits.
    /// </summary>
    public void SkipBits(int count)
    {
        if (count <= _bitsInBuffer)
        {
            _bitsInBuffer -= count;
        }
        else
        {
            count -= _bitsInBuffer;
            _bitsInBuffer = 0;

            while (count >= 8)
            {
                ReadByteWithStuffing();
                count -= 8;
            }

            if (count > 0)
            {
                FillBuffer();
                _bitsInBuffer -= count;
            }
        }
    }

    /// <summary>
    /// Aligns reading to the next byte boundary by discarding remaining bits.
    /// </summary>
    public void AlignToByte()
    {
        _bitsInBuffer = 0;
        _bitBuffer = 0;
    }

    /// <summary>
    /// Reads a byte directly from the stream (after aligning to byte boundary).
    /// </summary>
    public int ReadByte()
    {
        AlignToByte();
        return _stream.ReadByte();
    }

    /// <summary>
    /// Reads bytes directly from the stream (after aligning to byte boundary).
    /// </summary>
    public int ReadBytes(Span<byte> buffer)
    {
        AlignToByte();
        return _stream.Read(buffer);
    }

    /// <summary>
    /// Fills the bit buffer with more data from the stream.
    /// Handles JPEG byte stuffing (0xFF followed by 0x00 means 0xFF data).
    /// </summary>
    private bool FillBuffer()
    {
        if (_endOfData)
            return false;

        int b = ReadByteWithStuffing();
        if (b < 0)
        {
            _endOfData = true;
            return false;
        }

        _bitBuffer = (_bitBuffer << 8) | b;
        _bitsInBuffer += 8;
        return true;
    }

    /// <summary>
    /// Reads a byte from the stream, handling JPEG byte stuffing.
    /// </summary>
    private int ReadByteWithStuffing()
    {
        int b = _stream.ReadByte();
        if (b < 0)
            return -1;

        // Handle byte stuffing: 0xFF 0x00 means 0xFF data byte
        if (b == 0xFF)
        {
            int next = _stream.ReadByte();
            if (next < 0)
                return -1;

            if (next == 0x00)
            {
                // Stuffed byte - return 0xFF
                return 0xFF;
            }
            else if (next >= 0xD0 && next <= 0xD7)
            {
                // Restart marker - skip it and continue
                return ReadByteWithStuffing();
            }
            else
            {
                // Other marker - end of entropy-coded data
                _endOfData = true;
                return -1;
            }
        }

        return b;
    }

    /// <summary>
    /// Resets the reader state.
    /// </summary>
    public void Reset()
    {
        _bitBuffer = 0;
        _bitsInBuffer = 0;
        _endOfData = false;
    }

    /// <summary>
    /// Extends a value based on its bit size for JPEG coefficient decoding.
    /// Used to convert unsigned bit patterns to signed values.
    /// </summary>
    public static int Extend(int value, int bits)
    {
        if (bits == 0)
            return 0;

        // If high bit is 0, value is negative
        int threshold = 1 << (bits - 1);
        if (value < threshold)
            return value + (-1 << bits) + 1;

        return value;
    }
}
