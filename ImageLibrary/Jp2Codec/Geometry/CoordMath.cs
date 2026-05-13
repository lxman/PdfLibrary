using System;

namespace Jp2Codec.Geometry
{
    /// <summary>
    /// Canvas-coordinate arithmetic used throughout ISO/IEC 15444-1 Annex B
    /// for the tile / tile-component / resolution / subband ceiling-division
    /// chain. The spec is written in terms of <c>ceil</c> / <c>floor</c> over
    /// non-negative integers — C# integer division truncates toward zero, so
    /// these helpers are non-trivial when subband-shifted positions go below
    /// zero (which happens for HL/LH/HH bands whose 2^(n_b-1) offset exceeds
    /// the tile-component origin).
    /// </summary>
    internal static class CoordMath
    {
        /// <summary>
        /// <c>ceil(value / 2^exponent)</c> for signed <paramref name="value"/>.
        /// Required by the subband-canvas equations B-15 / B-16, which apply
        /// <c>value = tcx0 - 2^(n_b-1)</c> and can therefore be negative.
        /// </summary>
        public static int CeilDivPow2(int value, int exponent)
        {
            if (exponent < 0)
                throw new ArgumentOutOfRangeException(nameof(exponent), exponent, null);
            if (exponent == 0) return value;
            // ceil(v / 2^e) = (v + 2^e - 1) >> e for unsigned. For signed the
            // arithmetic right shift carries the sign correctly because
            //   v + (2^e - 1) keeps the sign of v for v <= 0
            //   and for v >= 0 it bumps up by (2^e - 1) before shifting.
            int divisorMinusOne = (1 << exponent) - 1;
            // Use long to avoid overflow when value is near int.MaxValue and
            // we add (2^e - 1). Codestream values are bounded by Xsiz/Ysiz so
            // they fit comfortably in int once the addition is done in long.
            long sum = (long)value + divisorMinusOne;
            return (int)(sum >> exponent);
        }

        /// <summary>
        /// <c>floor(value / 2^exponent)</c> for signed <paramref name="value"/>.
        /// Equivalent to arithmetic right shift on a two's-complement int.
        /// </summary>
        public static int FloorDivPow2(int value, int exponent)
        {
            if (exponent < 0)
                throw new ArgumentOutOfRangeException(nameof(exponent), exponent, null);
            return value >> exponent;
        }

        /// <summary>
        /// <c>ceil(num / den)</c> for non-negative <paramref name="num"/> and
        /// positive <paramref name="den"/>. Used for component-grid coordinates
        /// (B.5) where XRsiz / YRsiz are 1..255.
        /// </summary>
        public static int CeilDiv(int num, int den)
        {
            if (num < 0) throw new ArgumentOutOfRangeException(nameof(num), num, null);
            if (den <= 0) throw new ArgumentOutOfRangeException(nameof(den), den, null);
            return (num + den - 1) / den;
        }

        /// <summary>
        /// <c>ceil(num / den)</c> for the unsigned versions of the SIZ fields,
        /// returning int. Throws on overflow.
        /// </summary>
        public static int CeilDivUnsigned(uint num, uint den)
        {
            if (den == 0) throw new ArgumentOutOfRangeException(nameof(den));
            ulong r = (num + (ulong)den - 1UL) / den;
            return checked((int)r);
        }
    }
}
