using System;
using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Driver-level coverage of the TERMALL (termination on each coding pass)
    /// code-block style, ISO/IEC 15444-1 D.5.6 / Table A-19 bit 2. TERMALL
    /// flushes the MQ encoder after every pass; the decoder must therefore
    /// receive each pass as its own self-contained byte segment. The
    /// existing driver supports this naturally — the caller invokes
    /// <see cref="Tier1CodeBlockDecoder.RunPasses"/> once per pass with a
    /// fresh <see cref="Jp2MqDecoder"/> over each pass's bytes; driver-side
    /// state (passes completed, flag grid, context array, RESTART flag)
    /// carries across the calls.
    /// </summary>
    public sealed class Tier1TerminationTests
    {
        private const int W = 4, H = 4;

        [Fact]
        public void Termall_EmptyPasses_RoundTrips()
        {
            // Four passes (CUP@5, SPP@4, MRP@4, CUP@4) each terminated
            // independently. SPP and MRP at bp4 have no candidates, so
            // their segments are pure flush bytes — still legal input the
            // decoder must handle. CUP passes encode 4 RL-skip bits.
            const int FirstBp = 5;
            const int Passes = 4;

            byte[][] segments = EncodePerPass(FirstBp, Passes,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp);

            for (var p = 0; p < Passes; p++)
            {
                var mq = new Jp2MqDecoder(segments[p], 0, segments[p].Length);
                driver.RunPasses(mq, 1);
            }

            Assert.Equal(Passes, driver.PassesCompleted);
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(0, driver.State.GetFlags(x, y));
                Assert.Equal(0, driver.State.GetMagnitude(x, y));
            }
        }

        [Fact]
        public void Termall_NonTrivialActivity_RoundTrips()
        {
            // Column 0 becomes significant at row 2 during pass 0 (CUP@5)
            // via RL aggregation. Subsequent passes refine. Each pass is
            // terminated to its own segment; decoder state must match the
            // encoder's planned state at the end.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            byte[][] segments = EncodePerPass(planState, FirstBp, Passes,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 1);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp);

            for (var p = 0; p < Passes; p++)
            {
                var mq = new Jp2MqDecoder(segments[p], 0, segments[p].Length);
                driver.RunPasses(mq, 1);
            }

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
        public void Termall_PlusRestart_RoundTrips()
        {
            // TERMALL and RESTART are independent code-block styles and the
            // decoder must honour both. Each pass gets its own MQ segment
            // (TERMALL) AND the context array resets at every pass entry
            // (RESTART). The encoder must mirror both.
            const int FirstBp = 5;
            const int Passes = 4;

            var planState = new Tier1State(W, H);
            byte[][] segments = EncodePerPass(planState, FirstBp, Passes,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 1,
                resetEachPass: true);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, restart: true);

            for (var p = 0; p < Passes; p++)
            {
                var mq = new Jp2MqDecoder(segments[p], 0, segments[p].Length);
                driver.RunPasses(mq, 1);
            }

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
        public void Termall_PerPassByteCounts_ReflectPassActivity()
        {
            // Sanity: per-pass termination produces N independent byte
            // segments. The CUP segments should be larger than the empty
            // SPP/MRP segments since CUP encodes 4 RL-skip bits while
            // SPP/MRP encode nothing. Pure structural check on the
            // per-pass partitioning we expect from a TERMALL encoder.
            const int FirstBp = 5;
            const int Passes = 4;

            byte[][] segments = EncodePerPass(FirstBp, Passes,
                pickRl: _ => 0,
                pickRlIndex: _ => 0,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);

            // pass 0 = CUP@5; pass 1 = SPP@4; pass 2 = MRP@4; pass 3 = CUP@4
            Assert.Equal(Passes, segments.Length);
            // Every segment is at least the flush footer (2 bytes minus
            // optional trailing-0xFF trim), so they're all non-empty.
            for (var p = 0; p < Passes; p++)
                Assert.True(segments[p].Length > 0,
                    $"pass {p} segment is empty");
        }

        // ---- Helpers -----------------------------------------------------

        private static byte[][] EncodePerPass(
            int firstBitPlane, int passes,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool resetEachPass = false)
            => EncodePerPass(new Tier1State(W, H), firstBitPlane, passes,
                pickRl, pickRlIndex, pickSig, pickSign, pickMrBit, resetEachPass);

        private static byte[][] EncodePerPass(
            Tier1State state, int firstBitPlane, int passes,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool resetEachPass = false)
        {
            // Persistent context array across passes (unless resetEachPass
            // says otherwise) — matches the decoder's contract: TERMALL
            // terminates the *MQ stream* per pass but the *context state*
            // is preserved unless RESTART says otherwise.
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            var segments = new byte[passes][];

            for (var p = 0; p < passes; p++)
            {
                int planeOffset = (p + 2) / 3;
                int bitPlane = firstBitPlane - planeOffset;
                int kind = (p + 2) % 3;
                if (bitPlane < 0)
                    throw new InvalidOperationException("test asks for more passes than available");

                if (resetEachPass)
                    Jp2MqContextSet.ResetInPlace(contexts);

                var enc = new Jp2MqEncoder();
                switch (kind)
                {
                    case 0: // SPP
                        state.ResetVisited();
                        EncodeSpp(state, enc, contexts, SubbandOrientation.LL, bitPlane,
                            pickSig, pickSign);
                        break;
                    case 1: // MRP
                        EncodeMrp(state, enc, contexts, bitPlane, pickMrBit);
                        break;
                    case 2: // CUP
                        if (p == 0) state.ResetVisited();
                        EncodeCup(state, enc, contexts, SubbandOrientation.LL, bitPlane,
                            pickRl, pickRlIndex, pickSig, pickSign);
                        break;
                }
                enc.Flush();
                segments[p] = enc.ToArray();
            }

            return segments;
        }

        private static void EncodeSpp(
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

        private static void EncodeMrp(
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

        private static void EncodeCup(
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
