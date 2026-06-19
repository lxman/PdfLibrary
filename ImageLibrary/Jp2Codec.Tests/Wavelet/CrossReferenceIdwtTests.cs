using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    /// <summary>
    /// Cross-checks our 1D inverse 5/3 lift against a re-implementation of
    /// CSJ2K's SynWTFilterIntLift5x3.synthetize_lpf / synthetize_hpf (the
    /// Melville.CSJ2K reference path). Both expand the same canvas-positioned
    /// L/H subband split into the parent signal; differences would explain
    /// the file8.jp2 cross-codec divergence.
    /// </summary>
    public sealed class CrossReferenceIdwtTests
    {
        /// <summary>
        /// Re-implementation of CSJ2K's reversible 5/3 synthesize_lpf — used
        /// when the parent canvas-start is even (lowest sample is low-pass).
        /// </summary>
        private static int[] CsjSynthesizeLpf(int[] low, int[] high)
        {
            int outLen = low.Length + high.Length;
            var outSig = new int[outLen];
            int lk = 0, hk = 0, ik = 0;

            // Generate even samples.
            if (outLen > 1) outSig[ik] = low[lk] - ((high[hk] + 1) >> 1);
            else outSig[ik] = low[lk];
            lk++; hk++; ik += 2;

            for (var i = 2; i < outLen - 1; i += 2)
            {
                outSig[ik] = low[lk] - ((high[hk - 1] + high[hk] + 2) >> 2);
                lk++; hk++; ik += 2;
            }

            // Tail boundary for odd-length parent.
            if ((outLen % 2 == 1) && (outLen > 2))
            {
                outSig[ik] = low[lk] - ((2 * high[hk - 1] + 2) >> 2);
            }

            // Generate odd samples.
            hk = 0; ik = 1;
            for (var i = 1; i < outLen - 1; i += 2)
            {
                outSig[ik] = high[hk] + ((outSig[ik - 1] + outSig[ik + 1]) >> 1);
                hk++; ik += 2;
            }

            // Tail (CSJ2K calls it "head") boundary for even-length parent.
            if (outLen % 2 == 0 && outLen > 1)
            {
                outSig[ik] = high[hk] + outSig[ik - 1];
            }

            return outSig;
        }

        /// <summary>
        /// Re-implementation of CSJ2K's reversible 5/3 synthesize_hpf — used
        /// when the parent canvas-start is odd (lowest sample is high-pass).
        /// Mirrors synthesize_lpf with the L/H roles swapped.
        /// </summary>
        private static int[] CsjSynthesizeHpf(int[] low, int[] high)
        {
            int outLen = low.Length + high.Length;
            var outSig = new int[outLen];
            int lk = 0, hk = 0, ik;

            // Generate even samples (output indices 1, 3, ... since the first
            // canvas-even output is at index 1 when start parity is odd).
            ik = 1;
            for (var i = 1; i < outLen - 1; i += 2)
            {
                outSig[ik] = low[lk] - ((high[hk] + high[hk + 1] + 2) >> 2);
                lk++; hk++; ik += 2;
            }

            if ((outLen > 1) && (outLen % 2 == 0))
            {
                outSig[ik] = low[lk] - ((2 * high[hk] + 2) >> 2);
            }

            // Generate odd samples (output indices 0, 2, ... — start with the
            // boundary case).
            hk = 0; ik = 0;
            if (outLen > 1) outSig[ik] = high[hk] + outSig[ik + 1];
            else outSig[ik] = high[hk] >> 1;
            hk++; ik += 2;

            for (var i = 2; i < outLen - 1; i += 2)
            {
                outSig[ik] = high[hk] + ((outSig[ik - 1] + outSig[ik + 1]) >> 1);
                hk++; ik += 2;
            }

            if (outLen % 2 == 1 && outLen > 1)
            {
                outSig[ik] = high[hk] + outSig[ik - 1];
            }

            return outSig;
        }

        /// <summary>
        /// Our pipeline: interleave L/H by canvas parity, run the 1D inverse
        /// 5/3 lift, return the reconstructed parent signal.
        /// </summary>
        private static int[] OursInverse(int[] low, int[] high, int parity)
        {
            int[] interleaved = InverseInterleave.Combine(low, high, parity);
            return InverseLifting53.Apply(interleaved, parity);
        }

        // ---- Parity 0 (canvas-even start, L first) cases -----------------

        [Theory]
        [InlineData(44, 22, 22)]   // file8 level 5 horizontal: even total, balanced L=H
        [InlineData(50, 25, 25)]   // file8 level 5 vertical (parent dim): odd height per band
        [InlineData(88, 44, 44)]   // file8 level 4 horizontal
        [InlineData(175, 88, 87)]  // file8 level 3 horizontal: ODD total, L=H+1
        [InlineData(100, 50, 50)]  // file8 level 3 vertical
        [InlineData(350, 175, 175)] // file8 level 2 horizontal
        [InlineData(200, 100, 100)] // file8 level 2 vertical
        [InlineData(700, 350, 350)] // file8 level 1 horizontal
        [InlineData(400, 200, 200)] // file8 level 1 vertical
        public void Parity0_MatchesCsj2kReference(int parentLength, int lowCount, int highCount)
        {
            Assert.Equal(parentLength, lowCount + highCount);
            var rng = new Random(0xC512 + parentLength);
            int[] low = MakeRandom(lowCount, rng);
            int[] high = MakeRandom(highCount, rng);

            int[] ours = OursInverse(low, high, parity: 0);
            int[] reference = CsjSynthesizeLpf(low, high);

            Assert.Equal(reference, ours);
        }

        // ---- Parity 1 (canvas-odd start, H first) cases -----------------

        [Theory]
        [InlineData(8, 4, 4)]
        [InlineData(7, 3, 4)]   // odd-length, parity 1: H count > L count
        [InlineData(12, 6, 6)]
        [InlineData(13, 6, 7)]
        public void Parity1_MatchesCsj2kReference(int parentLength, int lowCount, int highCount)
        {
            Assert.Equal(parentLength, lowCount + highCount);
            var rng = new Random(0xC512 + parentLength + 1);
            int[] low = MakeRandom(lowCount, rng);
            int[] high = MakeRandom(highCount, rng);

            int[] ours = OursInverse(low, high, parity: 1);
            int[] reference = CsjSynthesizeHpf(low, high);

            Assert.Equal(reference, ours);
        }

        private static int[] MakeRandom(int count, Random rng)
        {
            var result = new int[count];
            for (var i = 0; i < count; i++)
                result[i] = rng.Next(-1024, 1024);
            return result;
        }

        // ---- 2D cross-reference: file8 level-3 reconstruction (175x100 from
        // LL_3=88x50, HL_3=87x50, LH_3=88x50, HH_3=87x50) ------------------

        [Theory]
        // (parentH, parentW, llH, llW, hlH, hlW, lhH, lhW, hhH, hhW)
        [InlineData( 25,  44, 13, 22, 13, 22, 12, 22, 12, 22)] // file8 level 5
        [InlineData( 50,  88, 25, 44, 25, 44, 25, 44, 25, 44)] // file8 level 4
        [InlineData(100, 175, 50, 88, 50, 87, 50, 88, 50, 87)] // file8 level 3 — width split 88/87
        [InlineData(200, 350,100,175,100,175,100,175,100,175)] // file8 level 2
        [InlineData(400, 700,200,350,200,350,200,350,200,350)] // file8 level 1
        public void Level3_2dIdwt_MatchesCsj2kReference(
            int parentH, int parentW,
            int llH, int llW, int hlH, int hlW, int lhH, int lhW, int hhH, int hhW)
        {
            Assert.Equal(parentH, llH + lhH);
            Assert.Equal(parentW, llW + hlW);

            var rng = new Random(0x52D + parentH * 31 + parentW);
            int[,] ll = MakeRandom2D(llH, llW, rng);
            int[,] hl = MakeRandom2D(hlH, hlW, rng);
            int[,] lh = MakeRandom2D(lhH, lhW, rng);
            int[,] hh = MakeRandom2D(hhH, hhW, rng);

            int[,] ours = InverseDwt2D.Reverse53(ll, hl, lh, hh, u0Parity: 0, v0Parity: 0);
            int[,] reference = Csj2kReference2dReverse53(ll, hl, lh, hh);

            Assert.Equal(parentH, ours.GetLength(0));
            Assert.Equal(parentW, ours.GetLength(1));
            for (var r = 0; r < parentH; r++)
                for (var c = 0; c < parentW; c++)
                    Assert.True(ours[r, c] == reference[r, c],
                        $"Mismatch at [{r},{c}]: ours={ours[r,c]} ref={reference[r,c]}");
        }

        private static int[,] MakeRandom2D(int h, int w, Random rng)
        {
            var arr = new int[h, w];
            for (var r = 0; r < h; r++)
                for (var c = 0; c < w; c++)
                    arr[r, c] = rng.Next(-1024, 1024);
            return arr;
        }

        /// <summary>
        /// CSJ2K-style 2D inverse 5/3 reconstruction at parity (0, 0):
        /// 1) lay the four subbands into a Mallat quadrant grid (LL upper-left,
        ///    HL upper-right, LH lower-left, HH lower-right);
        /// 2) HOR step — for every row, treat buf[0..llW) as low-pass and
        ///    buf[llW..) as high-pass, run <see cref="CsjSynthesizeLpf"/>;
        /// 3) VER step — for every column, treat buf[0..llH) as low-pass and
        ///    buf[llH..) as high-pass, run <see cref="CsjSynthesizeLpf"/>.
        /// This is the exact reconstruction sequence in
        /// SynWTFilterIntLift5x3.synthetize_lpf chained through InvWTFull.
        /// </summary>
        private static int[,] Csj2kReference2dReverse53(
            int[,] ll, int[,] hl, int[,] lh, int[,] hh)
        {
            int llH = ll.GetLength(0), llW = ll.GetLength(1);
            int hlW = hl.GetLength(1);
            int lhH = lh.GetLength(0);
            int parentH = llH + lhH;
            int parentW = llW + hlW;

            var grid = new int[parentH, parentW];

            // Lay leaves in their quadrants.
            for (var r = 0; r < llH; r++)
                for (var c = 0; c < llW; c++) grid[r, c] = ll[r, c];
            for (var r = 0; r < llH; r++)
                for (var c = 0; c < hlW; c++) grid[r, llW + c] = hl[r, c];
            for (var r = 0; r < lhH; r++)
                for (var c = 0; c < llW; c++) grid[llH + r, c] = lh[r, c];
            for (var r = 0; r < lhH; r++)
                for (var c = 0; c < hlW; c++) grid[llH + r, llW + c] = hh[r, c];

            // HOR step: per-row 1D inverse with first half = L, second half = H.
            for (var r = 0; r < parentH; r++)
            {
                var low = new int[llW];
                var high = new int[hlW];
                for (var c = 0; c < llW; c++) low[c] = grid[r, c];
                for (var c = 0; c < hlW; c++) high[c] = grid[r, llW + c];
                int[] rowOut = CsjSynthesizeLpf(low, high);
                for (var c = 0; c < parentW; c++) grid[r, c] = rowOut[c];
            }

            // VER step: per-column 1D inverse with first half = L, second half = H.
            for (var c = 0; c < parentW; c++)
            {
                var low = new int[llH];
                var high = new int[lhH];
                for (var r = 0; r < llH; r++) low[r] = grid[r, c];
                for (var r = 0; r < lhH; r++) high[r] = grid[llH + r, c];
                int[] colOut = CsjSynthesizeLpf(low, high);
                for (var r = 0; r < parentH; r++) grid[r, c] = colOut[r];
            }

            return grid;
        }
    }
}
