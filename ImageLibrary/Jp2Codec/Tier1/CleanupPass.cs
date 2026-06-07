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
            byte[] flags = state._flags;
            int[] magnitudes = state._magnitudes;
            int stride = state._stride;
            int magnitudeBit = 1 << bitPlane;

            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                int stripeHeight = stripeBottom - stripeTop;

                for (var x = 0; x < width; x++)
                {
                    int processStartY = stripeTop;

                    if (stripeHeight == 4 && IsRunLengthEligibleFast(flags, state.RowBase(stripeTop) + x, stride, vsc))
                    {
                        int rlBit = mq.Decode(ref contexts[Jp2MqContextSet.RunLength]);
                        if (rlBit == 0) continue;

                        int high = mq.Decode(ref contexts[Jp2MqContextSet.Uniform]);
                        int low = mq.Decode(ref contexts[Jp2MqContextSet.Uniform]);
                        int k = (high << 1) | low;

                        int firstSigY = stripeTop + k;
                        int firstSigIdx = state.RowBase(firstSigY) + x;
                        DecodeNewSignificanceFast(flags, magnitudes, stride, mq, contexts,
                            firstSigIdx, firstSigY, magnitudeBit, vsc);
                        processStartY = firstSigY + 1;
                    }

                    for (int y = processStartY; y < stripeBottom; y++)
                    {
                        int idx = state.RowBase(y) + x;
                        byte f = flags[idx];
                        if ((f & Tier1State.SignificanceFlag) != 0) continue;
                        if ((f & Tier1State.VisitedFlag) != 0) continue;

                        bool maskSouth = vsc && (y % 4 == 3);
                        byte neighbourhood = maskSouth
                            ? Tier1State.GetNeighbourhoodFastMaskSouth(flags, idx, stride)
                            : Tier1State.GetNeighbourhoodFast(flags, idx, stride);
                        int zcContext = Tier1Contexts.ZeroCoding(orientation, neighbourhood);
                        int sigBit = mq.Decode(ref contexts[zcContext]);
                        if (sigBit == 1)
                            DecodeNewSignificanceFast(flags, magnitudes, stride, mq, contexts,
                                idx, y, magnitudeBit, vsc);
                    }
                }
            }
        }

        private static bool IsRunLengthEligibleFast(byte[] flags, int idx0, int stride, bool vsc)
        {
            int idx = idx0;
            for (var row = 0; row < 4; row++)
            {
                byte f = flags[idx];
                if ((f & (Tier1State.SignificanceFlag | Tier1State.VisitedFlag)) != 0) return false;
                bool maskSouth = vsc && (row == 3);
                byte neighbourhood = maskSouth
                    ? Tier1State.GetNeighbourhoodFastMaskSouth(flags, idx, stride)
                    : Tier1State.GetNeighbourhoodFast(flags, idx, stride);
                if (neighbourhood != 0) return false;
                idx += stride;
            }
            return true;
        }

        private static void DecodeNewSignificanceFast(
            byte[] flags, int[] magnitudes, int stride,
            Jp2MqDecoder mq, byte[] contexts,
            int idx, int y, int magnitudeBit, bool vsc)
        {
            bool maskSouth = vsc && (y % 4 == 3);
            int hContrib = Math.Sign(
                Tier1State.GetSignContributionFast(flags, idx - 1) +
                Tier1State.GetSignContributionFast(flags, idx + 1));
            int southContrib = maskSouth
                ? 0
                : Tier1State.GetSignContributionFast(flags, idx + stride);
            int vContrib = Math.Sign(
                Tier1State.GetSignContributionFast(flags, idx - stride) +
                southContrib);
            (int scContext, int xorBit) =
                Tier1Contexts.SignCoding(hContrib, vContrib);
            int decodedSignBit = mq.Decode(ref contexts[scContext]);
            int sign = decodedSignBit ^ xorBit;

            flags[idx] |= Tier1State.SignificanceFlag;
            if (sign == 1) flags[idx] |= Tier1State.SignFlag;
            magnitudes[idx] = magnitudeBit;
        }
    }
}
