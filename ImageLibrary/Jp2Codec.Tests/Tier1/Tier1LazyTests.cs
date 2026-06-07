using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Driver-level coverage of the selective arithmetic coding bypass
    /// (LAZY) code-block style — ISO/IEC 15444-1 D.6 / Table A-19 bit 0 /
    /// Table D.9. Passes 0–9 stay on MQ; from pass 10 onward SPP and MRP go
    /// raw while CUP keeps MQ. Without TERMALL the raw SPP and raw MRP for
    /// a single bit-plane share one byte segment, terminated to the byte
    /// boundary after the MRP; with TERMALL each pass is its own segment.
    /// </summary>
    public sealed class Tier1LazyTests
    {
        private const int W = 4, H = 4;

        // ---- Slot classification -----------------------------------------

        [Fact]
        public void RunPasses_BeyondPass10_RawSppRefused()
        {
            // First nine MQ passes are fine; pass 10 would be SPP@bp(firstBp-4)
            // which under LAZY must come through RunRawPasses.
            const int FirstBp = 5;
            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, bypass: true);

            LazyContribution[] contribs = BuildLazyContributions(
                new Tier1State(W, H), FirstBp, totalPasses: 9,
                pickRl: _ => 0, pickRlIndex: _ => 0,
                pickSig: (_, _) => 0, pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);

            FeedContributions(driver, contribs);

            byte[] extra = EncodeMqOnly(new Tier1State(W, H), FirstBp,
                fromPass: 9, passes: 2,
                pickRl: _ => 0, pickRlIndex: _ => 0,
                pickSig: (_, _) => 0, pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);

            // Pass 9 is CUP@(firstBp-3) — still MQ, fine to start the call.
            // Pass 10 is the first raw slot — driver should refuse mid-loop.
            var mq = new Jp2MqDecoder(extra, 0, extra.Length);
            Assert.Throws<InvalidOperationException>(() => driver.RunPasses(mq, 2));
        }

        [Fact]
        public void RunRawPasses_WithoutBypass_Throws()
        {
            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, bypass: false);
            byte[] data = [0x00];
            Assert.Throws<InvalidOperationException>(() => driver.RunRawPasses(data, 0, 1, 1));
        }

        [Fact]
        public void RunRawPasses_OnMqSlot_Throws()
        {
            // Pass 0 (CUP@firstBp) is always MQ, even under LAZY. Calling
            // RunRawPasses on the very first pass must throw — caller is
            // confused about the segmentation.
            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: 5, bypass: true);
            byte[] data = [0x00];
            Assert.Throws<InvalidOperationException>(() => driver.RunRawPasses(data, 0, 1, 1));
        }

        // ---- Round trips -------------------------------------------------

        [Fact]
        public void LazyDefault_EmptyActivity_RoundTrips()
        {
            // firstBp=5 → with 13 passes we cover the first raw bit-plane
            // segment (SPP@1 + MRP@1) and a return-to-MQ CUP at bp 1.
            const int FirstBp = 5;
            const int Passes = 13;

            var planState = new Tier1State(W, H);
            LazyContribution[] contribs = BuildLazyContributions(
                planState, FirstBp, Passes,
                pickRl: _ => 0, pickRlIndex: _ => 0,
                pickSig: (_, _) => 0, pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0);

            // Sanity: we expect exactly 3 contributions under default style
            //   1: passes 0..9 (MQ)        — covers 10 passes
            //   2: passes 10..11 (raw)     — covers 2 passes
            //   3: pass 12 (MQ)            — covers 1 pass
            Assert.Equal(3, contribs.Length);
            Assert.False(contribs[0].IsRaw);
            Assert.Equal(10, contribs[0].PassCount);
            Assert.True(contribs[1].IsRaw);
            Assert.Equal(2, contribs[1].PassCount);
            Assert.False(contribs[2].IsRaw);
            Assert.Equal(1, contribs[2].PassCount);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, bypass: true);
            FeedContributions(driver, contribs);

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        [Fact]
        public void LazyDefault_NonTrivialActivity_RoundTrips()
        {
            // Column 0 becomes significant at row 2 during pass 0 (CUP@5)
            // via the RL-aggregation path. Through bp 4..2 the rest of
            // column 0 / row 2's neighbours wander through SPP/MRP/CUP under
            // MQ. Pass 10 (SPP@1) — the first raw pass — has at least a
            // couple of candidates whose 8-neighbourhood includes (0,2)
            // and its newer significant siblings. Pass 11 (MRP@1) refines.
            // Pass 12 (CUP@1) sweeps the leftovers.
            const int FirstBp = 5;
            const int Passes = 13;

            var planState = new Tier1State(W, H);
            LazyContribution[] contribs = BuildLazyContributions(
                planState, FirstBp, Passes,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (x, y) => (x + y) % 3 == 0 ? 1 : 0,
                pickSign: (x, _) => x % 2,
                pickMrBit: (_, y) => y % 2);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, bypass: true);
            FeedContributions(driver, contribs);

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        [Fact]
        public void LazyPlusTermall_RoundTrips()
        {
            // With TERMALL on, every pass becomes its own segment — including
            // the raw passes, so the SPP and MRP raw segments are separate
            // even though the spec normally bundles them.
            const int FirstBp = 5;
            const int Passes = 13;

            var planState = new Tier1State(W, H);
            LazyContribution[] contribs = BuildLazyContributions(
                planState, FirstBp, Passes,
                pickRl: x => x == 0 ? 1 : 0,
                pickRlIndex: _ => 2,
                pickSig: (x, y) => (x + y) % 3 == 0 ? 1 : 0,
                pickSign: (x, _) => x % 2,
                pickMrBit: (_, y) => y % 2,
                termall: true);

            // Termall: every pass is its own contribution (13 total).
            Assert.Equal(Passes, contribs.Length);
            for (var p = 0; p < Passes; p++)
                Assert.Equal(1, contribs[p].PassCount);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, bypass: true);
            FeedContributions(driver, contribs);

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        [Fact]
        public void LazyPlusSegSym_RoundTrips()
        {
            // SEGSYM appends a four-bit pattern to every CUP. Under LAZY the
            // CUP passes still go through MQ and the SEGSYM tail still lands
            // on the uniform context. The raw SPP/MRP between CUPs do not
            // emit a segsym tail — that's an MQ-only concept.
            const int FirstBp = 5;
            const int Passes = 13;

            var planState = new Tier1State(W, H);
            LazyContribution[] contribs = BuildLazyContributions(
                planState, FirstBp, Passes,
                pickRl: _ => 0, pickRlIndex: _ => 0,
                pickSig: (_, _) => 0, pickSign: (_, _) => 0,
                pickMrBit: (_, _) => 0,
                segSym: true);

            var driver = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL,
                firstBitPlane: FirstBp, bypass: true, segSym: true);
            FeedContributions(driver, contribs);

            Assert.Equal(Passes, driver.PassesCompleted);
            AssertStatesMatch(planState, driver.State);
        }

        // ---- Test helpers ------------------------------------------------

        private sealed record LazyContribution(byte[] Data, bool IsRaw, int PassCount);

        private static void FeedContributions(
            Tier1CodeBlockDecoder driver,
            LazyContribution[] contributions)
        {
            foreach (LazyContribution c in contributions)
            {
                if (c.IsRaw)
                {
                    driver.RunRawPasses(c.Data, 0, c.Data.Length, c.PassCount);
                }
                else
                {
                    var mq = new Jp2MqDecoder(c.Data, 0, c.Data.Length);
                    driver.RunPasses(mq, c.PassCount);
                }
            }
        }

        private static LazyContribution[] BuildLazyContributions(
            Tier1State state, int firstBitPlane, int totalPasses,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool termall = false,
            bool segSym = false)
        {
            var contributions = new List<LazyContribution>();
            byte[] contexts = Jp2MqContextSet.CreateInitialised();

            int p = 0;
            while (p < totalPasses)
            {
                bool raw = IsRawSlot(p);

                int spanStart = p;
                int spanEnd = spanStart + 1;

                if (!termall)
                {
                    // Default style: group consecutive same-modality passes
                    // into one segment. For MQ this means everything before
                    // pass 10, plus every CUP at the raw boundary; for raw
                    // this means SPP+MRP at one bit-plane.
                    while (spanEnd < totalPasses && IsRawSlot(spanEnd) == raw)
                    {
                        // Under default style we stop the first MQ group at
                        // pass 9 (terminated before raw mode kicks in). After
                        // pass 9 every MQ slot is just the single CUP between
                        // raw bit-planes — terminated on both sides — so the
                        // grouping there is naturally one pass per segment.
                        if (!raw && spanEnd >= 10) break;
                        spanEnd++;
                    }
                }

                int span = spanEnd - spanStart;
                byte[] segBytes = raw
                    ? EncodeRawSpan(state, firstBitPlane, spanStart, span,
                        pickSig, pickSign, pickMrBit)
                    : EncodeMqSpan(state, contexts, firstBitPlane, spanStart, span,
                        pickRl, pickRlIndex, pickSig, pickSign, pickMrBit, segSym);

                contributions.Add(new LazyContribution(segBytes, raw, span));
                p = spanEnd;
            }

            return contributions.ToArray();
        }

        private static byte[] EncodeMqOnly(
            Tier1State state, int firstBitPlane, int fromPass, int passes,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit)
        {
            byte[] contexts = Jp2MqContextSet.CreateInitialised();
            return EncodeMqSpan(state, contexts, firstBitPlane, fromPass, passes,
                pickRl, pickRlIndex, pickSig, pickSign, pickMrBit, segSym: false);
        }

        private static byte[] EncodeMqSpan(
            Tier1State state, byte[] contexts,
            int firstBitPlane, int fromPass, int passes,
            Func<int, int> pickRl,
            Func<int, int> pickRlIndex,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit,
            bool segSym)
        {
            var enc = new Jp2MqEncoder();
            for (var i = 0; i < passes; i++)
            {
                int p = fromPass + i;
                (int bitPlane, int kind) = LookupPass(firstBitPlane, p);
                switch (kind)
                {
                    case 0: // SPP
                        state.ResetVisited();
                        EncodeMqSpp(state, enc, contexts, bitPlane, pickSig, pickSign);
                        break;
                    case 1: // MRP
                        EncodeMqMrp(state, enc, contexts, bitPlane, pickMrBit);
                        break;
                    case 2: // CUP
                        if (p == 0) state.ResetVisited();
                        EncodeMqCup(state, enc, contexts, bitPlane,
                            pickRl, pickRlIndex, pickSig, pickSign);
                        if (segSym)
                            EncodeSegSym(enc, contexts);
                        break;
                }
            }
            enc.Flush();
            return enc.ToArray();
        }

        private static byte[] EncodeRawSpan(
            Tier1State state, int firstBitPlane, int fromPass, int passes,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign,
            Func<int, int, int> pickMrBit)
        {
            var w = new Tier1RawBitWriter();
            for (var i = 0; i < passes; i++)
            {
                int p = fromPass + i;
                (int bitPlane, int kind) = LookupPass(firstBitPlane, p);
                switch (kind)
                {
                    case 0: // raw SPP
                        state.ResetVisited();
                        EncodeRawSpp(state, w, bitPlane, pickSig, pickSign);
                        break;
                    case 1: // raw MRP
                        EncodeRawMrp(state, w, bitPlane, pickMrBit);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Cleanup pass cannot appear in a raw span.");
                }
            }
            w.Flush();
            return w.ToArray();
        }

        // ---- Per-pass encoders --------------------------------------------

        private static void EncodeMqSpp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign)
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
                    byte neigh = state.GetSignificanceNeighbourhood(x, y);
                    if (neigh == 0) continue;
                    int zcCtx = Tier1Contexts.ZeroCoding(SubbandOrientation.LL, neigh);
                    int sig = pickSig(x, y);
                    enc.Encode(sig, ref contexts[zcCtx]);
                    if (sig == 1)
                        EncodeMqNewSig(state, enc, contexts, x, y, bitPlane, pickSign);
                    state.SetFlag(x, y, Tier1State.VisitedFlag);
                }
            }
        }

        private static void EncodeMqMrp(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
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

        private static void EncodeMqCup(
            Tier1State state, Jp2MqEncoder enc, byte[] contexts, int bitPlane,
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
                        EncodeMqNewSig(state, enc, contexts, x, stripeTop + k, bitPlane, pickSign);
                        processStartY = stripeTop + k + 1;
                    }
                    for (int y = processStartY; y < stripeBottom; y++)
                    {
                        if (state.HasFlag(x, y, Tier1State.SignificanceFlag)) continue;
                        if (state.HasFlag(x, y, Tier1State.VisitedFlag)) continue;
                        byte neigh = state.GetSignificanceNeighbourhood(x, y);
                        int zcCtx = Tier1Contexts.ZeroCoding(SubbandOrientation.LL, neigh);
                        int sig = pickSig(x, y);
                        enc.Encode(sig, ref contexts[zcCtx]);
                        if (sig == 1)
                            EncodeMqNewSig(state, enc, contexts, x, y, bitPlane, pickSign);
                    }
                }
            }
        }

        private static void EncodeRawSpp(
            Tier1State state, Tier1RawBitWriter w, int bitPlane,
            Func<int, int, int> pickSig, Func<int, int, int> pickSign)
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
                    byte neigh = state.GetSignificanceNeighbourhood(x, y);
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

        private static void EncodeSegSym(Jp2MqEncoder enc, byte[] contexts)
        {
            int[] bits = [1, 0, 1, 0];
            foreach (int b in bits)
                enc.Encode(b, ref contexts[Jp2MqContextSet.Uniform]);
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

        private static (int BitPlane, int Kind) LookupPass(int firstBitPlane, int passIndex)
        {
            int planeOffset = (passIndex + 2) / 3;
            return (firstBitPlane - planeOffset, (passIndex + 2) % 3);
        }

        private static bool IsRawSlot(int passIndex)
        {
            if (passIndex < 10) return false;
            int kind = (passIndex + 2) % 3;
            return kind != 2; // 0 = SPP, 1 = MRP — both raw; 2 = CUP, MQ
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
