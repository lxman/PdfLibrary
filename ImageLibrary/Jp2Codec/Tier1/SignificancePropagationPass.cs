using System;
using Jp2Codec.Mq;

namespace Jp2Codec.Tier1
{
    /// <summary>
    /// EBCOT Tier-1 Significance Propagation Pass (ISO/IEC 15444-1 D.3.1).
    ///
    /// Walks the code block in stripe-then-column-then-row order. For each
    /// coefficient that is currently insignificant AND has at least one
    /// significant 8-neighbour, decodes one MQ bit using the zero-coding
    /// (ZC) context derived from the neighbour pattern. If that bit is 1,
    /// the coefficient becomes significant, the magnitude bit at the
    /// current bit-plane position is set, and a sign bit is decoded using
    /// the SC context derived from the cardinal neighbours' sign and
    /// significance state. Either way the coefficient is marked visited so
    /// the magnitude-refinement and cleanup passes know SPP already
    /// processed it this bit-plane.
    /// </summary>
    internal static class SignificancePropagationPass
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
                for (var x = 0; x < width; x++)
                {
                    for (int y = stripeTop; y < stripeBottom; y++)
                    {
                        int idx = state.RowBase(y) + x;
                        if ((flags[idx] & Tier1State.SignificanceFlag) != 0) continue;

                        bool maskSouth = vsc && (y % 4 == 3);
                        byte neighbourhood = maskSouth
                            ? Tier1State.GetNeighbourhoodFastMaskSouth(flags, idx, stride)
                            : Tier1State.GetNeighbourhoodFast(flags, idx, stride);
                        if (neighbourhood == 0) continue;

                        int zcContext = Tier1Contexts.ZeroCoding(orientation, neighbourhood);
                        int sigBit = mq.Decode(ref contexts[zcContext]);

                        if (sigBit == 1)
                        {
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

                        flags[idx] |= Tier1State.VisitedFlag;
                    }
                }
            }
        }
    }
}
