using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Coverage for the vertically-causal context formation style (VSC) —
    /// ISO/IEC 15444-1 D.7. With VSC enabled, every coefficient at the last
    /// row of a 4-row stripe (<c>y % 4 == 3</c>) treats its three southern
    /// 8-neighbours (SW, S, SE) as insignificant when forming contexts and
    /// when selecting candidates. This lets a line-based decoder process
    /// stripes independently without buffering the next code-block scan.
    /// </summary>
    public sealed class Tier1VscTests
    {
        // ---- State helpers -----------------------------------------------

        [Fact]
        public void GetNeighbourhood_MasksSouthRowBits()
        {
            // Place sig at every 8-neighbour of (1,1) in a 3×3 block.
            // Without mask: 0xFF. With mask: 0x1F (low 5 bits — NW, N, NE,
            // W, E retained; SW, S, SE cleared).
            var s = new Tier1State(3, 3);
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                if (!(x == 1 && y == 1))
                    s.SetFlag(x, y, Tier1State.SignificanceFlag);

            Assert.Equal(0xFF, s.GetSignificanceNeighbourhood(1, 1));
            Assert.Equal(0x1F, s.GetSignificanceNeighbourhood(1, 1, maskSouthRow: true));
        }

        [Fact]
        public void CountSignificantNeighbours_SkipsSouthRow()
        {
            var s = new Tier1State(3, 3);
            for (var y = 0; y < 3; y++)
            for (var x = 0; x < 3; x++)
                if (!(x == 1 && y == 1))
                    s.SetFlag(x, y, Tier1State.SignificanceFlag);

            Assert.Equal(8, s.CountSignificantNeighbours(1, 1));
            Assert.Equal(5, s.CountSignificantNeighbours(1, 1, maskSouthRow: true));
        }

        // ---- SPP candidate selection -------------------------------------

        [Fact]
        public void Vsc_HidesSouthStripeNeighbourFromSppCandidate()
        {
            // 4-wide, 8-tall block. Seed a single significant coefficient
            // at (0, 4) — that's the very first row of stripe 1, directly
            // south of (0, 3) which is the last row of stripe 0.
            //
            // Without VSC the SPP on stripe 0 must visit (0, 3) (its south
            // neighbour is significant) and (1, 3) (its south-west
            // neighbour is significant). Even if their decoded sig bits are
            // both zero, the pass consumes two MQ bits.
            //
            // With VSC those candidates collapse — the south-row mask
            // hides (0, 4) entirely, so neither (0, 3) nor (1, 3) is a
            // candidate and the pass consumes zero bits.
            const int W = 4, H = 8;
            const int Bp = 2;

            // Build an MQ stream that says "two zero significance bits".
            // Under VSC SPP reads nothing — but the stream having more bits
            // available is fine, the decoder just doesn't consume them.
            byte[] data = EncodeTwoZeroSigBits();

            // Without-VSC decoder visits (0,3) and (1,3) and marks them
            // visited. Sig flags stay zero.
            Tier1State stateNoVsc = SeedAtRow4(W, H);
            SignificancePropagationPass.Run(stateNoVsc, new Jp2MqDecoder(data, 0, data.Length),
                Jp2MqContextSet.CreateInitialised(),
                SubbandOrientation.LL, Bp, vsc: false);

            Assert.True(stateNoVsc.HasFlag(0, 3, Tier1State.VisitedFlag));
            Assert.True(stateNoVsc.HasFlag(1, 3, Tier1State.VisitedFlag));

            // With-VSC decoder skips the same cells.
            Tier1State stateVsc = SeedAtRow4(W, H);
            SignificancePropagationPass.Run(stateVsc, new Jp2MqDecoder(data, 0, data.Length),
                Jp2MqContextSet.CreateInitialised(),
                SubbandOrientation.LL, Bp, vsc: true);

            Assert.False(stateVsc.HasFlag(0, 3, Tier1State.VisitedFlag));
            Assert.False(stateVsc.HasFlag(1, 3, Tier1State.VisitedFlag));
        }

        // ---- CUP run-length eligibility ----------------------------------

        [Fact]
        public void Vsc_AllowsRunLengthEligibilityOnSouthBoundedColumns()
        {
            // Same shape — sig at (0, 4). Without VSC, column 0's last-row
            // candidate (0, 3) has the south neighbour set, so the column
            // is NOT run-length eligible. With VSC, the south neighbour is
            // masked away and the column qualifies.
            //
            // Drive the cleanup pass directly. Without VSC the pass will
            // walk per-sample (no RL bit); with VSC it consumes a single
            // RL=0 bit.
            const int W = 4, H = 8;
            const int FirstBp = 5;

            // Stream for the with-VSC path: 4 RL-skip zero bits (one per
            // column, since all columns are now eligible).
            var encVsc = new Jp2MqEncoder();
            byte[] ctxVscEnc = Jp2MqContextSet.CreateInitialised();
            for (var c = 0; c < 4; c++)
                encVsc.Encode(0, ref ctxVscEnc[Jp2MqContextSet.RunLength]);
            encVsc.Flush();
            byte[] dataVsc = encVsc.ToArray();

            Tier1State stateVsc = SeedAtRow4(W, H);
            CleanupPass.Run(stateVsc, new Jp2MqDecoder(dataVsc, 0, dataVsc.Length),
                Jp2MqContextSet.CreateInitialised(),
                SubbandOrientation.LL, FirstBp, vsc: true);
            // Nothing new became significant; (0,4) still the only sig.
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < W; x++)
                Assert.False(stateVsc.HasFlag(x, y, Tier1State.SignificanceFlag));

            // Without VSC, column 0 isn't RL-eligible (its y=3 neighbour
            // (0,4) is sig). To produce a working stream we'd need the
            // per-sample path for that column — that's already exercised by
            // existing CUP tests. Here we just confirm RL eligibility
            // differs by directly running with VSC on a stream that
            // assumes RL skip on every column; rejection happens at
            // encode-time, so we don't try to assert on the without-VSC
            // decode here.
        }

        // ---- Round trips -------------------------------------------------

        [Fact]
        public void Vsc_FullRoundTrip_TwoStripes()
        {
            const int W = 4, H = 8;
            const int FirstBp = 5;
            const int Passes = 7;

            var planState = new Tier1State(W, H);
            byte[] data = EncodeVscPasses(planState, FirstBp, Passes,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 1,
                pickSig: (x, y) => (x + y) % 3 == 0 ? 1 : 0,
                pickSign: (x, _) => x % 2,
                pickMrBit: (_, y) => y % 2);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                FirstBp, vsc: true);
            driver.RunPasses(new Jp2MqDecoder(data, 0, data.Length), Passes);

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        [Fact]
        public void Vsc_PlusLazy_FullRoundTrip()
        {
            // Eight-tall block, pass count crosses the LAZY boundary AND the
            // stripe boundary. Both styles active.
            const int W = 4, H = 8;
            const int FirstBp = 5;
            const int Passes = 13;

            var planState = new Tier1State(W, H);
            var segments = new List<(byte[] Bytes, bool Raw, int Passes)>();
            byte[] contexts = Jp2MqContextSet.CreateInitialised();

            var p = 0;
            while (p < Passes)
            {
                bool raw = IsRawSlot(p);
                int spanEnd = p + 1;
                while (spanEnd < Passes && IsRawSlot(spanEnd) == raw)
                {
                    if (!raw && spanEnd >= 10) break;
                    spanEnd++;
                }
                int span = spanEnd - p;

                if (raw)
                {
                    var w = new Tier1RawBitWriter();
                    for (var i = 0; i < span; i++)
                        EncodeRawByKind(planState, w, FirstBp, p + i,
                            pickSig: (x, y) => (x + y) % 2,
                            pickSign: (x, _) => x % 2,
                            pickMrBit: (_, y) => y % 2);
                    w.Flush();
                    segments.Add((w.ToArray(), true, span));
                }
                else
                {
                    var enc = new Jp2MqEncoder();
                    for (var i = 0; i < span; i++)
                        EncodeMqByKind(planState, enc, contexts, FirstBp, p + i,
                            pickRl: x => x == 0 ? 1 : 0,
                            pickRlIndex: _ => 1,
                            pickSig: (x, y) => (x + y) % 3 == 0 ? 1 : 0,
                            pickSign: (x, _) => x % 2,
                            pickMrBit: (_, y) => y % 2,
                            vsc: true);
                    enc.Flush();
                    segments.Add((enc.ToArray(), false, span));
                }
                p = spanEnd;
            }

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                FirstBp, bypass: true, vsc: true);
            foreach ((byte[] bytes, bool raw, int passes) in segments)
            {
                if (raw) driver.RunRawPasses(bytes, 0, bytes.Length, passes);
                else
                {
                    var mq = new Jp2MqDecoder(bytes, 0, bytes.Length);
                    driver.RunPasses(mq, passes);
                }
            }

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        [Fact]
        public void Vsc_OnSingleStripeBlock_NoEffect()
        {
            // 4×4 block has only one stripe. The last row (y=3) is the last
            // row of the codeblock too, so its south neighbours are off-grid
            // (guard band returns zero) — VSC masking is a no-op. Verify
            // that VSC and non-VSC decode the same stream identically.
            const int W = 4, H = 4;
            const int FirstBp = 5;

            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref contexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            var noVsc = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp);
            noVsc.RunPasses(new Jp2MqDecoder(data, 0, data.Length), 1);

            var vsc = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp, vsc: true);
            vsc.RunPasses(new Jp2MqDecoder(data, 0, data.Length), 1);

            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                Assert.Equal(noVsc.State.GetFlags(x, y), vsc.State.GetFlags(x, y));
                Assert.Equal(noVsc.State.GetMagnitude(x, y), vsc.State.GetMagnitude(x, y));
            }
        }

        // ---- Helpers -----------------------------------------------------

        private static Tier1State SeedAtRow4(int w, int h)
        {
            var s = new Tier1State(w, h);
            s.SetFlag(0, 4, Tier1State.SignificanceFlag);
            return s;
        }

        private static byte[] EncodeTwoZeroSigBits()
        {
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            // (0,3): south bit set → neighbourhood 0x40. ZC context for LL +
            // 0x40 is one specific lookup; we don't care about the exact
            // value since we're encoding 0 anyway. Same for (1,3).
            var ctx1 = (byte)Tier1Contexts.ZeroCoding(SubbandOrientation.LL, 0x40);
            var ctx2 = (byte)Tier1Contexts.ZeroCoding(SubbandOrientation.LL, 0x20);
            enc.Encode(0, ref contexts[ctx1]);
            enc.Encode(0, ref contexts[ctx2]);
            enc.Flush();
            return enc.ToArray();
        }

        private static byte[] EncodeVscPasses(
            Tier1State plan, int firstBitPlane, int passes,
            Func<int, int> pickRl, Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit)
        {
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var p = 0; p < passes; p++)
                EncodeMqByKind(plan, enc, contexts, firstBitPlane, p,
                    pickRl, pickRlIndex, pickSig, pickSign, pickMrBit, vsc: true);
            enc.Flush();
            return enc.ToArray();
        }

        private static void EncodeMqByKind(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            int firstBitPlane, int passIndex,
            Func<int, int> pickRl, Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool vsc)
        {
            int planeOffset = (passIndex + 2) / 3;
            int bitPlane = firstBitPlane - planeOffset;
            int kind = (passIndex + 2) % 3;
            switch (kind)
            {
                case 0:
                    state.ResetVisited();
                    EncodeMqSpp(state, enc, contexts, bitPlane, pickSig, pickSign, vsc);
                    break;
                case 1:
                    EncodeMqMrp(state, enc, contexts, bitPlane, pickMrBit, vsc);
                    break;
                case 2:
                    if (passIndex == 0) state.ResetVisited();
                    EncodeMqCup(state, enc, contexts, bitPlane,
                        pickRl, pickRlIndex, pickSig, pickSign, vsc);
                    break;
            }
        }

        private static void EncodeRawByKind(
            Tier1State state, Tier1RawBitWriter w,
            int firstBitPlane, int passIndex,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit)
        {
            int planeOffset = (passIndex + 2) / 3;
            int bitPlane = firstBitPlane - planeOffset;
            int kind = (passIndex + 2) % 3;
            switch (kind)
            {
                case 0:
                    state.ResetVisited();
                    EncodeRawSpp(state, w, bitPlane, pickSig, pickSign, vsc: true);
                    break;
                case 1:
                    EncodeRawMrp(state, w, bitPlane, pickMrBit);
                    break;
                default:
                    throw new InvalidOperationException("CUP cannot be raw.");
            }
        }

        private static void EncodeMqSpp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign, bool vsc)
        {
            int width = state.Width;
            int paddedHeight = state.PaddedHeight;
            int actualHeight = state.Height;
            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                for (int y = stripeTop; y < stripeBottom; y++)
                {
                    if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                    bool mask = vsc && (y % 4 == 3);
                    byte neigh = state.GetSignificanceNeighbourhood(x, y, mask);
                    if (neigh == 0) continue;
                    int zcCtx = Tier1Contexts.ZeroCoding(SubbandOrientation.LL, neigh);
                    int sig = pickSig(x, y);
                    enc.Encode(sig, ref contexts[zcCtx]);
                    if (sig == 1) EncodeMqNewSig(state, enc, contexts, x, y, bitPlane, pickSign, vsc);
                    state.SetFlag(x, y, Tier1State.VisitedFlag);
                }
            }
        }

        private static void EncodeMqMrp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
            Func<int, int, int> pickBit, bool vsc)
        {
            int width = state.Width;
            int paddedHeight = state.PaddedHeight;
            int actualHeight = state.Height;
            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                for (int y = stripeTop; y < stripeBottom; y++)
                {
                    if (!state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                    if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;
                    bool refined = state.HasFlag(x, y, Tier1State.RefinedFlag);
                    bool mask = vsc && (y % 4 == 3);
                    int neighCount = state.CountSignificantNeighbours(x, y, mask);
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

        private static void EncodeMqCup(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
            Func<int, int> pickRl, Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign, bool vsc)
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
                    if (stripeHeight == 4 && IsRunLengthEligible(state, x, stripeTop, vsc))
                    {
                        int rl = pickRl(x);
                        enc.Encode(rl, ref contexts[Jp2MqContextSet.RunLength]);
                        if (rl == 0) continue;
                        int k = pickRlIndex(x) & 3;
                        enc.Encode((k >> 1) & 1, ref contexts[Jp2MqContextSet.Uniform]);
                        enc.Encode(k & 1, ref contexts[Jp2MqContextSet.Uniform]);
                        EncodeMqNewSig(state, enc, contexts, x, stripeTop + k, bitPlane, pickSign, vsc);
                        processStartY = stripeTop + k + 1;
                    }
                    for (int y = processStartY; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;
                        bool mask = vsc && (y % 4 == 3);
                        byte neigh = state.GetSignificanceNeighbourhood(x, y, mask);
                        int zcCtx = Tier1Contexts.ZeroCoding(SubbandOrientation.LL, neigh);
                        int sig = pickSig(x, y);
                        enc.Encode(sig, ref contexts[zcCtx]);
                        if (sig == 1) EncodeMqNewSig(state, enc, contexts, x, y, bitPlane, pickSign, vsc);
                    }
                }
            }
        }

        private static void EncodeRawSpp(
            Tier1State state, Tier1RawBitWriter w, int bitPlane,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign, bool vsc)
        {
            int width = state.Width;
            int paddedHeight = state.PaddedHeight;
            int actualHeight = state.Height;
            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                for (int y = stripeTop; y < stripeBottom; y++)
                {
                    if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                    bool mask = vsc && (y % 4 == 3);
                    byte neigh = state.GetSignificanceNeighbourhood(x, y, mask);
                    if (neigh == 0) continue;
                    int sig = pickSig(x, y);
                    w.WriteBit(sig);
                    if (sig == 1)
                    {
                        int sign = pickSign(x, y);
                        w.WriteBit(sign);
                        state.SetFlag(x, y, Tier1State.SignificanceFlag);
                        if (sign == 1) state.SetFlag(x, y, Tier1State.SignFlag);
                        state.SetMagnitude(x, y, 1 << bitPlane);
                    }
                    state.SetFlag(x, y, Tier1State.VisitedFlag);
                }
            }
        }

        private static void EncodeRawMrp(
            Tier1State state, Tier1RawBitWriter w, int bitPlane,
            Func<int, int, int> pickBit)
        {
            int width = state.Width;
            int paddedHeight = state.PaddedHeight;
            int actualHeight = state.Height;
            for (var stripeTop = 0; stripeTop < paddedHeight; stripeTop += 4)
            {
                int stripeBottom = Math.Min(stripeTop + 4, actualHeight);
                for (var x = 0; x < width; x++)
                for (int y = stripeTop; y < stripeBottom; y++)
                {
                    if (!state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                    if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;
                    int bit = pickBit(x, y);
                    w.WriteBit(bit);
                    int mag = state.GetMagnitude(x, y);
                    mag |= bit << bitPlane;
                    state.SetMagnitude(x, y, mag);
                    state.SetFlag(x, y, Tier1State.RefinedFlag);
                }
            }
        }

        private static void EncodeMqNewSig(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts,
            int x, int y, int bitPlane, Func<int, int, int> pickSign, bool vsc)
        {
            bool mask = vsc && (y % 4 == 3);
            int hC = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.West) +
                state.GetSignContribution(x, y, NeighbourDirection.East));
            int southC = mask ? 0 : state.GetSignContribution(x, y, NeighbourDirection.South);
            int vC = Math.Sign(
                state.GetSignContribution(x, y, NeighbourDirection.North) + southC);
            (int scContext, int xorBit) = Tier1Contexts.SignCoding(hC, vC);
            int rawSign = pickSign(x, y);
            int encodedBit = rawSign ^ xorBit;
            enc.Encode(encodedBit, ref contexts[scContext]);
            state.SetFlag(x, y, Tier1State.SignificanceFlag);
            if (rawSign == 1) state.SetFlag(x, y, Tier1State.SignFlag);
            state.SetMagnitude(x, y, 1 << bitPlane);
        }

        private static bool IsRunLengthEligible(Tier1State state, int x, int stripeTop, bool vsc)
        {
            for (int y = stripeTop; y < stripeTop + 4; y++)
            {
                if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) return false;
                if (state.HasFlag(x, y, Tier1State.VisitedFlag)) return false;
                bool mask = vsc && (y % 4 == 3);
                if (state.GetSignificanceNeighbourhood(x, y, mask) != 0) return false;
            }
            return true;
        }

        private static bool IsRawSlot(int passIndex)
        {
            if (passIndex < 10) return false;
            int kind = (passIndex + 2) % 3;
            return kind != 2;
        }

        private static void AssertStatesMatch(Tier1State expected, Tier1State actual)
        {
            for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.True(expected.GetFlags(x, y) == actual.GetFlags(x, y),
                    $"flags ({x},{y}): plan=0x{expected.GetFlags(x, y):X2}, " +
                    $"drv=0x{actual.GetFlags(x, y):X2}");
                Assert.Equal(expected.GetMagnitude(x, y), actual.GetMagnitude(x, y));
            }
        }
    }
}
