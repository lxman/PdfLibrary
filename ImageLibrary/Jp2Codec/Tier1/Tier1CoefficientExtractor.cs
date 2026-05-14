using System;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Converts a finished EBCOT Tier-1 state grid into a rectangular grid
    /// of signed quantized coefficient indices (ISO/IEC 15444-1 D.2).
    /// Tier-1's output is the quantized index q such that q == 0 iff the
    /// coefficient is insignificant; otherwise the magnitude is the
    /// accumulated MSB-first bit pattern stored by the passes and the sign
    /// is taken from the sign flag (cleared = positive, set = negative).
    /// Dequantization (Annex E) consumes these signed indices and produces
    /// the reconstructed subband samples; the mid-point reconstruction bias
    /// is applied there, not here.
    ///
    /// Output shape is <c>[Height, Width]</c> indexed <c>[y, x]</c>; the
    /// padded rows the state keeps for stripe-aligned pass walks are NOT
    /// included.
    /// </summary>
    internal static class Tier1CoefficientExtractor
    {
        public static int[,] Extract(Tier1State state)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));

            int width = state.Width;
            int height = state.Height;
            byte[] flags = state._flags;
            int[] magnitudes = state._magnitudes;
            var output = new int[height, width];
            for (var y = 0; y < height; y++)
            {
                int rowBase = state.RowBase(y);
                for (var x = 0; x < width; x++)
                {
                    int idx = rowBase + x;
                    byte f = flags[idx];
                    if ((f & Tier1State.SignificanceFlag) == 0) continue;
                    int magnitude = magnitudes[idx];
                    output[y, x] = (f & Tier1State.SignFlag) != 0 ? -magnitude : magnitude;
                }
            }
            return output;
        }
    }
}
