using System;
using Jp2Codec.Tier1;

namespace Jp2Codec.Quantization
{
    /// <summary>
    /// A subband within a tile-component, identified by its orientation and the
    /// decomposition level n_b at which it was generated (Table F.1).
    /// n_b ranges from N_L (for the deepest LL band and the LH/HL/HH triplet at
    /// that level) down to 1 (for the highest-resolution detail bands).
    /// </summary>
    internal readonly struct SubbandDescriptor : IEquatable<SubbandDescriptor>
    {
        public SubbandOrientation Orientation { get; }

        /// <summary>Decomposition level n_b at which this subband lives (1..N_L).</summary>
        public int DecompositionLevel { get; }

        public SubbandDescriptor(SubbandOrientation orientation, int decompositionLevel)
        {
            if (decompositionLevel < 0)
                throw new ArgumentOutOfRangeException(nameof(decompositionLevel), decompositionLevel, null);
            Orientation = orientation;
            DecompositionLevel = decompositionLevel;
        }

        public bool Equals(SubbandDescriptor other) =>
            Orientation == other.Orientation && DecompositionLevel == other.DecompositionLevel;

        public override bool Equals(object? obj) => obj is SubbandDescriptor d && Equals(d);

        public override int GetHashCode() =>
            unchecked(((int)Orientation * 397) ^ DecompositionLevel);

        public override string ToString() => $"{DecompositionLevel}{Orientation}";

        public static bool operator ==(SubbandDescriptor a, SubbandDescriptor b) => a.Equals(b);
        public static bool operator !=(SubbandDescriptor a, SubbandDescriptor b) => !a.Equals(b);
    }

    /// <summary>
    /// Enumerates the subbands of a tile-component in the order they appear in
    /// QCD/QCC marker segments (ISO/IEC 15444-1 F.3.1):
    /// NLLL, NLHL, NLLH, NLHH, (NL-1)HL, (NL-1)LH, (NL-1)HH, ..., 1HL, 1LH, 1HH.
    /// Total count is 1 + 3·N_L.
    /// </summary>
    internal static class SubbandLayout
    {
        public static SubbandDescriptor[] EnumerateQcdOrder(int numDecompositionLevels)
        {
            if (numDecompositionLevels < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(numDecompositionLevels), numDecompositionLevels, null);

            int nl = numDecompositionLevels;
            var result = new SubbandDescriptor[1 + 3 * nl];
            result[0] = new SubbandDescriptor(SubbandOrientation.LL, nl);
            int idx = 1;
            for (int lev = nl; lev >= 1; lev--)
            {
                result[idx++] = new SubbandDescriptor(SubbandOrientation.HL, lev);
                result[idx++] = new SubbandDescriptor(SubbandOrientation.LH, lev);
                result[idx++] = new SubbandDescriptor(SubbandOrientation.HH, lev);
            }
            return result;
        }

        /// <summary>
        /// log2(gain_b) per Table E.1: LL=0, HL=LH=1, HH=2. Feeds R_b = R_I + log2(gain_b).
        /// </summary>
        public static int Log2Gain(SubbandOrientation orientation) => orientation switch
        {
            SubbandOrientation.LL => 0,
            SubbandOrientation.HL => 1,
            SubbandOrientation.LH => 1,
            SubbandOrientation.HH => 2,
            _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, null),
        };
    }
}
