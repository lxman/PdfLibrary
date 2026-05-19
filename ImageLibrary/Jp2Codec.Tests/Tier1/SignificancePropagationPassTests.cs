using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class SignificancePropagationPassTests
    {
        // ---- Trivial cases ------------------------------------------------

        [Fact]
        public void Spp_EmptyState_DecodesNothing_AndLeavesStateUntouched()
        {
            var state = new Tier1State(8, 8);
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            byte[] beforeContexts = (byte[])contexts.Clone();
            // Empty body — would throw if SPP tried to decode anything,
            // because the decoder would walk into virtual 0xFF bytes.
            var mq = new Jp2MqDecoder(new byte[] { 0x00, 0x00 }, 0, 2);

            SignificancePropagationPass.Run(state, mq, contexts, SubbandOrientation.LL, bitPlane: 0);

            for (var y = 0; y < state.Height; y++)
            for (var x = 0; x < state.Width; x++)
            {
                Assert.Equal(0, state.GetFlags(x, y));
                Assert.Equal(0, state.GetMagnitude(x, y));
            }
            Assert.Equal(beforeContexts, contexts);
        }

        [Fact]
        public void Spp_AllAlreadySignificant_DecodesNothing()
        {
            // Every coefficient is already significant — SPP must skip them all.
            var state = new Tier1State(4, 4);
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                state.SetFlag(x, y, Tier1State.SignificanceFlag);

            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            byte[] before = (byte[])contexts.Clone();
            var mq = new Jp2MqDecoder(new byte[] { 0x00, 0x00 }, 0, 2);

            SignificancePropagationPass.Run(state, mq, contexts, SubbandOrientation.LL, bitPlane: 5);

            // No visited flag set anywhere (SPP does not visit pre-significant coefficients).
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
                Assert.False(state.HasFlag(x, y, Tier1State.VisitedFlag));
            Assert.Equal(before, contexts);
        }

        [Fact]
        public void Spp_IsolatedSignificant_HasNoCandidatesBeyondNeighbours()
        {
            // (1, 1) is the only significant coefficient initially.
            // Eligible candidates are exactly its 8 neighbours that are
            // in-bounds and insignificant: (0,0), (1,0), (2,0), (0,1),
            // (2,1), (0,2), (1,2), (2,2). All eight live in stripe 0.
            var state = new Tier1State(4, 4);
            state.SetFlag(1, 1, Tier1State.SignificanceFlag);

            // Encode "0" (insignificant) for each candidate using the right
            // ZC context — no candidate becomes significant; we simply verify
            // SPP marks them visited and walks past correctly.
            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceSpp(
                state, enc, encContexts, SubbandOrientation.LL, bitPlane: 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0);
            enc.Flush();

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);

            SignificancePropagationPass.Run(state, mq, decContexts, SubbandOrientation.LL, bitPlane: 0);

            // All 8 expected candidates marked visited; none became significant.
            (int, int)[] expectedVisited =
            {
                (0, 0), (0, 1), (0, 2),
                (1, 0),         (1, 2),
                (2, 0), (2, 1), (2, 2),
            };
            foreach ((int x, int y) in expectedVisited)
            {
                Assert.True(state.HasFlag(x, y, Tier1State.VisitedFlag),
                    $"({x},{y}) should be visited");
                Assert.False(state.HasFlag(x, y, Tier1State.SignificanceFlag),
                    $"({x},{y}) should remain insignificant");
            }
            // Far corner not touched.
            Assert.False(state.HasFlag(3, 3, Tier1State.VisitedFlag));
            Assert.Equal(encContexts, decContexts);
        }

        // ---- Round-trip cases -------------------------------------------

        [Fact]
        public void Spp_RoundTrip_AllCoefficientsBecomeSignificant_LL()
        {
            RunRoundTrip(
                width: 4, height: 4,
                preSignificant: new[] { (0, 0) },
                preSignSet: new HashSet<(int, int)>(),
                orientation: SubbandOrientation.LL,
                bitPlane: 3,
                pickSig: (_, _) => 1,
                pickSign: (x, y) => (x + y) & 1);
        }

        [Fact]
        public void Spp_RoundTrip_NoCoefficientsBecomeSignificant_HH()
        {
            RunRoundTrip(
                width: 8, height: 8,
                preSignificant: new[] { (4, 4) },
                preSignSet: new HashSet<(int, int)> { (4, 4) },
                orientation: SubbandOrientation.HH,
                bitPlane: 7,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0);
        }

        [Fact]
        public void Spp_RoundTrip_RandomDecisions_LH_8x8()
        {
            var rng = new Random(20260512);
            var pre = new HashSet<(int, int)>();
            // Sprinkle 6 pre-significant coefficients.
            while (pre.Count < 6) pre.Add((rng.Next(0, 8), rng.Next(0, 8)));
            var preSignSet = new HashSet<(int, int)>();
            foreach ((int x, int y) p in pre) if (rng.Next(0, 2) == 1) preSignSet.Add(p);

            var sigChoices = new Dictionary<(int, int), int>();
            var signChoices = new Dictionary<(int, int), int>();
            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                sigChoices[(x, y)] = rng.Next(0, 2);
                signChoices[(x, y)] = rng.Next(0, 2);
            }

            RunRoundTrip(
                width: 8, height: 8,
                preSignificant: pre,
                preSignSet: preSignSet,
                orientation: SubbandOrientation.LH,
                bitPlane: 4,
                pickSig: (x, y) => sigChoices[(x, y)],
                pickSign: (x, y) => signChoices[(x, y)]);
        }

        [Fact]
        public void Spp_RoundTrip_RandomDecisions_HL_NonStripeAlignedHeight()
        {
            // Height 5 → padded to 8 → last stripe has only 1 valid row.
            var rng = new Random(7);
            RunRoundTrip(
                width: 6, height: 5,
                preSignificant: new[] { (2, 2), (5, 0) },
                preSignSet: new HashSet<(int, int)> { (5, 0) },
                orientation: SubbandOrientation.HL,
                bitPlane: 6,
                pickSig: (_, _) => rng.Next(0, 2),
                pickSign: (_, _) => rng.Next(0, 2));
        }

        // ---- Validation helpers -----------------------------------------

        /// <summary>
        /// Runs SPP twice — once "encoding" (a reference walker that mirrors
        /// the production scan order, picks a decision per candidate, and
        /// feeds it through Jp2MqEncoder) and once "decoding" (the actual
        /// production code being tested). After both runs the two state
        /// grids must be identical.
        /// </summary>
        private static void RunRoundTrip(
            int width, int height,
            IEnumerable<(int x, int y)> preSignificant,
            HashSet<(int, int)> preSignSet,
            SubbandOrientation orientation,
            int bitPlane,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign)
        {
            var planState = new Tier1State(width, height);
            foreach ((int x, int y) in preSignificant)
            {
                planState.SetFlag(x, y, Tier1State.SignificanceFlag);
                if (preSignSet.Contains((x, y))) planState.SetFlag(x, y, Tier1State.SignFlag);
            }

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceSpp(planState, enc, encContexts, orientation, bitPlane, pickSig, pickSign);
            enc.Flush();

            // Production decoder run starts from the SAME pre-state.
            var decodeState = new Tier1State(width, height);
            foreach ((int x, int y) in preSignificant)
            {
                decodeState.SetFlag(x, y, Tier1State.SignificanceFlag);
                if (preSignSet.Contains((x, y))) decodeState.SetFlag(x, y, Tier1State.SignFlag);
            }

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            SignificancePropagationPass.Run(decodeState, mq, decContexts, orientation, bitPlane);

            // Compare state grids and contexts.
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                Assert.True(
                    planState.GetFlags(x, y) == decodeState.GetFlags(x, y),
                    $"flags mismatch at ({x},{y}): plan=0x{planState.GetFlags(x, y):X2}, " +
                    $"decoded=0x{decodeState.GetFlags(x, y):X2}");
                Assert.Equal(planState.GetMagnitude(x, y), decodeState.GetMagnitude(x, y));
            }
            Assert.Equal(encContexts, decContexts);
        }

        /// <summary>
        /// Reference walker that mirrors the production SPP scan but ENCODES
        /// per-candidate decisions instead of decoding them. Returns through
        /// the supplied state — every candidate it processes also updates the
        /// in-memory plan state so subsequent neighbour lookups see the same
        /// significance pattern the decoder will see.
        /// </summary>
        private static void EncodeReferenceSpp(
            Tier1State state,
            Jp2MqEncoder enc,
            byte[] contexts,
            SubbandOrientation orientation,
            int bitPlane,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign)
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
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;

                        byte neigh = state.GetSignificanceNeighbourhood(x, y);
                        if (neigh == 0) continue;

                        int zcContext = Tier1Contexts.ZeroCoding(orientation, neigh);
                        int sigBit = pickSig(x, y);
                        enc.Encode(sigBit, ref contexts[zcContext]);

                        if (sigBit == 1)
                        {
                            int hC = Math.Sign(
                                state.GetSignContribution(x, y, NeighbourDirection.West) +
                                state.GetSignContribution(x, y, NeighbourDirection.East));
                            int vC = Math.Sign(
                                state.GetSignContribution(x, y, NeighbourDirection.North) +
                                state.GetSignContribution(x, y, NeighbourDirection.South));

                            (int scContext, int xorBit) =
                                Tier1Contexts.SignCoding(hC, vC);
                            int rawSign = pickSign(x, y);
                            int encodedBit = rawSign ^ xorBit;
                            enc.Encode(encodedBit, ref contexts[scContext]);

                            state.SetFlag(x, y, Tier1State.SignificanceFlag);
                            if (rawSign == 1) state.SetFlag(x, y, Tier1State.SignFlag);
                            state.SetMagnitude(x, y, 1 << bitPlane);
                        }

                        state.SetFlag(x, y, Tier1State.VisitedFlag);
                    }
                }
            }
        }
    }
}
