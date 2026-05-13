using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class Tier1CodeBlockDecoderTests
    {
        // ---- Pass-to-bit-plane mapping ------------------------------------

        [Theory]
        [InlineData(7, 1)]
        [InlineData(7, 4)]
        [InlineData(7, 8)]
        public void PassesCompleted_AdvancesByPassCount(int firstBp, int passCount)
        {
            var dec = new Tier1CodeBlockDecoder(4, 4, SubbandOrientation.LL, firstBp);
            var enc = new Jp2MqEncoder();
            byte[] ctx = Jp2MqContextSet.CreateInitialised();
            // Pad with enough RL-skip 0-bits to cover the passes we'll run —
            // CUP-only passes need 4 RL-skip bits each; SPP/MRP need none.
            for (var c = 0; c < 4 * passCount; c++)
                enc.Encode(0, ref ctx[Jp2MqContextSet.RunLength]);
            enc.Flush();

            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            dec.RunPasses(mq, passCount);
            Assert.Equal(passCount, dec.PassesCompleted);
        }

        // ---- Round-trip tests --------------------------------------------

        [Fact]
        public void RunOnePass_SameAsCallingCleanupDirectly()
        {
            const int W = 4, H = 4;
            const int BitPlane = 5;

            // Reference state: run CleanupPass directly with a hand-built MQ
            // stream that says "every column RL-skip = 0-bit at RL ctx".
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            // Path A: pass driver
            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, BitPlane);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), passCount: 1);

            // Path B: direct CUP call on a fresh state
            var directState = new Tier1State(W, H);
            var directContexts = Jp2MqContextSet.CreateInitialised();
            CleanupPass.Run(directState, new Jp2MqDecoder(data, 0, data.Length),
                directContexts, SubbandOrientation.LL, BitPlane);

            // Compare flags and magnitudes; both should be all-zero in this
            // empty round-trip.
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(directState.GetFlags(x, y), driver.State.GetFlags(x, y));
                Assert.Equal(directState.GetMagnitude(x, y), driver.State.GetMagnitude(x, y));
            }
        }

        [Fact]
        public void RunFourPasses_FollowsCupSppMrpCupSequence()
        {
            // Verify pass driver runs CUP, SPP, MRP, CUP in order.
            // We construct an MQ stream that:
            //   Pass 0 (CUP, bit-plane 5):   every column RL-skip → no decisions of substance.
            //   Pass 1 (SPP, bit-plane 4):   no candidates (state empty) → nothing decoded.
            //   Pass 2 (MRP, bit-plane 4):   no significant coefficients yet → nothing decoded.
            //   Pass 3 (CUP, bit-plane 4):   every column RL-skip again.
            // Total decoded bits: 4 RL bits (pass 0) + 4 RL bits (pass 3) = 8 zero bits.

            const int W = 4, H = 4;
            const int FirstBp = 5;

            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), passCount: 4);

            // After 4 passes nothing became significant.
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(0, driver.State.GetFlags(x, y));
                Assert.Equal(0, driver.State.GetMagnitude(x, y));
            }
            Assert.Equal(4, driver.PassesCompleted);
        }

        [Fact]
        public void RunPasses_AcrossTwoCalls_ContinuesFromPriorPassIndex()
        {
            // Pass driver is called twice — once with 1 pass, then with 3
            // more — should produce same state as one 4-pass call.
            const int W = 4, H = 4;
            const int FirstBp = 5;

            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp);
            // Same byte stream, two calls, but same MQ instance — that's the
            // default-style invariant: one MQ across the whole code block.
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            driver.RunPasses(mq, 1);
            driver.RunPasses(mq, 3);

            Assert.Equal(4, driver.PassesCompleted);
            // No coefficients became significant.
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(0, driver.State.GetFlags(x, y));
        }

        [Fact]
        public void RunPasses_RejectsTooManyPassesForBitPlanes()
        {
            // firstBitPlane = 0 → only one pass possible (CUP at bit-plane 0).
            // Asking for 2 passes should throw because pass 1 would target
            // bit-plane -1.
            var driver = new Tier1CodeBlockDecoder(4, 4, SubbandOrientation.LL, firstBitPlane: 0);
            var enc = new Jp2MqEncoder();
            var ctx = Jp2MqContextSet.CreateInitialised();
            for (var c = 0; c < 4; c++) enc.Encode(0, ref ctx[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            Assert.Throws<InvalidDataException>(() => driver.RunPasses(mq, 2));
        }

        [Fact]
        public void RunPasses_RejectsNegativePassCount()
        {
            var driver = new Tier1CodeBlockDecoder(4, 4, SubbandOrientation.LL, 5);
            var enc = new Jp2MqEncoder();
            enc.Flush();
            var mq = new Jp2MqDecoder(enc.ToArray(), 0, enc.ToArray().Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => driver.RunPasses(mq, -1));
        }

        [Fact]
        public void RunPasses_FullRoundTrip_MultipleBitPlanes_LL()
        {
            // Build the same round-trip pattern the per-pass tests use, but
            // through the driver: encoder runs the same SPP/MRP/CUP logic
            // and the driver decodes back into a parallel state.
            const int W = 4, H = 4;
            const int FirstBp = 4;
            const int Passes = 7; // CUP at 4, then SPP/MRP/CUP at 3, then SPP/MRP/CUP at 2

            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();

            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                FirstBp, Passes,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);

            Assert.Equal(Passes, driver.PassesCompleted);
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.True(planState.GetFlags(x, y) == driver.State.GetFlags(x, y),
                    $"flags ({x},{y}): plan=0x{planState.GetFlags(x, y):X2}, drv=0x{driver.State.GetFlags(x, y):X2}");
                Assert.Equal(planState.GetMagnitude(x, y), driver.State.GetMagnitude(x, y));
            }
        }

        // ---- Reference encoder mirroring the driver -----------------------

        private static void EncodeReferenceDriver(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            SubbandOrientation orientation,
            int firstBitPlane, int passes,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit)
        {
            for (var p = 0; p < passes; p++)
            {
                int planeOffset = (p + 2) / 3;
                int bitPlane = firstBitPlane - planeOffset;
                int kind = (p + 2) % 3;
                if (bitPlane < 0)
                    throw new InvalidOperationException("test asks for more passes than available");

                switch (kind)
                {
                    case 0: // SPP
                        state.ResetVisited();
                        EncodeReferenceSpp(state, enc, contexts, orientation, bitPlane,
                            pickSig, pickSign);
                        break;
                    case 1: // MRP
                        EncodeReferenceMrp(state, enc, contexts, bitPlane, pickMrBit);
                        break;
                    case 2: // CUP
                        if (p == 0) state.ResetVisited();
                        EncodeReferenceCup(state, enc, contexts, orientation, bitPlane,
                            pickRl, pickRlIndex, pickSig, pickSign);
                        break;
                }
            }
        }

        // The next three helpers are private duplicates of the per-pass-test
        // reference walkers. Keeping them local to this file rather than
        // sharing keeps the dependency direction sane: T7 tests only reach
        // back to T3 (Jp2MqEncoder), not to other test classes.

        private static void EncodeReferenceSpp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            SubbandOrientation orientation, int bitPlane,
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
                        int zcCtx = Tier1Contexts.ZeroCoding(orientation, neigh);
                        int sig = pickSig(x, y);
                        enc.Encode(sig, ref contexts[zcCtx]);
                        if (sig == 1)
                            EncodeNewSig(state, enc, contexts, x, y, bitPlane, pickSign);
                        state.SetFlag(x, y, Tier1State.VisitedFlag);
                    }
                }
            }
        }

        private static void EncodeReferenceMrp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            int bitPlane, Func<int, int, int> pickBit)
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
                        bool refined = state.HasFlag(x, y, Tier1State.RefinedFlag);
                        int neighCount = state.CountSignificantNeighbours(x, y);
                        int mrCtx = Tier1Contexts.MagnitudeRefinement(refined, neighCount);
                        int bit = pickBit(x, y);
                        enc.Encode(bit, ref contexts[mrCtx]);
                        int mag = state.GetMagnitude(x, y);
                        mag |= bit << bitPlane;
                        state.SetMagnitude(x, y, mag);
                        state.SetFlag(x, y, Tier1State.RefinedFlag);
                    }
                }
            }
        }

        private static void EncodeReferenceCup(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            SubbandOrientation orientation, int bitPlane,
            Func<int, int> pickRl, Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign)
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
                        int rl = pickRl(x);
                        enc.Encode(rl, ref contexts[Jp2MqContextSet.RunLength]);
                        if (rl == 0) continue;
                        int k = pickRlIndex(x) & 3;
                        enc.Encode((k >> 1) & 1, ref contexts[Jp2MqContextSet.Uniform]);
                        enc.Encode(k & 1, ref contexts[Jp2MqContextSet.Uniform]);
                        EncodeNewSig(state, enc, contexts, x, stripeTop + k, bitPlane, pickSign);
                        processStartY = stripeTop + k + 1;
                    }
                    for (var y = processStartY; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;
                        byte neigh = state.GetSignificanceNeighbourhood(x, y);
                        int zcCtx = Tier1Contexts.ZeroCoding(orientation, neigh);
                        int sig = pickSig(x, y);
                        enc.Encode(sig, ref contexts[zcCtx]);
                        if (sig == 1)
                            EncodeNewSig(state, enc, contexts, x, y, bitPlane, pickSign);
                    }
                }
            }
        }

        private static bool IsRunLengthEligible(Tier1State state, int x, int stripeTop)
        {
            for (var y = stripeTop; y < stripeTop + 4; y++)
            {
                if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) return false;
                if (state.HasFlag(x, y, Tier1State.VisitedFlag)) return false;
                if (state.GetSignificanceNeighbourhood(x, y) != 0) return false;
            }
            return true;
        }

        private static void EncodeNewSig(
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
