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
    private byte _nextMarker; // 0 = no marker, otherwise the marker byte (non-zero part of FFxx)

    /// <summary>
    /// Creates a new BitReader for the specified stream.
    /// </summary>
    public BitReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _bitBuffer = 0;
        _bitsInBuffer = 0;
        _endOfData = false;
        _nextMarker = 0;
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
    /// Checks if a marker was encountered during buffer filling.
    /// Returns true if _nextMarker is set (non-zero).
    /// Only reliable when buffer is empty, as marker is only detected when FillBuffer() encounters it.
    /// </summary>
    public bool IsAtMarker()
    {
        // Can only detect markers when buffer is empty
        if (_bitsInBuffer != 0)
            return false;

        // Check if we have a pending marker
        return _nextMarker != 0 || _endOfData;
    }

    /// <summary>
    /// Gets the pending marker byte (0 if no marker).
    /// </summary>
    public byte GetMarker()
    {
        return _nextMarker;
    }

    /// <summary>
    /// Clears the pending marker, allowing reading to continue.
    /// </summary>
    public void ClearMarker()
    {
        _nextMarker = 0;
    }

    /// <summary>
    /// Gets the current stream position (for debugging).
    /// </summary>
    public long GetStreamPosition()
    {
        return _stream.Position;
    }

    // Debug tracking
    private int _bitsReadSinceMarker = 0;
    private bool _trackingBits = false;

    /// <summary>
    /// Start tracking bit reads (for debugging).
    /// </summary>
    public void StartBitTracking()
    {
        _trackingBits = true;
        _bitsReadSinceMarker = 0;
    }

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
        int bit = (_bitBuffer >> _bitsInBuffer) & 1;

        if (_trackingBits && _bitsReadSinceMarker < 500)
        {
            _bitsReadSinceMarker++;
            Console.WriteLine($"      BIT#{_bitsReadSinceMarker}: {bit} (buffer=0x{_bitBuffer:X4}, bitsLeft={_bitsInBuffer}, pos=0x{_stream.Position:X6})");
        }

        return bit;
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
    /// When a marker is encountered, stores it in _nextMarker and stops filling.
    /// </summary>
    private bool FillBuffer()
    {
        if (_endOfData || _nextMarker != 0)
            return false;

        long posBeforeRead = _stream.Position;
        int b = ReadByteWithStuffing();
        if (b < 0)
        {
            // If _nextMarker is set, a marker was encountered - don't set _endOfData
            // If _nextMarker is NOT set, we hit actual end-of-stream
            if (_nextMarker == 0)
                _endOfData = true;
            return false;
        }

        // Mask off consumed bits before shifting
        // Only keep the bits that haven't been consumed
        int validBits = _bitBuffer & ((1 << _bitsInBuffer) - 1);
        _bitBuffer = (validBits << 8) | b;
        _bitsInBuffer += 8;

        if (_trackingBits && _bitsReadSinceMarker < 500)
        {
            Console.WriteLine($"    FILL: Read byte 0x{b:X2} from pos 0x{posBeforeRead:X6}, buffer now=0x{_bitBuffer:X}, bits now={_bitsInBuffer}");
        }

        return true;
    }

    /// <summary>
    /// Reads a byte from the stream, handling JPEG byte stuffing.
    /// When a marker is encountered, stores it in _nextMarker and returns -1.
    /// </summary>
    private int ReadByteWithStuffing()
    {
        int b = _stream.ReadByte();
        if (b < 0)
            return -1;

        // Handle byte stuffing: 0xFF 0x00 means 0xFF data byte
        if (b == 0xFF)
        {
            long pos = _stream.Position;
            int next = _stream.ReadByte();
            if (next < 0)
                return -1;

            if (next == 0x00)
            {
                // Stuffed byte - return 0xFF
                return 0xFF;
            }
            else
            {
                // Marker (including RST markers) - store it and signal end of buffer filling
                _nextMarker = (byte)next;

                // Log RST marker detection
                if (next >= 0xD0 && next <= 0xD7)
                {
                    Console.WriteLine($"    *** MARKER DETECTED: RST{next - 0xD0} at stream pos 0x{pos - 1:X6}");
                    Console.WriteLine($"    *** Buffer state: bits={_bitsInBuffer}, buffer=0x{_bitBuffer:X}");
                }

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
        _nextMarker = 0;
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
