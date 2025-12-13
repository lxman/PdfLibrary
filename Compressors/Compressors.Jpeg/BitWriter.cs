using System;
using System.IO;

namespace Compressors.Jpeg;

/// <summary>
/// Writes bits to a byte stream for Huffman encoding.
/// Handles JPEG byte stuffing (inserts 0x00 after 0xFF).
/// </summary>
public class BitWriter : IDisposable
{
    private readonly Stream _stream;
    private int _bitBuffer;
    private int _bitsInBuffer;
    private bool _disposed;

    /// <summary>
    /// Creates a new BitWriter for the specified stream.
    /// </summary>
    public BitWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _bitBuffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>
    /// Gets the number of bits waiting to be written.
    /// </summary>
    public int BitsInBuffer => _bitsInBuffer;

    /// <summary>
    /// Writes a single bit to the stream.
    /// </summary>
    /// <param name="bit">0 or 1</param>
    public void WriteBit(int bit)
    {
        _bitBuffer = (_bitBuffer << 1) | (bit & 1);
        _bitsInBuffer++;

        if (_bitsInBuffer == 8)
        {
            FlushByte();
        }
    }

    /// <summary>
    /// Writes multiple bits to the stream.
    /// </summary>
    /// <param name="bits">The bits to write (MSB first)</param>
    /// <param name="count">Number of bits to write (1-16)</param>
    public void WriteBits(int bits, int count)
    {
        if (count < 1 || count > 16)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be between 1 and 16");

        // Mask to ensure only 'count' bits are used
        bits &= (1 << count) - 1;

        // Add bits to buffer
        _bitBuffer = (_bitBuffer << count) | bits;
        _bitsInBuffer += count;

        // Flush complete bytes
        while (_bitsInBuffer >= 8)
        {
            FlushByte();
        }
    }

    /// <summary>
    /// Writes a byte directly to the stream, with byte stuffing if necessary.
    /// </summary>
    public void WriteByte(byte value)
    {
        FlushPendingBits();
        WriteByteWithStuffing(value);
    }

    /// <summary>
    /// Writes bytes directly to the stream.
    /// </summary>
    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        FlushPendingBits();
        foreach (byte b in data)
        {
            WriteByteWithStuffing(b);
        }
    }

    /// <summary>
    /// Writes bytes directly to the stream without byte stuffing.
    /// Use for marker segments and other non-entropy-coded data.
    /// </summary>
    public void WriteBytesRaw(ReadOnlySpan<byte> data)
    {
        FlushPendingBits();
        _stream.Write(data);
    }

    /// <summary>
    /// Writes a 16-bit value in big-endian order.
    /// </summary>
    public void WriteUInt16BigEndian(ushort value)
    {
        FlushPendingBits();
        _stream.WriteByte((byte)(value >> 8));
        _stream.WriteByte((byte)value);
    }

    /// <summary>
    /// Flushes any remaining bits, padding with 1s to byte boundary.
    /// JPEG standard specifies padding with 1 bits.
    /// </summary>
    public void FlushWithPadding()
    {
        if (_bitsInBuffer > 0)
        {
            // Pad with 1 bits to complete the byte
            int padBits = 8 - _bitsInBuffer;
            _bitBuffer = (_bitBuffer << padBits) | ((1 << padBits) - 1);
            _bitsInBuffer = 8;
            FlushByte();
        }
    }

    /// <summary>
    /// Flushes any complete bytes in the buffer.
    /// </summary>
    private void FlushByte()
    {
        if (_bitsInBuffer >= 8)
        {
            _bitsInBuffer -= 8;
            int byteValue = (_bitBuffer >> _bitsInBuffer) & 0xFF;
            WriteByteWithStuffing((byte)byteValue);
        }
    }

    /// <summary>
    /// Flushes any pending bits (without padding).
    /// </summary>
    private void FlushPendingBits()
    {
        while (_bitsInBuffer >= 8)
        {
            FlushByte();
        }
    }

    /// <summary>
    /// Writes a byte with JPEG byte stuffing.
    /// If 0xFF is written, 0x00 is inserted after it.
    /// </summary>
    private void WriteByteWithStuffing(byte value)
    {
        _stream.WriteByte(value);

        // Byte stuffing: follow 0xFF with 0x00
        if (value == 0xFF)
        {
            _stream.WriteByte(0x00);
        }
    }

    /// <summary>
    /// Gets the size in bits needed to represent a value.
    /// Used for coefficient encoding.
    /// </summary>
    public static int GetBitSize(int value)
    {
        if (value == 0)
            return 0;

        if (value < 0)
            value = -value;

        var bits = 0;
        while (value > 0)
        {
            bits++;
            value >>= 1;
        }
        return bits;
    }

    /// <summary>
    /// Gets the bit pattern for a coefficient value.
    /// Negative values use one's complement.
    /// </summary>
    public static int GetBitPattern(int value, int bits)
    {
        if (value >= 0)
            return value;

        // Negative: use one's complement
        return value + (1 << bits) - 1;
    }

    /// <summary>
    /// Disposes the writer and flushes remaining bits.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            FlushWithPadding();
            _disposed = true;
        }
    }
}
