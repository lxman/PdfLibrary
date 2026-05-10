using System;
using Jbig2Decoder.Mq;

namespace Jbig2Decoder.Arith
{
    /// <summary>
    /// Symbol-ID arithmetic decoder defined by ITU-T T.88 §A.3 (IAID procedure).
    ///
    /// Used by text regions to decode the indices of symbol instances drawn from
    /// the symbol dictionary. The bit-length of each ID is fixed within a region
    /// (<c>SBSYMCODELEN</c>), so the algorithm is simply: maintain a 9-bit-wide
    /// "previous bits" register PREV initialised to 1, decode SBSYMCODELEN bits
    /// each in a context selected by the current PREV, and the resulting ID is
    /// PREV minus its leading 1 marker.
    ///
    /// Each instance owns a 2^SBSYMCODELEN-byte context array. Per the spec,
    /// each symbol-ID-decoding region holds its own context set.
    /// </summary>
    internal sealed class IaidDecoder
    {
        private readonly MqDecoder _mq;
        private readonly byte[] _ctx;
        private readonly int _codeLength;

        public IaidDecoder(MqDecoder mq, int codeLength)
        {
            // SBSYMCODELEN = 0 is legal (and required) when SBNUMSYMS = 1: the loop
            // runs zero times and the procedure deterministically returns 0 without
            // touching the MQ state. Forcing a min of 1 here would consume a stray
            // MQ bit per instance and desynchronise downstream decoders.
            if (codeLength < 0 || codeLength > 30)
                throw new ArgumentOutOfRangeException(nameof(codeLength), "SBSYMCODELEN must be in [0, 30]");

            _mq = mq;
            _codeLength = codeLength;
            // Context array is sized 2^codeLength but PREV walks indices 1..(2^L)-1
            // during decode; with codeLength=0 the loop is skipped entirely so a
            // 1-byte allocation is the minimum we need (Annex A.3 step (1) sets
            // PREV=1 before the unused descent).
            _ctx = new byte[Math.Max(1, 1 << codeLength)];
        }

        /// <summary>
        /// Decode a single symbol ID. Result is in [0, 2^codeLength).
        /// When codeLength is 0, returns 0 without consuming any MQ bits.
        /// </summary>
        public int Decode()
        {
            if (MqTrace.Enabled) MqTrace.LogEnter("IAID");
            // T.88 §A.3 (1) — initial PREV register.
            var prev = 1;

            // T.88 §A.3 (2) — fixed-length unary descent.
            for (var i = 0; i < _codeLength; i++)
            {
                int bit = _mq.Decode(ref _ctx[prev]);
                prev = (prev << 1) | bit;
            }

            // T.88 §A.3 (3) — strip the leading 1.
            return prev - (1 << _codeLength);
        }
    }
}
