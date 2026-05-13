using System;

namespace Jp2Codec.Mq
{
    /// <summary>
    /// MQ-coder decoder for JPEG 2000 (ISO/IEC 15444-1 Annex C). The
    /// arithmetic algorithm is identical to JBIG2's (T.88 Annex E); the only
    /// way JPEG 2000 differs is in what bytes the decoder is fed (a packet
    /// body fragment scoped to a single codeblock) and in the optional
    /// bypass-mode / segmentation-symbol / predictable-termination handling
    /// — those happen at the Tier-1 layer above, not in the MQ core.
    ///
    /// State convention follows the spec:
    ///   A  — interval register (16 bits, kept in low half of <see cref="_a"/>)
    ///   C  — code register (32 bits; conceptually [Chigh:Clow] each 16 bits)
    ///   CT — bit counter for Clow
    ///   BP — pointer into compressed data
    ///
    /// The context state — Qe-table index in the low 7 bits and the MPS sense
    /// in bit 7 — is owned by the caller and passed by ref to each <see cref="Decode"/>
    /// call. This matches the spec's "context CX" abstraction and lets higher
    /// layers store flat arrays of contexts (e.g. <see cref="Jp2MqContextSet"/>
    /// for the 19 codeblock contexts in Table D.7).
    /// </summary>
    internal sealed class Jp2MqDecoder
    {
        private const byte MpsMask = 0x80;
        private const byte IndexMask = 0x7F;

        private readonly byte[] _data;
        private readonly int _bpEnd;
        private int _bp;
        private uint _c;
        private uint _a;
        private int _ct;

        public Jp2MqDecoder(byte[] data, int offset, int length)
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
        /// Decode one binary decision. Returns 0 or 1. <paramref name="cx"/> packs
        /// the Qe-table index in the low 7 bits and the MPS sense in bit 7; both
        /// are updated in place per the probability-estimation state machine
        /// (Table C.2).
        /// </summary>
        public int Decode(ref byte cx)
        {
            // Annex C.3.2 — DECODE procedure.
            var i = (byte)(cx & IndexMask);
            var mps = (byte)((cx & MpsMask) >> 7);
            uint qe = QeTable.Qe[i];
            _a -= qe;

            uint chigh = _c >> 16;
            int d;

            if (chigh < qe)
            {
                d = LpsExchange(ref i, ref mps, qe);
                RenormD();
            }
            else
            {
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
            return d;
        }

        private int MpsExchange(ref byte i, ref byte mps, uint qe)
        {
            // Annex C.3.2 — MPS_EXCHANGE.
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
            // Annex C.3.2 — LPS_EXCHANGE.
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
            // Annex C.3.3 — RENORMD.
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
            // Annex C.3.1 — INITDEC.
            byte first = _bp < _bpEnd ? _data[_bp] : (byte)0xFF;
            _c = (uint)first << 16;
            ByteIn();
            _c <<= 7;
            _ct -= 7;
            _a = 0x8000;
        }

        private void ByteIn()
        {
            // Annex C.3.4 — BYTEIN. Handles the 0xFF + (next > 0x8F) marker case
            // and 0xFF stuff-byte case. Same shape as T.88 BYTEIN.
            if (_bp >= _bpEnd)
            {
                _c += 0xFF00u;
                _ct = 8;
                return;
            }

            byte b = _data[_bp];
            if (b == 0xFF)
            {
                if (_bp + 1 >= _bpEnd || _data[_bp + 1] > 0x8F)
                {
                    _c += 0xFF00u;
                    _ct = 8;
                }
                else
                {
                    _bp++;
                    _c += (uint)_data[_bp] << 9;
                    _ct = 7;
                }
            }
            else
            {
                _bp++;
                // Past the end, the spec feeds a virtual 0xFF byte so the
                // decoder degrades gracefully rather than reading uninitialised
                // memory (Annex C.3.4 — BYTEIN end-of-buffer fallback).
                byte next = _bp < _bpEnd ? _data[_bp] : (byte)0xFF;
                _c += (uint)next << 8;
                _ct = 8;
            }
        }
    }
}
