using System;
using Jp2Codec.Mq;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Pure-function context formation for the three EBCOT coding passes
    /// (ISO/IEC 15444-1 Annex D.5). Each method maps the relevant Tier-1
    /// state — neighbour-significance pattern, subband orientation,
    /// neighbour signs, refinement history — to an absolute MQ context
    /// index into the 19-entry table allocated by
    /// <see cref="Jp2MqContextSet"/>.
    /// </summary>
    internal static class Tier1Contexts
    {
        // Build the zero-coding lookup tables once at class load. The tables
        // are indexed by the 8-bit neighbourhood produced by
        // Tier1State.GetSignificanceNeighbourhood(...) — bit 0 = NW,
        // bit 1 = N, bit 2 = NE, bit 3 = W, bit 4 = E, bit 5 = SW,
        // bit 6 = S, bit 7 = SE — and yield a context index in 0..8.
        private static readonly byte[] ZcLlLh = BuildZcLlLh();
        private static readonly byte[] ZcHl   = BuildZcHl();
        private static readonly byte[] ZcHh   = BuildZcHh();

        /// <summary>
        /// Zero-coding context (Annex D.5.1, Tables D-1..D-3). The returned
        /// index is in [<see cref="Jp2MqContextSet.ZeroCoding"/>,
        /// <see cref="Jp2MqContextSet.ZeroCoding"/> + 8].
        /// </summary>
        public static int ZeroCoding(SubbandOrientation orientation, byte neighbourhood)
        {
            byte[] table = orientation switch
            {
                SubbandOrientation.LL => ZcLlLh,
                SubbandOrientation.LH => ZcLlLh,
                SubbandOrientation.HL => ZcHl,
                SubbandOrientation.HH => ZcHh,
                _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null),
            };
            return Jp2MqContextSet.ZeroCoding + table[neighbourhood];
        }

        /// <summary>
        /// Sign-coding context and the XOR bit that flips the raw decoded
        /// sign decision into the actual sign (Annex D.5.2 / Table D-4).
        /// <paramref name="hContribution"/> = clamp(-1..+1) of the sum of
        /// the W and E neighbours' sign-contribution trits;
        /// <paramref name="vContribution"/> = same for N and S. Each
        /// neighbour contributes 0 if insignificant, +1 if significant
        /// positive, −1 if significant negative — matches
        /// <see cref="Tier1State.GetSignContribution"/>.
        /// Returns (context index in [9, 13], xor bit ∈ {0, 1}).
        /// </summary>
        // Table D-4 packed as (offset, xorBit) indexed by [h+1, v+1].
        // Row = h ∈ {-1, 0, +1} → index 0..2; col = v ∈ {-1, 0, +1} → index 0..2.
        private static readonly byte[] ScOffset =
        {
            // h=-1: v=-1, v=0, v=+1
            4, 3, 2,
            // h=0:  v=-1, v=0, v=+1
            1, 0, 1,
            // h=+1: v=-1, v=0, v=+1
            2, 3, 4,
        };

        private static readonly byte[] ScXor =
        {
            // h=-1: v=-1, v=0, v=+1
            1, 1, 1,
            // h=0:  v=-1, v=0, v=+1
            1, 0, 0,
            // h=+1: v=-1, v=0, v=+1
            0, 0, 0,
        };

        public static (int Context, int XorBit) SignCoding(int hContribution, int vContribution)
        {
            uint h = (uint)(hContribution + 1);
            uint v = (uint)(vContribution + 1);
            if (h > 2) throw new ArgumentOutOfRangeException(nameof(hContribution), hContribution, null);
            if (v > 2) throw new ArgumentOutOfRangeException(nameof(vContribution), vContribution, null);
            int tableIdx = (int)(h * 3 + v);
            return (Jp2MqContextSet.SignCoding + ScOffset[tableIdx], ScXor[tableIdx]);
        }

        /// <summary>
        /// Magnitude-refinement context (Annex D.5.3 / Table D-5). Once a
        /// coefficient has been refined at least once (μ = 1) it always
        /// uses context 16 regardless of neighbours; on the very first
        /// refinement the choice between 14 and 15 depends on whether any
        /// 8-neighbour is significant.
        /// </summary>
        public static int MagnitudeRefinement(bool alreadyRefined, int significantNeighbourCount)
        {
            if (significantNeighbourCount < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(significantNeighbourCount), significantNeighbourCount, null);

            if (alreadyRefined)
                return Jp2MqContextSet.MagnitudeRefinement + 2; // context 16
            return Jp2MqContextSet.MagnitudeRefinement +
                   (significantNeighbourCount == 0 ? 0 : 1);   // 14 or 15
        }

        // ---- Table builders ------------------------------------------------

        // Counts in {0, 1, ≥2} for H and V neighbour-significance, and in
        // {0, 1, ≥2} (or larger as the tables specify) for D.
        private static int Cap2(int n) => n > 2 ? 2 : n;

        private static (int H, int V, int D) Decompose(byte neighbourhood)
        {
            // Bit layout: 0=NW, 1=N, 2=NE, 3=W, 4=E, 5=SW, 6=S, 7=SE.
            int n  = (neighbourhood >> 1) & 1;
            int s  = (neighbourhood >> 6) & 1;
            int w  = (neighbourhood >> 3) & 1;
            int e  = (neighbourhood >> 4) & 1;
            int nw = (neighbourhood     ) & 1;
            int ne = (neighbourhood >> 2) & 1;
            int sw = (neighbourhood >> 5) & 1;
            int se = (neighbourhood >> 7) & 1;
            return (w + e, n + s, nw + ne + sw + se);
        }

        private static byte[] BuildZcLlLh()
        {
            // Table D-1.
            var t = new byte[256];
            for (var n = 0; n < 256; n++)
            {
                (int h, int v, int d) = Decompose((byte)n);
                int hc = Cap2(h), vc = Cap2(v), dc = Cap2(d);
                byte ctx;
                if      (hc == 2)            ctx = 8;
                else if (hc == 1 && vc >= 1) ctx = 7;
                else if (hc == 1 && dc >= 1) ctx = 6;
                else if (hc == 1)            ctx = 5;
                else if (vc == 2)            ctx = 4;
                else if (vc == 1)            ctx = 3;
                else if (dc >= 2)            ctx = 2;
                else if (dc == 1)            ctx = 1;
                else                         ctx = 0;
                t[n] = ctx;
            }
            return t;
        }

        private static byte[] BuildZcHl()
        {
            // Table D-2 — same shape as D-1 with H and V swapped.
            var t = new byte[256];
            for (var n = 0; n < 256; n++)
            {
                (int h, int v, int d) = Decompose((byte)n);
                int hc = Cap2(h), vc = Cap2(v), dc = Cap2(d);
                byte ctx;
                if      (vc == 2)            ctx = 8;
                else if (vc == 1 && hc >= 1) ctx = 7;
                else if (vc == 1 && dc >= 1) ctx = 6;
                else if (vc == 1)            ctx = 5;
                else if (hc == 2)            ctx = 4;
                else if (hc == 1)            ctx = 3;
                else if (dc >= 2)            ctx = 2;
                else if (dc == 1)            ctx = 1;
                else                         ctx = 0;
                t[n] = ctx;
            }
            return t;
        }

        private static byte[] BuildZcHh()
        {
            // Table D-3 — diagonal-favoured.
            var t = new byte[256];
            for (var n = 0; n < 256; n++)
            {
                (int h, int v, int d) = Decompose((byte)n);
                int hv = h + v;
                int hvc = hv > 2 ? 2 : hv; // capped at 2
                int dc;
                if      (d == 0) dc = 0;
                else if (d == 1) dc = 1;
                else if (d == 2) dc = 2;
                else             dc = 3;

                byte ctx;
                if (dc >= 3)
                    ctx = 8;
                else if (dc == 2)
                    ctx = (byte)(hvc >= 1 ? 7 : 6);
                else if (dc == 1)
                    ctx = hvc switch { 0 => 3, 1 => 4, _ => 5 };
                else // dc == 0
                    ctx = hvc switch { 0 => 0, 1 => 1, _ => 2 };
                t[n] = ctx;
            }
            return t;
        }
    }
}
