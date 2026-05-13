using System;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    /// <summary>
    /// Direct unit coverage for the raw-coded SPP and MRP used by the LAZY
    /// (selective arithmetic coding bypass) style — ISO/IEC 15444-1 D.6.
    /// Each test builds an encoder-plan state alongside the decoder state,
    /// emits raw bits with a helper that walks the grid in the exact same
    /// order the decoder will, then runs the decoder and asserts the two
    /// states match. Driver-level coverage lives in
    /// <see cref="Tier1LazyTests"/>.
    /// </summary>
    public sealed class Tier1RawPassTests
    {
        private const int W = 4, H = 4;

        // ---- Raw SPP -----------------------------------------------------

        [Fact]
        public void RawSpp_NoCandidates_ConsumesNoBits()
        {
            // Empty state has no significant coefficients, so no candidate
            // has a non-zero neighbourhood. A buffer of all-1s would mark
            // every coefficient significant if SPP misbehaved — the test
            // confirms the state stays untouched.
            var state = new Tier1State(W, H);
            byte[] buf = [0xFF, 0xFF];
            var reader = new Tier1RawBitReader(buf, 0, buf.Length);
            RawSignificancePropagationPass.Run(state, reader, bitPlane: 0);

            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(0, state.GetFlags(x, y));
        }

        [Fact]
        public void RawSpp_SeedAtTopLeft_AllCandidatesInsignificant_RoundTrips()
        {
            // (0,0) seeded. With every candidate's sig bit zero no cascading
            // can occur and the candidate set is exactly the initial
            // neighbours of (0,0): (0,1), (1,0), (1,1). Verify all three
            // wear VisitedFlag and none became significant.
            const int Bp = 2;
            var plan = new Tier1State(W, H);
            plan.SetFlag(0, 0, Tier1State.SignificanceFlag);
            plan.SetMagnitude(0, 0, 1 << (Bp + 3)); // upper bit-plane

            var dec = new Tier1State(W, H);
            dec.SetFlag(0, 0, Tier1State.SignificanceFlag);
            dec.SetMagnitude(0, 0, 1 << (Bp + 3));

            byte[] data = EncodeRawSpp(plan, Bp,
                pickSig: (_, _) => 0,
                pickSign: (_, _) => 0);

            var reader = new Tier1RawBitReader(data, 0, data.Length);
            RawSignificancePropagationPass.Run(dec, reader, Bp);

            AssertStatesMatch(plan, dec);
            Assert.True(dec.HasFlag(0, 1, Tier1State.VisitedFlag));
            Assert.True(dec.HasFlag(1, 0, Tier1State.VisitedFlag));
            Assert.True(dec.HasFlag(1, 1, Tier1State.VisitedFlag));
        }

        [Fact]
        public void RawSpp_SignBitGoesIntoSignFlagWithoutXor()
        {
            // Critical raw-vs-MQ difference: per D.6 Equation (D-2) the raw
            // sign bit IS the sign (1 = negative). No XOR with a sign-coding
            // predictor based on neighbour state. Test by encoding a sign=1
            // for a candidate whose horizontal+vertical neighbour
            // contributions would push the MQ-mode predictor toward a flip;
            // raw must still record the bit verbatim.
            const int Bp = 2;
            var plan = new Tier1State(W, H);
            plan.SetFlag(0, 0, Tier1State.SignificanceFlag);
            plan.SetMagnitude(0, 0, 1 << (Bp + 3));

            var dec = new Tier1State(W, H);
            dec.SetFlag(0, 0, Tier1State.SignificanceFlag);
            dec.SetMagnitude(0, 0, 1 << (Bp + 3));

            byte[] data = EncodeRawSpp(plan, Bp,
                pickSig: (x, y) => x == 1 && y == 0 ? 1 : 0,
                pickSign: (_, _) => 1);

            var reader = new Tier1RawBitReader(data, 0, data.Length);
            RawSignificancePropagationPass.Run(dec, reader, Bp);

            AssertStatesMatch(plan, dec);
            Assert.True(dec.HasFlag(1, 0, Tier1State.SignificanceFlag));
            Assert.True(dec.HasFlag(1, 0, Tier1State.SignFlag));
            Assert.Equal(1 << Bp, dec.GetMagnitude(1, 0));
        }

        // ---- Raw MRP -----------------------------------------------------

        [Fact]
        public void RawMrp_NoCandidates_ConsumesNoBits()
        {
            var state = new Tier1State(W, H);
            byte[] buf = [0xFF, 0xFF];
            var reader = new Tier1RawBitReader(buf, 0, buf.Length);
            RawMagnitudeRefinementPass.Run(state, reader, bitPlane: 0);
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(0, state.GetFlags(x, y));
        }

        [Fact]
        public void RawMrp_OrsBitsIntoMagnitudeAtCurrentBitPlane()
        {
            const int SeedBp = 4;
            const int RefineBp = 2;
            var plan = new Tier1State(W, H);
            var dec = new Tier1State(W, H);
            foreach ((int x, int y) in new[] { (0, 0), (0, 1), (0, 2) })
            {
                plan.SetFlag(x, y, Tier1State.SignificanceFlag);
                plan.SetMagnitude(x, y, 1 << SeedBp);
                dec.SetFlag(x, y, Tier1State.SignificanceFlag);
                dec.SetMagnitude(x, y, 1 << SeedBp);
            }

            byte[] data = EncodeRawMrp(plan, RefineBp,
                pickBit: (x, y) => y == 1 ? 0 : 1);

            var reader = new Tier1RawBitReader(data, 0, data.Length);
            RawMagnitudeRefinementPass.Run(dec, reader, RefineBp);

            AssertStatesMatch(plan, dec);
            Assert.Equal((1 << SeedBp) | (1 << RefineBp), dec.GetMagnitude(0, 0));
            Assert.Equal(1 << SeedBp,                     dec.GetMagnitude(0, 1));
            Assert.Equal((1 << SeedBp) | (1 << RefineBp), dec.GetMagnitude(0, 2));
        }

        [Fact]
        public void RawMrp_SkipsCoefficientsVisitedThisBitPlane()
        {
            // A coefficient that became significant during this bit-plane's
            // SPP carries the VisitedFlag through the bit-plane. MRP must
            // skip such coefficients. Confirm by seeding the visited flag on
            // a significant coefficient and verifying MRP reads zero bits
            // from a buffer of all-1s.
            const int Bp = 2;
            var state = new Tier1State(W, H);
            state.SetFlag(0, 0, Tier1State.SignificanceFlag);
            state.SetFlag(0, 0, Tier1State.VisitedFlag);
            state.SetMagnitude(0, 0, 1 << Bp);

            byte[] buf = [0xFF, 0xFF];
            var reader = new Tier1RawBitReader(buf, 0, buf.Length);
            RawMagnitudeRefinementPass.Run(state, reader, Bp);

            Assert.False(state.HasFlag(0, 0, Tier1State.RefinedFlag));
            Assert.Equal(1 << Bp, state.GetMagnitude(0, 0));
        }

        // ---- Helpers -----------------------------------------------------

        private static byte[] EncodeRawSpp(
            Tier1State state, int bitPlane,
            Func<int, int, int> pickSig,
            Func<int, int, int> pickSign)
        {
            var w = new Tier1RawBitWriter();
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

            w.Flush();
            return w.ToArray();
        }

        private static byte[] EncodeRawMrp(
            Tier1State state, int bitPlane,
            Func<int, int, int> pickBit)
        {
            var w = new Tier1RawBitWriter();
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

                        int bit = pickBit(x, y);
                        w.WriteBit(bit);
                        int mag = state.GetMagnitude(x, y);
                        mag |= bit << bitPlane;
                        state.SetMagnitude(x, y, mag);
                        state.SetFlag(x, y, Tier1State.RefinedFlag);
                    }
                }
            }

            w.Flush();
            return w.ToArray();
        }

        private static void AssertStatesMatch(Tier1State expected, Tier1State actual)
        {
            for (var y = 0; y < expected.Height; y++)
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.True(expected.GetFlags(x, y) == actual.GetFlags(x, y),
                    $"flags ({x},{y}): plan=0x{expected.GetFlags(x, y):X2}, " +
                    $"dec=0x{actual.GetFlags(x, y):X2}");
                Assert.Equal(expected.GetMagnitude(x, y), actual.GetMagnitude(x, y));
            }
        }
    }
}
