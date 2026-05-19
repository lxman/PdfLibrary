using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class MagnitudeRefinementPassTests
    {
        [Fact]
        public void Mrp_EmptyState_DecodesNothing()
        {
            var state = new Tier1State(8, 8);
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            byte[] before = (byte[])contexts.Clone();
            var mq = new Jp2MqDecoder(new byte[] { 0x00, 0x00 }, 0, 2);

            MagnitudeRefinementPass.Run(state, mq, contexts, bitPlane: 4);

            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(0, state.GetFlags(x, y));
                Assert.Equal(0, state.GetMagnitude(x, y));
            }
            Assert.Equal(before, contexts);
        }

        [Fact]
        public void Mrp_AllVisitedSignificants_DecodesNothing()
        {
            // Significant coefficients that were processed by SPP this bit-
            // plane (visited flag set) must NOT be refined here.
            var state = new Tier1State(4, 4);
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                state.SetFlag(x, y, Tier1State.SignificanceFlag);
                state.SetFlag(x, y, Tier1State.VisitedFlag);
            }

            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            byte[] before = (byte[])contexts.Clone();
            var mq = new Jp2MqDecoder(new byte[] { 0x00, 0x00 }, 0, 2);

            MagnitudeRefinementPass.Run(state, mq, contexts, bitPlane: 0);

            // No coefficients refined.
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                Assert.False(state.HasFlag(x, y, Tier1State.RefinedFlag));
            Assert.Equal(before, contexts);
        }

        [Fact]
        public void Mrp_RoundTrip_OneSignificantNotVisited_BitIsRefined()
        {
            var pre = new Dictionary<(int, int), bool> { { (1, 1), false /*not visited*/ } };
            RunRoundTrip(
                width: 4, height: 4,
                preSignificant: pre,
                preRefined: new HashSet<(int, int)>(),
                bitPlane: 5,
                pickBit: (_, _) => 1);
        }

        [Fact]
        public void Mrp_RoundTrip_AlreadyRefined_UsesContext16()
        {
            // Coefficient already μ=1 so MR context 16 is used; isolate so
            // neighbourCount has no effect (still ctx 16 either way).
            var pre = new Dictionary<(int, int), bool> { { (2, 2), false } };
            var refined = new HashSet<(int, int)> { (2, 2) };
            RunRoundTrip(
                width: 4, height: 4,
                preSignificant: pre,
                preRefined: refined,
                bitPlane: 2,
                pickBit: (_, _) => 1);
        }

        [Fact]
        public void Mrp_RoundTrip_Random_8x8_MixedRefinedAndUnrefined()
        {
            var rng = new Random(20260512);
            var pre = new Dictionary<(int, int), bool>();
            var refined = new HashSet<(int, int)>();

            for (var i = 0; i < 12; i++)
            {
                int x = rng.Next(0, 8), y = rng.Next(0, 8);
                bool visited = rng.Next(0, 2) == 0; // half visited (skipped by MRP)
                pre[(x, y)] = visited;
                if (rng.Next(0, 2) == 0) refined.Add((x, y));
            }

            RunRoundTrip(
                width: 8, height: 8,
                preSignificant: pre,
                preRefined: refined,
                bitPlane: 6,
                pickBit: (_, _) => rng.Next(0, 2));
        }

        [Fact]
        public void Mrp_RoundTrip_NonStripeAlignedHeight()
        {
            var rng = new Random(7);
            var pre = new Dictionary<(int, int), bool>
            {
                { (0, 0), false }, { (3, 4), false },
                { (5, 2), true },  // visited — should be skipped
            };
            RunRoundTrip(
                width: 6, height: 5,
                preSignificant: pre,
                preRefined: new HashSet<(int, int)> { (0, 0) },
                bitPlane: 3,
                pickBit: (_, _) => rng.Next(0, 2));
        }

        // ---- Helper -----------------------------------------------------

        private static void RunRoundTrip(
            int width, int height,
            Dictionary<(int, int), bool> preSignificant,  // value = visited flag
            HashSet<(int, int)> preRefined,
            int bitPlane,
            Func<int, int, int> pickBit)
        {
            var planState = new Tier1State(width, height);
            foreach ((var coord, bool visited) in preSignificant)
            {
                planState.SetFlag(coord.Item1, coord.Item2, Tier1State.SignificanceFlag);
                if (visited) planState.SetFlag(coord.Item1, coord.Item2, Tier1State.VisitedFlag);
                if (preRefined.Contains(coord))
                    planState.SetFlag(coord.Item1, coord.Item2, Tier1State.RefinedFlag);
                // Seed magnitude as if it had been set significant at some
                // earlier bit-plane (any value > 0 is fine; we'll OR the new
                // bit in).
                planState.SetMagnitude(coord.Item1, coord.Item2, 1 << (bitPlane + 1));
            }

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceMrp(planState, enc, encContexts, bitPlane, pickBit);
            enc.Flush();

            // Build decode-side state with the SAME initial conditions.
            var decodeState = new Tier1State(width, height);
            foreach ((var coord, bool visited) in preSignificant)
            {
                decodeState.SetFlag(coord.Item1, coord.Item2, Tier1State.SignificanceFlag);
                if (visited) decodeState.SetFlag(coord.Item1, coord.Item2, Tier1State.VisitedFlag);
                if (preRefined.Contains(coord))
                    decodeState.SetFlag(coord.Item1, coord.Item2, Tier1State.RefinedFlag);
                decodeState.SetMagnitude(coord.Item1, coord.Item2, 1 << (bitPlane + 1));
            }

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            MagnitudeRefinementPass.Run(decodeState, mq, decContexts, bitPlane);

            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                Assert.True(planState.GetFlags(x, y) == decodeState.GetFlags(x, y),
                    $"flags mismatch at ({x},{y}): plan=0x{planState.GetFlags(x, y):X2}, " +
                    $"dec=0x{decodeState.GetFlags(x, y):X2}");
                Assert.Equal(planState.GetMagnitude(x, y), decodeState.GetMagnitude(x, y));
            }
            Assert.Equal(encContexts, decContexts);
        }

        private static void EncodeReferenceMrp(
            Tier1State state,
            Jp2MqEncoder enc,
            byte[] contexts,
            int bitPlane,
            Func<int, int, int> pickBit)
        {
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
                        int neighbourCount = state.CountSignificantNeighbours(x, y);
                        int mrContext =
                            Tier1Contexts.MagnitudeRefinement(alreadyRefined, neighbourCount);
                        int bit = pickBit(x, y);
                        enc.Encode(bit, ref contexts[mrContext]);

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
