using System.Collections.Generic;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// MSB-first bit writer mirroring <c>Tier1RawBitReader</c>. Implements the
    /// JPEG 2000 raw-stream stuff-bit rule (ISO/IEC 15444-1 D.6): whenever a
    /// just-emitted byte equals 0xFF the NEXT byte's MSB is a forced 0 stuff
    /// bit, so the caller's bits start at bit position 6 of the new byte
    /// rather than 7. Used to hand-build raw segments for round-trip tests.
    /// </summary>
    internal sealed class Tier1RawBitWriter
    {
        private readonly List<byte> _bytes = new();
        private byte _current;
        private int _bitsWritten;
        private int _bitsCapacity = 8;

        public void WriteBit(int bit)
        {
            int position = _bitsCapacity - 1 - _bitsWritten;
            _current |= (byte)((bit & 1) << position);
            _bitsWritten++;
            if (_bitsWritten == _bitsCapacity) Emit();
        }

        /// <summary>
        /// Flush any partially-filled byte. The remaining low-order bits are
        /// already zero (we OR'd in bits MSB-first), so this is the byte
        /// boundary the spec requires after the MRP of a raw segment.
        /// </summary>
        public void Flush()
        {
            if (_bitsWritten > 0) Emit();
        }

        /// <summary>
        /// PTERM-style flush (ISO/IEC 15444-1 D.6): fill the remaining bits
        /// of the partial byte with an alternating 0/1 sequence starting
        /// from 0 — i.e., 0, 1, 0, 1, … — rather than zero-padding. The
        /// pattern is decoder-verifiable for corruption detection. If the
        /// byte is already full this is a no-op.
        /// </summary>
        public void FlushAlternatingPad()
        {
            if (_bitsWritten == 0) return;

            int padBits = _bitsCapacity - _bitsWritten;
            for (var i = 0; i < padBits; i++) WriteBit(i & 1);
            // WriteBit auto-emits once the partial byte fills; no explicit
            // Emit needed here.
        }

        private void Emit()
        {
            _bytes.Add(_current);
            bool wasFf = _current == 0xFF;
            _current = 0;
            _bitsWritten = 0;
            _bitsCapacity = wasFf ? 7 : 8;
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
