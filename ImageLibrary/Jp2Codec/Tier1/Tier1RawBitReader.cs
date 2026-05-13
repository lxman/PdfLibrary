using System;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// MSB-first bit reader used by the selective arithmetic coding bypass
    /// ("LAZY") code-block style — ISO/IEC 15444-1 D.6. When LAZY is active the
    /// significance-propagation and magnitude-refinement passes for the 5th
    /// non-zero bit-plane and beyond are coded as plain bits packed into bytes
    /// rather than driven through MQ; the cleanup passes continue on MQ.
    ///
    /// Bit stuffing follows the MQ encoder's rule: any byte whose value is
    /// 0xFF is followed by a stuff bit at the MSB of the NEXT byte (always 0),
    /// preventing the raw stream from ever forming a marker prefix. The reader
    /// transparently discards that stuff bit so the upper layer sees only the
    /// 7 usable bits in the byte after a 0xFF.
    ///
    /// Reading past the end of the segment returns 0. JPEG 2000 raw passes are
    /// coefficient-driven (the pass loop knows in advance how many bits it
    /// needs), so a well-formed encoder always pads the segment to a byte
    /// boundary that comfortably outlasts the decoder's bit demand; falling
    /// back to 0 is a safe default for malformed inputs.
    /// </summary>
    internal sealed class Tier1RawBitReader
    {
        private readonly byte[] _data;
        private readonly int _bpStart;
        private readonly int _bpEnd;
        private int _bp;
        private byte _b;
        private int _bitsLeft;
        private bool _prevWas0xFF;

        public Tier1RawBitReader(byte[] data, int offset, int length)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > data.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            _data = data;
            _bpStart = offset;
            _bpEnd = offset + length;
            _bp = offset;
            _bitsLeft = 0;
            _prevWas0xFF = false;
        }

        public int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                if (_bp >= _bpEnd) return 0;
                _b = _data[_bp++];
                _bitsLeft = _prevWas0xFF ? 7 : 8;
                _prevWas0xFF = _b == 0xFF;
            }
            int bit = (_b >> (_bitsLeft - 1)) & 1;
            _bitsLeft--;
            return bit;
        }

        internal int BytesConsumed => _bp - _bpStart;
    }
}
