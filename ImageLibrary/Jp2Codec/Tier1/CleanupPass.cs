using System;
using Jp2Codec.Mq;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// EBCOT Tier-1 Cleanup Pass (ISO/IEC 15444-1 D.3.4 / D.5.4).
    ///
    /// Walks the code block in stripe-then-column-then-row order. For each
    /// 4-row column where every sample is currently insignificant, unvisited
    /// (i.e. SPP didn't touch it) AND has no significant 8-neighbour, the
    /// pass uses run-length aggregation: one MQ bit on the run-length
    /// context (ctx 17) signals "any sample becomes significant?". If yes,
    /// two MQ bits on the uniform context (ctx 18) give the row index k of
    /// the first newly-significant sample, MSB first; rows above k stay
    /// insignificant; row k gets its sign decoded; rows below k continue
    /// through the per-sample path. Outside RL mode every unvisited
    /// insignificant sample decodes its significance bit using the same
    /// zero-coding context SPP would use, independently of whether its
    /// neighbourhood is empty.
    ///
    /// CUP does NOT mark samples visited — the visited flag is reset by the
    /// pass driver between bit-planes.
    /// </summary>
    internal static class CleanupPass
    {
        public static void Run(
            Tier1State state,
            Jp2MqDecoder mq,
            byte[] contexts,
            SubbandOrientation orientation,
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
                int stripeHeight = stripeBottom - stripeTop;

                for (var x = 0; x < width; x++)
                {
                    int processStartY = stripeTop;

                    if (stripeHeight == 4 && IsRunLengthEligible(state, x, stripeTop, vsc))
                    {
                        int rlBit = mq.Decode(ref contexts[Jp2MqContextSet.RunLength]);
                        if (rlBit == 0) continue;

                        int high = mq.Decode(ref contexts[Jp2MqContextSet.Uniform]);
                        int low = mq.Decode(ref contexts[Jp2MqContextSet.Uniform]);
                        int k = (high << 1) | low;

                        int firstSigY = stripeTop + k;
                        DecodeNewSignificance(state, mq, contexts, x, firstSigY, bitPlane, vsc);
                        processStartY = firstSigY + 1;
                    }

                    for (var y = processStartY; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;

                        bool maskSouth = vsc && (y % 4 == 3);
                        byte neighbourhood = state.GetSignificanceNeighbourhood(x, y, maskSouth);
                        int zcContext = Tier1Contexts.ZeroCoding(orientation, neighbourhood);
                        int sigBit = mq.Decode(ref contexts[zcContext]);
                        if (sigBit == 1)
                            DecodeNewSignificance(state, mq, contexts, x, y, bitPlane, vsc);
                    }
                }
            }
        }

        private static bool IsRunLengthEligible(Tier1State state, int x, int stripeTop, bool vsc)
        {
            for (var y = stripeTop; y < stripeTop + 4; y++)
            {
                if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) return false;
                if (state.HasFlag(x, y, Tier1State.VisitedFlag)) return false;
                bool maskSouth = vsc && (y % 4 == 3);
                if (state.GetSignificanceNeighbourhood(x, y, maskSouth) != 0) return false;
            }
            return true;
        }

        private static void DecodeNewSignificance(
            Tier1State state, Jp2MqDecoder mq, byte[] contexts,
            int x, int y, int bitPlane, bool vsc)
        {
            bool maskSouth = vsc && (y % 4 == 3);
            int hContrib = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.West) +
                state.GetSignContribution(x, y, NeighbourDirection.East));
            int southContrib = maskSouth
                ? 0
                : state.GetSignContribution(x, y, NeighbourDirection.South);
            int vContrib = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.North) +
                southContrib);
            (int scContext, int xorBit) =
                Tier1Contexts.SignCoding(hContrib, vContrib);
            int decodedSignBit = mq.Decode(ref contexts[scContext]);
            int sign = decodedSignBit ^ xorBit;

            state.SetFlag(x, y, Tier1State.SignificanceFlag);
            if (sign == 1) state.SetFlag(x, y, Tier1State.SignFlag);
            state.SetMagnitude(x, y, 1 << bitPlane);
        }
    }
}
