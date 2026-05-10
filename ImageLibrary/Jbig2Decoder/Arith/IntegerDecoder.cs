using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Arith
{
    /// <summary>
    /// Arithmetic integer decoder defined by ITU-T T.88 Annex A.
    ///
    /// Implements the procedure used by all integer arithmetic decoding routines
    /// (IADH/IADW/IADS/IADT/IAFS/IAIT/IAEX/IAAI/IARDH/IARDW/IARDX/IARDY/IARI) —
    /// every <c>IAx</c> in the spec except IAID, which is layered on top of the
    /// MQ decoder differently and lives in its own class.
    ///
    /// Each instance owns a private 512-byte context array, indexed by PREV (the
    /// running 9-bit register that records previously-decoded bits within a single
    /// integer-decode invocation). Per the spec, separate IAx procedures must use
    /// separate context sets, so each procedure type gets its own decoder instance.
    /// </summary>
    internal sealed class IntegerDecoder
    {
        private readonly MqDecoder _mq;
        private readonly byte[] _ctx = new byte[512];
        private readonly string _traceTag;

        public IntegerDecoder(MqDecoder mq, string traceTag = "INT")
        {
            _mq = mq;
            _traceTag = traceTag;
        }

        /// <summary>
        /// Decode a single integer value.
        /// Returns true on a normal value (written to <paramref name="value"/>);
        /// returns false on the out-of-band sentinel (OOB), in which case
        /// <paramref name="value"/> is undefined.
        /// </summary>
        public bool Decode(out int value)
        {
            if (MqTrace.Enabled) MqTrace.LogEnter(_traceTag);
            // T.88 §A.2 — sign + decision-tree prefix + tail bits.
            var prev = 1;
            int s = _mq.Decode(ref _ctx[prev]);
            prev = (prev << 1) | s;

            int nTail, offset;
            int bit = _mq.Decode(ref _ctx[prev]);
            prev = (prev << 1) | bit;
            if (bit == 0)
            {
                nTail = 2; offset = 0;
            }
            else
            {
                bit = _mq.Decode(ref _ctx[prev]);
                prev = (prev << 1) | bit;
                if (bit == 0)
                {
                    nTail = 4; offset = 4;
                }
                else
                {
                    bit = _mq.Decode(ref _ctx[prev]);
                    prev = (prev << 1) | bit;
                    if (bit == 0)
                    {
                        nTail = 6; offset = 20;
                    }
                    else
                    {
                        bit = _mq.Decode(ref _ctx[prev]);
                        prev = (prev << 1) | bit;
                        if (bit == 0)
                        {
                            nTail = 8; offset = 84;
                        }
                        else
                        {
                            bit = _mq.Decode(ref _ctx[prev]);
                            prev = (prev << 1) | bit;
                            if (bit == 0)
                            {
                                nTail = 12; offset = 340;
                            }
                            else
                            {
                                nTail = 32; offset = 4436;
                            }
                        }
                    }
                }
            }

            var v = 0;
            for (var i = 0; i < nTail; i++)
            {
                bit = _mq.Decode(ref _ctx[prev]);
                // Once PREV crosses 256 (the "9th bit set" marker), keep that marker
                // and slide the low 8 bits — gives a fixed-window context history.
                prev = ((prev << 1) & 511) | (prev & 256) | bit;
                v = (v << 1) | bit;
            }

            // Avoid signed overflow on the synthetic max-prefix branch (offset = 4436).
            if (v > int.MaxValue - offset)
                v = int.MaxValue;
            else
                v += offset;

            // Sign + OOB resolution: V if S=0, -V if S=1 and V>0, OOB if S=1 and V=0.
            if (s == 1)
            {
                if (v == 0)
                {
                    value = 0;
                    return false;   // OOB
                }
                v = -v;
            }

            value = v;
            return true;
        }
    }
}
