using System;
using System.IO;

namespace ImageLibrary.Lzw;

/// <summary>
/// Writes variable-width codes to a stream in MSB-first order (PDF/TIFF style).
/// </summary>
internal sealed class BitWriter : IDisposable
{
    private readonly Stream _output;
    private readonly bool _leaveOpen;
    private int _buffer;
    private int _bitsInBuffer;

    public BitWriter(Stream output, bool leaveOpen = false)
    {
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _leaveOpen = leaveOpen;
        _buffer = 0;
        _bitsInBuffer = 0;
    }

    /// <summary>
    /// Writes a code with the specified number of bits.
    /// Bits are written MSB-first.
    /// </summary>
    public void WriteCode(int code, int bitCount)
    {
        // Add the code to our buffer (MSB first)
        // We accumulate bits from the left side
        _buffer = (_buffer << bitCount) | code;
        _bitsInBuffer += bitCount;

        // Write out complete bytes
        while (_bitsInBuffer >= 8)
        {
            _bitsInBuffer -= 8;
            int byteToWrite = (_buffer >> _bitsInBuffer) & 0xFF;
            _output.WriteByte((byte)byteToWrite);
        }

        // Keep only the remaining bits
        _buffer &= (1 << _bitsInBuffer) - 1;
    }

    /// <summary>
    /// Flushes any remaining bits to the output, padding with zeros.
    /// </summary>
    public void Flush()
    {
        if (_bitsInBuffer > 0)
        {
            // Pad with zeros and write the final byte
            int byteToWrite = _buffer << (8 - _bitsInBuffer);
            _output.WriteByte((byte)byteToWrite);
            _buffer = 0;
            _bitsInBuffer = 0;
        }

        _output.Flush();
    }

    public void Dispose()
    {
        Flush();
        if (!_leaveOpen)
        {
            _output.Dispose();
        }
    }
}