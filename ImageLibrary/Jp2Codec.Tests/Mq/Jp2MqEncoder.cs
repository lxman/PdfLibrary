using Jp2Codec.Mq;

namespace Jp2Codec.Tests.Mq
{
    /// <summary>
    /// Test-only MQ encoder mirroring <c>Jp2MqDecoder</c> per ISO/IEC 15444-1
    /// Annex C.2. Used by Tier-1 pass tests to hand-build "encode this bit
    /// stream against these contexts" inputs that the decoder under test then
    /// has to decode back. Not shipped — production has no need to encode.
    /// Carry / stuff-bit handling adapted from the OpenJPEG reference encoder.
    /// </summary>
    internal sealed class Jp2MqEncoder
    {
        // The encoder buffers one byte ahead because a later carry can
        // increment the most-recently-written byte. OpenJPEG implements this
        // by initialising bp to one before the buffer start and reading the
        // OOB byte; we mirror that semantically with an explicit sentinel
        // byte that absorbs any carry happening on the very first byteout.
        // _bp == -1 means "we're still buffered on the sentinel".
        private readonly byte[] _buffer;
        private byte _sentinel;
        private int _bp = -1;
        private uint _c;
        private uint _a;
        private int _ct;

        public Jp2MqEncoder(int capacity = 65536)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _buffer = new byte[capacity];
            InitEnc();
        }

        public int BytesEmitted => _bp + 1;

        private void InitEnc()
        {
            _a = 0x8000u;
            _c = 0u;
            _ct = 12;
        }

        public void Encode(int d, ref byte cx)
        {
            if (d != 0 && d != 1) throw new ArgumentOutOfRangeException(nameof(d));
            var index = (byte)(cx & 0x7F);
            int mps = (cx >> 7) & 1;
            uint qe = QeTable.Qe[index];

            if (d == mps) CodeMps(ref index, qe);
            else CodeLps(ref index, ref mps, qe);

            cx = (byte)((mps << 7) | index);
        }

        private void CodeMps(ref byte index, uint qe)
        {
            _a -= qe;
            if ((_a & 0x8000u) == 0)
            {
                if (_a < qe) _a = qe;
                else _c += qe;
                index = QeTable.NMPS[index];
                Renorme();
            }
            else
            {
                _c += qe;
            }
        }

        private void CodeLps(ref byte index, ref int mps, uint qe)
        {
            _a -= qe;
            if (_a < qe) _c += qe;
            else _a = qe;
            if (QeTable.Switch[index]) mps = 1 - mps;
            index = QeTable.NLPS[index];
            Renorme();
        }

        private void Renorme()
        {
            do
            {
                _a <<= 1;
                _c <<= 1;
                _ct--;
                if (_ct == 0) ByteOut();
            } while ((_a & 0x8000u) == 0);
        }

        private byte CurrentBufferedByte() => _bp < 0 ? _sentinel : _buffer[_bp];

        private void IncrementCurrentBufferedByte()
        {
            if (_bp < 0) _sentinel++;
            else _buffer[_bp]++;
        }

        private void ByteOut()
        {
            byte b = CurrentBufferedByte();

            if (b == 0xFF)
            {
                // Stuff-byte path — the previous emitted (or sentinel) byte
                // is 0xFF, so the next emitted byte borrows its top bit as a
                // stuff bit. The sentinel taking this path is benign: the
                // decoder won't see a corresponding 0xFF in its input, but
                // the sentinel itself is never emitted.
                _bp++;
                _buffer[_bp] = (byte)(_c >> 20);
                _c &= 0xFFFFFu;
                _ct = 7;
                return;
            }

            if ((_c & 0x8000000u) != 0)
            {
                // Carry — bit 27 of C propagates into the previously
                // buffered byte (sentinel or real byte).
                IncrementCurrentBufferedByte();
                _c &= 0x7FFFFFFu;
                if (CurrentBufferedByte() == 0xFF)
                {
                    _bp++;
                    _buffer[_bp] = (byte)(_c >> 20);
                    _c &= 0xFFFFFu;
                    _ct = 7;
                    return;
                }
            }

            _bp++;
            _buffer[_bp] = (byte)(_c >> 19);
            _c &= 0x7FFFFu;
            _ct = 8;
        }

        /// <summary>
        /// Spec-compliant termination (OpenJPEG's <c>opj_mqc_terminate</c>,
        /// ISO/IEC 15444-1 Annex C.2.9). Adjusts C so the decoder can
        /// unambiguously decode every bit emitted so far, pads the remaining
        /// register bits out, and trims a trailing 0xFF byte.
        /// </summary>
        public void Flush() => FlushCore(trimTrailingFf: true);

        /// <summary>
        /// Predictable-termination flush (PTERM, ISO/IEC 15444-1 D.4.2). Same
        /// register flush as <see cref="Flush"/> but the trailing 0xFF byte
        /// is preserved — the spec forbids further truncation under PTERM so
        /// that decoders can run a consistency check on the residual MQ
        /// register state.
        /// </summary>
        public void FlushPredictable() => FlushCore(trimTrailingFf: false);

        private void FlushCore(bool trimTrailingFf)
        {
            uint tempC = _c + _a;
            _c |= 0xFFFFu;
            if (_c >= tempC) _c -= 0x8000u;
            _c <<= _ct;
            ByteOut();
            _c <<= _ct;
            ByteOut();

            if (trimTrailingFf && _bp >= 0 && _buffer[_bp] == 0xFF) _bp--;
        }

        public byte[] ToArray()
        {
            int len = _bp + 1;
            var copy = new byte[len];
            Array.Copy(_buffer, 0, copy, 0, len);
            return copy;
        }
    }
}
