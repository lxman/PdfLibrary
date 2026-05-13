using System;
using Jp2Codec.Mq;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// EBCOT Tier-1 Magnitude Refinement Pass (ISO/IEC 15444-1 D.3.3).
    ///
    /// Walks the code block in stripe-then-column-then-row order. For each
    /// coefficient that is currently significant AND wasn't visited by SPP
    /// in this bit-plane, decodes one MQ bit using the magnitude-refinement
    /// (MR) context — the first refinement of a coefficient picks between
    /// MR ctx 14 and 15 based on whether any 8-neighbour is significant;
    /// every subsequent refinement uses MR ctx 16 (μ flag set). The bit is
    /// OR'd into the magnitude at the current bit-plane position.
    /// </summary>
    internal static class MagnitudeRefinementPass
    {
        public static void Run(
            Tier1State state,
            Jp2MqDecoder mq,
            byte[] contexts,
            int bitPlane,
            bool vsc = false)
        {
            if (state is null) throw new ArgumentNullException(nameof(state));
            if (mq is null) throw new ArgumentNullException(nameof(mq));
            if (contexts is null) throw new ArgumentNullException(nameof(contexts));
            if (contexts.Length != Jp2MqContextSet.Count)
                throw new ArgumentException(
                    $"contexts must have {Jp2MqContextSet.Count} entries; got {contexts.Length}.",
                    nameof(contexts));
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

                        bool alreadyRefined = state.HasFlag(x, y, Tier1State.RefinedFlag);
                        bool maskSouth = vsc && (y % 4 == 3);
                        int neighbourCount = state.CountSignificantNeighbours(x, y, maskSouth);
                        int mrContext =
                            Tier1Contexts.MagnitudeRefinement(alreadyRefined, neighbourCount);
                        int bit = mq.Decode(ref contexts[mrContext]);

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
