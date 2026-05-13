using System;
using System.Collections.Generic;
using System.IO;
using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Driver-level coverage of the RESTART (reset context probabilities on
    /// coding pass boundaries) code-block style flag, ISO/IEC 15444-1 D.5.5
    /// / Table A-19 bit 1. With RESTART set, both encoder and decoder must
    /// reinitialise the 19-entry MQ context array at the start of every pass.
    /// The MQ stream itself is continuous — RESTART is orthogonal to TERMALL.
    /// </summary>
    public sealed class Tier1RestartTests
    {
        private const int W = 4, H = 4;

        // ---- Positive round-trips ----------------------------------------

        [Fact]
        public void Restart_EmptyMultiBitPlaneRoundTrip_StateMatches()
        {
            // Four passes — CUP@5, SPP@4, MRP@4, CUP@4 — all empty payloads.
            // Encoder resets contexts at every pass entry to mirror the
            // decoder. With restart=true on both sides, the byte stream is
            // well-formed and the final state is empty on both.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();

            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                FirstBp, Passes,
                resetEachPass: true,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, restart: true);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);

            Assert.Equal(Passes, driver.PassesCompleted);
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(planState.GetFlags(x, y), driver.State.GetFlags(x, y));
                Assert.Equal(planState.GetMagnitude(x, y), driver.State.GetMagnitude(x, y));
            }
        }

        [Fact]
        public void Restart_SinglePass_NoReset_SameStreamAsDefault()
        {
            // RESTART is a no-op for the very first pass (contexts are
            // already at the initial state). Encode one CUP with reset=true,
            // decode with restart=false, then again with restart=true —
            // both must succeed and produce identical state.
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            var d1 = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, restart: false);
            d1.RunPasses(new Jp2MqDecoder(data, 0, data.Length), 1);

            var d2 = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, restart: true);
            d2.RunPasses(new Jp2MqDecoder(data, 0, data.Length), 1);

            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(d1.State.GetFlags(x, y), d2.State.GetFlags(x, y));
                Assert.Equal(d1.State.GetMagnitude(x, y), d2.State.GetMagnitude(x, y));
            }
        }

        [Fact]
        public void Restart_NonTrivialCup_RoundTrips()
        {
            // Drive the encoder with a non-empty cleanup pass: column 0
            // becomes significant at row 2 via the RL aggregation path
            // (sign=0). Pass 0 thus encodes: 1 RL bit (=1) + 2 uniform bits
            // (k=2 → "10" MSB-first) + 1 sign bit, then columns 1..3 emit
            // RL=0. Pass 1 (SPP) and Pass 2 (MRP) at bp4 then have real
            // candidates around the newly-significant sample. Pass 3 (CUP
            // at bp4) operates on a state where one column is already
            // partially-significant and the others remain RL-eligible.
            //
            // This exercises every context category — ZC, SC, MR, RL,
            // Uniform — across enough passes that RESTART's context reset
            // makes a tangible difference to the encoder state machine.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                FirstBp, Passes,
                resetEachPass: true,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 1);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, restart: true);
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

        [Fact]
        public void Restart_NonTrivialCup_DivergesFromDefault()
        {
            // Same plan as Restart_NonTrivialCup_RoundTrips. The reset-mode
            // stream must NOT round-trip when fed to a default-style
            // decoder — context state has been reset at pass boundaries on
            // the encoder side, so the decoder must do the same to keep MQ
            // probabilities in sync. Decoder either decodes a different
            // state or runs the MQ buffer dry.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                FirstBp, Passes,
                resetEachPass: true,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 1);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, restart: false);

            bool matchedPlanned = true;
            try
            {
                driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);
                for (var y = 0; y < H && matchedPlanned; y++)
                for (var x = 0; x < W && matchedPlanned; x++)
                {
                    if (planState.GetFlags(x, y) != driver.State.GetFlags(x, y))
                        matchedPlanned = false;
                    else if (planState.GetMagnitude(x, y) != driver.State.GetMagnitude(x, y))
                        matchedPlanned = false;
                }
            }
            catch (Exception)
            {
                // Throwing during decode is also a valid signal of mode
                // mismatch — the MQ ran off the end of the stream or
                // arrived at an inconsistent state.
                matchedPlanned = false;
            }

            Assert.False(matchedPlanned,
                "Default-style decoder must NOT reproduce the planned state from a RESTART-encoded stream.");
        }

        [Fact]
        public void Restart_AcrossTwoRunPassesCalls_StillResets()
        {
            // Driver state must carry restart semantics across multiple
            // RunPasses invocations on the same MQ instance.
            const int FirstBp = 5;
            const int Passes = 4;

            byte[] data = EncodePlannedStream(FirstBp, Passes, resetEachPass: true);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, restart: true);
            var mq = new Jp2MqDecoder(data, 0, data.Length);
            driver.RunPasses(mq, 1);
            driver.RunPasses(mq, 3);

            Assert.Equal(Passes, driver.PassesCompleted);
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(0, driver.State.GetFlags(x, y));
        }

        [Fact]
        public void Restart_WithSegSym_BothFlagsHonoured()
        {
            // RESTART and SEGSYM are independent — verify a stream that
            // exercises both round-trips. CUP passes get a {1,0,1,0} tail
            // *and* the contexts reset at each pass boundary.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                FirstBp, Passes,
                resetEachPass: true,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0,
                emitSegSymOnCup: true);
            enc.Flush();
            byte[] data = enc.ToArray();

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, segSym: true, restart: true);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);

            Assert.Equal(Passes, driver.PassesCompleted);
        }

        // ---- Helpers ------------------------------------------------------

        private static byte[] EncodePlannedStream(int firstBitPlane, int passes, bool resetEachPass)
        {
            var planState = new Tier1State(W, H);
            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            EncodeReferenceDriver(planState, enc, encContexts, SubbandOrientation.LL,
                firstBitPlane, passes,
                resetEachPass,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);
            enc.Flush();
            return enc.ToArray();
        }

        // Reference encoder copy with the RESTART option and an optional
        // segsym tail flag. Mirror of the per-pass encoder used by
        // Tier1CodeBlockDecoderTests, factored so the RESTART tests don't
        // depend on the sibling test class.
        private static void EncodeReferenceDriver(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            SubbandOrientation orientation,
            int firstBitPlane, int passes,
            bool resetEachPass,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool emitSegSymOnCup = false)
        {
            for (var p = 0; p < passes; p++)
            {
                int planeOffset = (p + 2) / 3;
                int bitPlane = firstBitPlane - planeOffset;
                int kind = (p + 2) % 3;
                if (bitPlane < 0)
                    throw new InvalidOperationException("test asks for more passes than available");

                if (resetEachPass)
                    Jp2MqContextSet.ResetInPlace(contexts);

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
                        if (emitSegSymOnCup)
                            WriteSegSym(enc, contexts);
                        break;
                }
            }
        }

        private static void WriteSegSym(Jp2MqEncoder enc, byte[] contexts)
        {
            enc.Encode(1, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(0, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(1, ref contexts[Jp2MqContextSet.Uniform]);
            enc.Encode(0, ref contexts[Jp2MqContextSet.Uniform]);
        }

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
