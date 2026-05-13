using System;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Significance Propagation Pass under the selective arithmetic coding
    /// bypass ("LAZY") code-block style, ISO/IEC 15444-1 D.6 / Table D.9.
    /// Coefficient selection mirrors the MQ-coded
    /// <see cref="SignificancePropagationPass"/>: every insignificant
    /// coefficient with at least one significant 8-neighbour is visited. The
    /// difference is purely in how the bits are obtained — instead of MQ
    /// arithmetic decoding the significance bit and sign bit come straight
    /// out of a packed bit stream, and the sign is the raw value (1 means
    /// negative, 0 positive — equation D-2). No XOR with a sign-coding
    /// predictor, no context updates.
    /// </summary>
    internal static class RawSignificancePropagationPass
    {
        public static void Run(Tier1State state, Tier1RawBitReader reader, int bitPlane,
            bool vsc = false)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            if (reader is null) throw new ArgumentNullException(nameof(reader));
            if (bitPlane < 0)
                throw new ArgumentOutOfRangeException(nameof(bitPlane), bitPlane, null);

            int width = state.Width;
            int paddedHeight = state.PaddedHeight;
            int actualHeight = state.Height;

            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                {
                    for (var y = stripeTop; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        bool maskSouth = vsc && (y % 4 == 3);
                        byte neighbourhood = state.GetSignificanceNeighbourhood(x, y, maskSouth);
                        if (neighbourhood == 0) continue;

                        int sigBit = reader.ReadBit();
                        if (sigBit == 1)
                        {
                            int signBit = reader.ReadBit();
                            state.SetFlag(x, y, Tier1State.SignificanceFlag);
                            if (signBit == 1) state.SetFlag(x, y, Tier1State.SignFlag);
                            state.SetMagnitude(x, y, 1 << bitPlane);
                        }
                        state.SetFlag(x, y, Tier1State.VisitedFlag);
                    }
                }
            }
        }
    }
}
