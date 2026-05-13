using System.Collections.Generic;

namespace Jp2Codec.Tests.Tier2
{
    /// <summary>
    /// MSB-first bit writer that mirrors <c>PacketHeaderBitReader</c>'s 0xFF
    /// stuff-bit convention: whenever the writer emits an 0xFF byte, the
    /// next byte is forced to begin with a '0' stuff bit so the reader can
    /// recover the payload exactly.
    /// </summary>
    internal sealed class PacketHeaderBitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _currentByte;
        private int _bitsAvailable = 8; // 7 if previous emitted byte was 0xFF
        private int _bitsUsed;

        public void WriteBit(int bit)
        {
            int bitPos = _bitsAvailable - _bitsUsed - 1;
            _currentByte |= (bit & 1) << bitPos;
            _bitsUsed++;
            if (_bitsUsed == _bitsAvailable) FlushByte();
        }

        public void WriteBits(int value, int numBits)
        {
            for (int i = numBits - 1; i >= 0; i--) WriteBit((value >> i) & 1);
        }

        /// <summary>
        /// Pad the current byte with zero bits and flush. Subsequent writes
        /// land in fresh bytes — matching reader's <c>AlignToByte()</c>.
        /// </summary>
        public void AlignToByte()
        {
            if (_bitsUsed > 0) FlushByte();
        }

        public byte[] ToBytes()
        {
            AlignToByte();
            return _bytes.ToArray();
        }

        private void FlushByte()
        {
            _bytes.Add((byte)_currentByte);
            bool wasFf = _currentByte == 0xFF;
            _currentByte = 0;
            _bitsUsed = 0;
            _bitsAvailable = wasFf ? 7 : 8;
        }
    }
}
