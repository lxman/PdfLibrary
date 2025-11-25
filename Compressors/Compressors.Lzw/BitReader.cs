using System;
using System.IO;

namespace Compressors.Lzw
{
    /// <summary>
    /// Reads variable-width codes from a stream in MSB-first order (PDF/TIFF style).
    /// </summary>
    internal sealed class BitReader : IDisposable
    {
        private readonly Stream _input;
        private readonly bool _leaveOpen;
        private int _buffer;
        private int _bitsInBuffer;
        private bool _endOfStream;

        public BitReader(Stream input, bool leaveOpen = false)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _leaveOpen = leaveOpen;
            _buffer = 0;
            _bitsInBuffer = 0;
            _endOfStream = false;
        }

        /// <summary>
        /// Gets whether the end of the input stream has been reached.
        /// </summary>
        public bool EndOfStream => _endOfStream && _bitsInBuffer == 0;

        /// <summary>
        /// Reads a code with the specified number of bits.
        /// Returns -1 if end of stream is reached before enough bits are available.
        /// </summary>
        public int ReadCode(int bitCount)
        {
            // Ensure we have enough bits in the buffer
            while (_bitsInBuffer < bitCount)
            {
                int nextByte = _input.ReadByte();
                if (nextByte == -1)
                {
                    _endOfStream = true;
                    if (_bitsInBuffer == 0)
                    {
                        return -1;
                    }
                    // Return what we have, padded with zeros
                    break;
                }

                // Add byte to buffer (MSB first)
                _buffer = (_buffer << 8) | nextByte;
                _bitsInBuffer += 8;
            }

            if (_bitsInBuffer < bitCount)
            {
                // Not enough bits available
                if (_bitsInBuffer == 0)
                {
                    return -1;
                }
                // Pad remaining bits with zeros
                _buffer <<= (bitCount - _bitsInBuffer);
                _bitsInBuffer = bitCount;
            }

            // Extract the code from the buffer
            _bitsInBuffer -= bitCount;
            int code = (_buffer >> _bitsInBuffer) & ((1 << bitCount) - 1);

            // Keep only remaining bits
            _buffer &= (1 << _bitsInBuffer) - 1;

            return code;
        }

        public void Dispose()
        {
            if (!_leaveOpen)
            {
                _input.Dispose();
            }
        }
    }
}
