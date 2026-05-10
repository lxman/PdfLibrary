using System;

namespace Jbig2Decoder.Huffman
{
    /// <summary>
    /// MSB-first bit reader over a contiguous byte buffer. Used by the
    /// Huffman-coded paths in JBIG2 (T.88 §B.4 step 1: read one bit at a time
    /// until the prefix matches a table entry).
    ///
    /// Distinct from <see cref="Mq.MqDecoder"/> at the bottom of the arithmetic
    /// stack: no byte stuffing, no marker handling — the JBIG2 Huffman path
    /// requires neither.
    /// </summary>
    internal sealed class HuffmanBitReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _byteIndex;
        private int _bitIndex;     // 0 = MSB of current byte

        public HuffmanBitReader(byte[] data, int offset, int length)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _byteIndex = offset;
            _end = offset + length;
            _bitIndex = 0;
        }

        public int Offset => _byteIndex;
        public int BitOffset => _bitIndex;

        /// <summary>
        /// Read up to 32 bits MSB-first. Bits past end of buffer are returned as 0.
        /// </summary>
        public uint ReadBits(int count)
        {
            if (count < 0 || count > 32) throw new ArgumentOutOfRangeException(nameof(count));
            uint result = 0;
            while (count > 0)
            {
                if (_byteIndex >= _end)
                {
                    result <<= count;
                    return result;
                }
                int avail = 8 - _bitIndex;
                int take = count < avail ? count : avail;
                int b = _data[_byteIndex];
                int shifted = (b >> (avail - take)) & ((1 << take) - 1);
                result = (result << take) | (uint)shifted;
                _bitIndex += take;
                if (_bitIndex == 8) { _bitIndex = 0; _byteIndex++; }
                count -= take;
            }
            return result;
        }

        /// <summary>Skip to the next byte boundary if not already aligned.</summary>
        public void AlignToByte()
        {
            if (_bitIndex != 0) { _bitIndex = 0; _byteIndex++; }
        }

        /// <summary>Advance the byte cursor by <paramref name="bytes"/>, resetting bit alignment.</summary>
        public void Advance(int bytes)
        {
            AlignToByte();
            _byteIndex += bytes;
        }
    }
}
