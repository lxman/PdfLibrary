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
        private static int CountNeighboursFast(byte[] flags, int idx, int stride)
        {
            int count = 0;
            if ((flags[idx - stride - 1] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - stride    ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - stride + 1] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - 1          ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx + 1          ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx + stride - 1] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx + stride    ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx + stride + 1] & Tier1State.SignificanceFlag) != 0) count++;
            return count;
        }

        private static int CountNeighboursFastMaskSouth(byte[] flags, int idx, int stride)
        {
            int count = 0;
            if ((flags[idx - stride - 1] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - stride    ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - stride + 1] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx - 1          ] & Tier1State.SignificanceFlag) != 0) count++;
            if ((flags[idx + 1          ] & Tier1State.SignificanceFlag) != 0) count++;
            return count;
        }

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
            byte[] flags = state._flags;
            int[] magnitudes = state._magnitudes;
            int stride = state._stride;

            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                {
                    for (var y = stripeTop; y < stripeBottom; y++)
                    {
                        int idx = state.RowBase(y) + x;
                        byte f = flags[idx];
                        if ((f & Tier1State.SignificanceFlag) == 0) continue;
                        if ((f & Tier1State.VisitedFlag) != 0) continue;

                        bool alreadyRefined = (f & Tier1State.RefinedFlag) != 0;
                        int neighbourCount;
                        if (alreadyRefined)
                        {
                            neighbourCount = 0;
                        }
                        else
                        {
                            bool maskSouth = vsc && (y % 4 == 3);
                            neighbourCount = maskSouth
                                ? CountNeighboursFastMaskSouth(flags, idx, stride)
                                : CountNeighboursFast(flags, idx, stride);
                        }

                        int mrContext =
                            Tier1Contexts.MagnitudeRefinement(alreadyRefined, neighbourCount);
                        int bit = mq.Decode(ref contexts[mrContext]);

                        magnitudes[idx] |= bit << bitPlane;
                        flags[idx] = (byte)(f | Tier1State.RefinedFlag);
                    }
                }
            }
        }
    }
}
