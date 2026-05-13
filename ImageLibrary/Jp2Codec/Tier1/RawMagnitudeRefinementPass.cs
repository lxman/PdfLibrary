using System;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// Magnitude Refinement Pass under the selective arithmetic coding bypass
    /// ("LAZY") code-block style, ISO/IEC 15444-1 D.6 / Table D.9. The same
    /// coefficient-selection rules as the MQ-coded
    /// <see cref="MagnitudeRefinementPass"/> apply — significant coefficients
    /// not already visited by SPP this bit-plane get refined — but the bit is
    /// taken directly from the raw bit stream rather than from MQ. No
    /// per-coefficient context lookup; no context updates.
    /// </summary>
    internal static class RawMagnitudeRefinementPass
    {
        public static void Run(Tier1State state, Tier1RawBitReader reader, int bitPlane)
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
                        if (!state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;

                        int bit = reader.ReadBit();
                        int magnitude = state.GetMagnitude(x, y);
                        magnitude |= bit << bitPlane;
                        state.SetMagnitude(x, y, magnitude);
                        state.SetFlag(x, y, Tier1State.RefinedFlag);
                    }
                }
            }
        }
    }
}
