using System;
using System.IO;

namespace Jp2Codec.Tier2
{
    /// <summary>
    /// MSB-first bit reader for J2K packet headers (ISO/IEC 15444-1 B.10).
    /// Implements the 0xFF stuff-bit convention: when the previous byte
    /// emitted was 0xFF, the next byte's top bit is a stuff bit (its actual
    /// value is forced to 0 by the encoder), so the reader only delivers
    /// 7 bits from it. This prevents accidental marker-code emissions inside
    /// packet header data.
    ///
    /// Reader contract: the input span must be a single packet header (or a
    /// concatenation of packet headers stored in a PPM/PPT box). Marker
    /// detection (SOP/EPH) is the responsibility of the layer above.
    /// </summary>
    internal sealed class PacketHeaderBitReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _pos;
        private int _bitsLeft;     // bits remaining in _currentByte
        private byte _currentByte; // most recently fetched byte
        private bool _previousWasFf;

        public PacketHeaderBitReader(byte[] data, int offset, int length)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

            _data = data;
            _pos = offset;
            _end = offset + length;
            _bitsLeft = 0;
            _currentByte = 0;
            _previousWasFf = false;
            _startOffset = offset;
        }

        private readonly int _startOffset;

        /// <summary>True once all bits in the input have been consumed.</summary>
        public bool IsAtEnd => _bitsLeft == 0 && _pos >= _end;

        /// <summary>
        /// Bytes consumed since construction. Only meaningful after
        /// <see cref="AlignToByte"/> has been called — between bit reads the
        /// underlying byte cursor sits one past the byte currently being
        /// shifted out, so the value over-counts mid-byte by 1.
        /// </summary>
        public int BytesConsumedAfterAlign => _pos - _startOffset;

        /// <summary>Read one bit (0 or 1).</summary>
        public int ReadBit()
        {
            if (_bitsLeft == 0) FetchNextByte();
            _bitsLeft--;
            return (_currentByte >> _bitsLeft) & 1;
        }

        /// <summary>Read <paramref name="count"/> bits MSB-first as an integer.</summary>
        public int ReadBits(int count)
        {
            if (count < 0 || count > 31) throw new ArgumentOutOfRangeException(nameof(count));
            int value = 0;
            for (var i = 0; i < count; i++)
            {
                value = (value << 1) | ReadBit();
            }
            return value;
        }

        /// <summary>
        /// Align to the next byte boundary. Discards any remaining bits in the
        /// current byte. Used at packet-header termination per B.10.2.
        /// </summary>
        public void AlignToByte()
        {
            _bitsLeft = 0;
            // If alignment falls on a 0xFF byte boundary, the spec still requires
            // the following stuff-bit handling on the next fetch.
        }

        private void FetchNextByte()
        {
            if (_pos >= _end)
                throw new EndOfStreamException("Packet header bit reader ran past end of input.");

            _currentByte = _data[_pos++];
            if (_previousWasFf)
            {
                // The encoder pre-stuffed a zero bit at the top of this byte to
                // keep the prior 0xFF from being interpreted as a marker. Only
                // 7 usable bits remain (the low 7).
                _bitsLeft = 7;
            }
            else
            {
                _bitsLeft = 8;
            }
            _previousWasFf = _currentByte == 0xFF;
        }
    }
}
