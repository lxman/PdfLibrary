using System;

namespace Jbig2Decoder.Mq
{
    /// <summary>
    /// MQ-coder decoder defined by ITU-T T.88 Annex E (also reused by JPEG 2000 / T.800).
    ///
    /// State convention follows the spec:
    ///   A — interval register (16 bits, kept in low half of <see cref="_a"/>)
    ///   C — code register (32 bits; conceptually [Chigh:Clow] each 16 bits)
    ///   CT — bit counter for Clow
    ///   BP — pointer into compressed data
    ///
    /// The decoder is context-free: a context's state — Qe-table index in the low
    /// 7 bits and the MPS sense in bit 7 — is owned by the caller and passed by
    /// reference to each <see cref="Decode"/> call. This matches the spec's "context
    /// CX" abstraction (and jbig2dec's <c>Jbig2ArithCx</c> packed byte) so higher
    /// layers can store flat arrays of contexts indexed by template position.
    /// </summary>
    internal sealed class MqDecoder
    {
        private const byte MpsMask = 0x80;
        private const byte IndexMask = 0x7F;

        private readonly byte[] _data;
        private readonly int _bpEnd;
        private int _bp;
        private uint _c;
        private uint _a;
        private int _ct;

        public MqDecoder(byte[] data, int offset, int length)
        {
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (offset < 0 || offset > data.Length) throw new ArgumentOutOfRangeException(nameof(offset));
            if (length < 0 || offset + length > data.Length) throw new ArgumentOutOfRangeException(nameof(length));

            _data = data;
            _bp = offset;
            _bpEnd = offset + length;
            InitDec();
        }

        // Exposed for tests / diagnostics — not part of the public protocol.
        internal uint A => _a;
        internal uint C => _c;
        internal int CT => _ct;
        internal int BP => _bp;

        /// <summary>
        /// Decode one binary decision. Returns 0 or 1.
        /// <paramref name="cx"/> packs the Qe-table index in the low 7 bits and the
        /// MPS sense in bit 7; both are updated in place per the probability-estimation
        /// state machine (Table E.1).
        /// </summary>
        public int Decode(ref byte cx)
        {
            byte preCx = cx;

            // T.88 §E.3.2 — DECODE procedure.
            var i = (byte)(cx & IndexMask);
            var mps = (byte)((cx & MpsMask) >> 7);
            uint qe = QeTable.Qe[i];
            _a -= qe;

            uint chigh = _c >> 16;
            int d;

            if (chigh < qe)
            {
                // LPS path candidate (Chigh below the LPS sub-interval boundary)
                d = LpsExchange(ref i, ref mps, qe);
                RenormD();
            }
            else
            {
                // MPS path candidate
                _c -= qe << 16;
                if ((_a & 0x8000u) == 0)
                {
                    d = MpsExchange(ref i, ref mps, qe);
                    RenormD();
                }
                else
                {
                    d = mps;
                }
            }

            cx = (byte)((mps << 7) | i);
            if (MqTrace.Enabled) MqTrace.Log(preCx, d, _a, _ct);
            return d;
        }

        private int MpsExchange(ref byte i, ref byte mps, uint qe)
        {
            // T.88 Figure E.16 — MPS conditional exchange.
            // _a holds (A - Qe) here; the comparison is "MPS sub-interval vs LPS sub-interval".
            byte i0 = i;
            int d;
            if (_a < qe)
            {
                d = 1 - mps;
                if (QeTable.Switch[i0]) mps = (byte)(1 - mps);
                i = QeTable.NLPS[i0];
            }
            else
            {
                d = mps;
                i = QeTable.NMPS[i0];
            }
            return d;
        }

        private int LpsExchange(ref byte i, ref byte mps, uint qe)
        {
            // T.88 Figure E.17 — LPS conditional exchange.
            byte i0 = i;
            int d;
            if (_a < qe)
            {
                _a = qe;
                d = mps;
                i = QeTable.NMPS[i0];
            }
            else
            {
                _a = qe;
                d = 1 - mps;
                if (QeTable.Switch[i0]) mps = (byte)(1 - mps);
                i = QeTable.NLPS[i0];
            }
            return d;
        }

        private void RenormD()
        {
            // T.88 §E.3.3 — RENORMD.
            do
            {
                if (_ct == 0) ByteIn();
                _a <<= 1;
                _c <<= 1;
                _ct--;
            } while ((_a & 0x8000u) == 0);
        }

        private void InitDec()
        {
            // T.88 §E.3.5 — INITDEC.
            // First byte is loaded into the low half of Chigh.
            byte first = _bp < _bpEnd ? _data[_bp] : (byte)0xFF;
            _c = (uint)first << 16;
            ByteIn();
            _c <<= 7;
            _ct -= 7;
            _a = 0x8000;
        }

        private void ByteIn()
        {
            // T.88 §E.3.4 — BYTEIN.
            // BP is currently looking at the byte that was last loaded; this routine inspects it
            // for 0xFF stuffing/marker handling and brings the next byte into Clow.
            if (_bp >= _bpEnd)
            {
                // End of stream — feed virtual 1-bits (the spec's marker-code-completion rule).
                _c += 0xFF00u;
                _ct = 8;
                return;
            }

            byte b = _data[_bp];
            if (b == 0xFF)
            {
                // Peek next byte to decide between marker-code and stuff-bit cases.
                if (_bp + 1 >= _bpEnd || _data[_bp + 1] > 0x8F)
                {
                    // Marker code — leave BP on the 0xFF, feed virtual 1-bits.
                    _c += 0xFF00u;
                    _ct = 8;
                }
                else
                {
                    // Stuff-bit byte follows: advance and load 7 effective bits.
                    _bp++;
                    _c += (uint)_data[_bp] << 9;
                    _ct = 7;
                }
            }
            else
            {
                _bp++;
                // Past the end, the spec feeds a virtual 0xFF byte so the
                // running interval keeps advancing rather than stalling on
                // uninitialised memory (T.88 §E.3.4 end-of-buffer fallback).
                byte next = _bp < _bpEnd ? _data[_bp] : (byte)0xFF;
                _c += (uint)next << 8;
                _ct = 8;
            }
        }
    }
}
