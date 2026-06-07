using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class CleanupPassTests
    {
        // ---- Trivial cases ------------------------------------------------

        [Fact]
        public void Cup_EmptyState_AllZerosRunLengthSkipsEverything()
        {
            // Empty state → every column qualifies for RL. We need to
            // encode a "0" run-length bit per column to say "all four stay
            // insignificant".
            var state = new Tier1State(8, 8);
            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            int columns = state.Width * state.StripeCount;
            for (var i = 0; i < columns; i++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);

            CleanupPass.Run(state, mq, decContexts, SubbandOrientation.LL, bitPlane: 0);

            for (var y = 0; y < 8; y++)
            for (var x = 0; x < 8; x++)
            {
                Assert.Equal(0, state.GetFlags(x, y));
                Assert.Equal(0, state.GetMagnitude(x, y));
            }
            Assert.Equal(encContexts, decContexts);
        }

        // ---- Round-trip cases -------------------------------------------

        [Fact]
        public void Cup_RoundTrip_RlOnly_AllZeros()
        {
            // Reference walker decides every RL column gets a 0 → no decode beyond RL.
            RunRoundTrip(
                width: 4, height: 4,
                preState: _ => { },
                orientation: SubbandOrientation.LL,
                bitPlane: 5,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0);
        }

        [Fact]
        public void Cup_RoundTrip_RlOnly_AlternatingFirstSigPositions()
        {
            int seed = 0;
            int RlIdx(int x) => x & 3;
            RunRoundTrip(
                width: 8, height: 4,
                preState: _ => { },
                orientation: SubbandOrientation.LL,
                bitPlane: 7,
                pickRl: x => 1,         // every column gets a sig sample
                pickRlIndex: RlIdx,     // first-sig row varies by column
                pickSig: (_, _) => seed++ & 1,
                pickSign: (_, _) => 0);
        }

        [Fact]
        public void Cup_RoundTrip_MixOfRlAndPerSample_HH()
        {
            // Pre-significance breaks RL eligibility on some columns.
            var rng = new Random(42);
            RunRoundTrip(
                width: 8, height: 8,
                preState: s =>
                {
                    s.SetFlag(2, 1, Tier1State.SignificanceFlag);
                    s.SetFlag(5, 5, Tier1State.SignificanceFlag);
                    s.SetFlag(0, 6, Tier1State.SignificanceFlag);
                },
                orientation: SubbandOrientation.HH,
                bitPlane: 4,
                pickRl: _ => rng.Next(0, 2),
                pickRlIndex: _ => rng.Next(0, 4),
                pickSig: (_, _) => rng.Next(0, 2),
                pickSign: (_, _) => rng.Next(0, 2));
        }

        [Fact]
        public void Cup_RoundTrip_AfterSppLeavesVisitedFlags_HL()
        {
            // Some samples are visited (SPP touched them this bit-plane) —
            // CUP must skip them entirely and not re-decode their bits.
            var rng = new Random(20260512);
            RunRoundTrip(
                width: 6, height: 8,
                preState: s =>
                {
                    s.SetFlag(0, 0, Tier1State.SignificanceFlag);
                    s.SetFlag(3, 3, Tier1State.SignificanceFlag);
                    // Mark some insignificant samples as visited (as if SPP processed them).
                    s.SetFlag(1, 0, Tier1State.VisitedFlag);
                    s.SetFlag(0, 1, Tier1State.VisitedFlag);
                    s.SetFlag(4, 3, Tier1State.VisitedFlag);
                    s.SetFlag(2, 4, Tier1State.VisitedFlag);
                },
                orientation: SubbandOrientation.HL,
                bitPlane: 3,
                pickRl: _ => rng.Next(0, 2),
                pickRlIndex: _ => rng.Next(0, 4),
                pickSig: (_, _) => rng.Next(0, 2),
                pickSign: (_, _) => rng.Next(0, 2));
        }

        [Fact]
        public void Cup_RoundTrip_NonStripeAlignedHeight_RlNotUsedInPartialStripe()
        {
            // Height 5 → padded to 8 → last stripe is partial (1 valid row),
            // RL aggregation cannot apply there. Verify that path.
            var rng = new Random(7);
            RunRoundTrip(
                width: 5, height: 5,
                preState: _ => { },
                orientation: SubbandOrientation.LH,
                bitPlane: 6,
                pickRl: _ => rng.Next(0, 2),
                pickRlIndex: _ => rng.Next(0, 4),
                pickSig: (_, _) => rng.Next(0, 2),
                pickSign: (_, _) => rng.Next(0, 2));
        }

        // ---- Helper -----------------------------------------------------

        /// <summary>
        /// Reference walker mirrors production CUP scan order, encoding
        /// one decision per RL/per-sample step instead of decoding. The
        /// resulting MQ stream then has to round-trip through production
        /// CUP into the same final state.
        /// </summary>
        private static void RunRoundTrip(
            int width, int height,
            Action<Tier1State> preState,
            SubbandOrientation orientation,
            int bitPlane,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign)
        {
            var planState = new Tier1State(width, height);
            preState(planState);

            byte[] encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceCup(planState, enc, encContexts, orientation, bitPlane,
                pickRl, pickRlIndex, pickSig, pickSign);
            enc.Flush();

            var decodeState = new Tier1State(width, height);
            preState(decodeState);

            byte[] decContexts = Jp2MqContextSet.CreateInitialised();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            CleanupPass.Run(decodeState, mq, decContexts, orientation, bitPlane);

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

        private static void EncodeReferenceCup(
            Tier1State state,
            Jp2MqEncoder enc,
            byte[] contexts,
            SubbandOrientation orientation,
            int bitPlane,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign)
        {
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

                    if (stripeHeight == 4 && IsRunLengthEligible(state, x, stripeTop))
                    {
                        int rlBit = pickRl(x);
                        enc.Encode(rlBit, ref contexts[Jp2MqContextSet.RunLength]);
                        if (rlBit == 0) continue;

                        int k = pickRlIndex(x) & 3;
                        enc.Encode((k >> 1) & 1, ref contexts[Jp2MqContextSet.Uniform]);
                        enc.Encode(k & 1,        ref contexts[Jp2MqContextSet.Uniform]);

                        int firstSigY = stripeTop + k;
                        EncodeNewSignificance(state, enc, contexts, x, firstSigY, bitPlane,
                            pickSign);
                        processStartY = firstSigY + 1;
                    }

                    for (int y = processStartY; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;

                        byte neigh = state.GetSignificanceNeighbourhood(x, y);
                        int zcCtx = Tier1Contexts.ZeroCoding(orientation, neigh);
                        int sigBit = pickSig(x, y);
                        enc.Encode(sigBit, ref contexts[zcCtx]);
                        if (sigBit == 1)
                            EncodeNewSignificance(state, enc, contexts, x, y, bitPlane, pickSign);
                    }
                }
            }
        }

        private static bool IsRunLengthEligible(Tier1State state, int x, int stripeTop)
        {
            for (int y = stripeTop; y < stripeTop + 4; y++)
            {
                if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) return false;
                if (state.HasFlag(x, y, Tier1State.VisitedFlag)) return false;
                if (state.GetSignificanceNeighbourhood(x, y) != 0) return false;
            }
            return true;
        }

        private static void EncodeNewSignificance(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            int x, int y, int bitPlane, Func<int, int, int> pickSign)
        {
            int hC = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.West) +
                state.GetSignContribution(x, y, NeighbourDirection.East));
            int vC = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.North) +
                state.GetSignContribution(x, y, NeighbourDirection.South));
            (int scContext, int xorBit) = Tier1Contexts.SignCoding(hC, vC);
            int rawSign = pickSign(x, y);
            int encodedBit = rawSign ^ xorBit;
            enc.Encode(encodedBit, ref contexts[scContext]);

            state.SetFlag(x, y, Tier1State.SignificanceFlag);
            if (rawSign == 1) state.SetFlag(x, y, Tier1State.SignFlag);
            state.SetMagnitude(x, y, 1 << bitPlane);
        }
    }
}
