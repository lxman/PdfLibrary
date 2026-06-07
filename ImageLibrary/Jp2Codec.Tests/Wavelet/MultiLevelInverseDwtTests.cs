using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class MultiLevelInverseDwtTests
    {
        private const float Tolerance97 = 5e-2f;

        // ==== Zero-level: input is output ==================================

        [Fact]
        public void Reverse53_ZeroLevels_ReturnsLLAsIs()
        {
            int[,] ll = { { 1, 2 }, { 3, 4 } };
            int[,] out_ = MultiLevelInverseDwt.Reverse53(ll, Array.Empty<WaveletLevel53>());
            AssertEqual2D(ll, out_);
        }

        [Fact]
        public void Reverse97_ZeroLevels_ReturnsLLAsIs()
        {
            float[,] ll = { { 1f, 2f }, { 3f, 4f } };
            float[,] out_ = MultiLevelInverseDwt.Reverse97(ll, Array.Empty<WaveletLevel97>());
            AssertClose2D(ll, out_, 1e-6f);
        }

        // ==== Round-trip 5/3 at various depths and parities ================

        [Theory]
        [InlineData(8, 8, 1, 0, 0)]
        [InlineData(8, 8, 2, 0, 0)]
        [InlineData(8, 8, 3, 0, 0)]
        [InlineData(16, 16, 1, 0, 0)]
        [InlineData(16, 16, 2, 0, 0)]
        [InlineData(16, 16, 3, 0, 0)]
        [InlineData(16, 16, 4, 0, 0)]
        [InlineData(13, 11, 2, 1, 0)]
        [InlineData(13, 11, 3, 1, 1)]
        [InlineData(7, 7, 2, 0, 1)]
        [InlineData(9, 5, 2, 1, 1)]
        public void Reverse53_MultiLevel_RoundTrip(int h, int w, int nl, int tcx0, int tcy0)
        {
            int[,] x = MakeRandomInt(h, w, seed: 30000 + h * 313 + w * 7 + nl * 11 + tcx0 * 5 + tcy0);
            (int[,] llDeepest, WaveletLevel53[] levels) = Forward53_MultiLevel(x, nl, tcx0, tcy0);
            int[,] recovered = MultiLevelInverseDwt.Reverse53(llDeepest, levels);

            Assert.Equal(h, recovered.GetLength(0));
            Assert.Equal(w, recovered.GetLength(1));
            AssertEqual2D(x, recovered);
        }

        // ==== Round-trip 9/7 at various depths ============================

        [Theory]
        [InlineData(8, 8, 1, 0, 0)]
        [InlineData(8, 8, 2, 0, 0)]
        [InlineData(8, 8, 3, 0, 0)]
        [InlineData(16, 16, 3, 0, 0)]
        [InlineData(16, 16, 4, 0, 0)]
        [InlineData(13, 11, 2, 1, 0)]
        [InlineData(13, 11, 3, 1, 1)]
        public void Reverse97_MultiLevel_RoundTrip(int h, int w, int nl, int tcx0, int tcy0)
        {
            float[,] x = MakeRandomFloat(h, w, seed: 70000 + h * 313 + w * 7 + nl * 11 + tcx0 * 5 + tcy0);
            (float[,] llDeepest, WaveletLevel97[] levels) = Forward97_MultiLevel(x, nl, tcx0, tcy0);
            float[,] recovered = MultiLevelInverseDwt.Reverse97(llDeepest, levels);

            Assert.Equal(h, recovered.GetLength(0));
            Assert.Equal(w, recovered.GetLength(1));
            AssertCloseElementwise2D(x, recovered, Tolerance97);
        }

        // ==== Constant signal collapses to a single LL coefficient =========

        [Fact]
        public void Reverse53_ConstantInput_FlatReconstructionAcrossLevels()
        {
            // Forward 5/3 of a constant produces LL = c, all HL/LH/HH = 0.
            // Multi-level forward propagates the constant down: at level n,
            // LL_n still equals the constant (since each forward step preserves
            // the DC for a flat input).
            int[,] x = Fill(8, 8, 5);
            (int[,] ll, WaveletLevel53[] levels) = Forward53_MultiLevel(x, decompositionLevels: 3, tcx0: 0, tcy0: 0);
            int[,] recovered = MultiLevelInverseDwt.Reverse53(ll, levels);
            AssertEqual2D(x, recovered);
        }

        // ==== Forward 5/3 multi-level helper ==============================

        private static (int[,] llDeepest, WaveletLevel53[] levels) Forward53_MultiLevel(
            int[,] x, int decompositionLevels, int tcx0, int tcy0)
        {
            int[,] currentLL = Clone2D(x);
            int curTcx0 = tcx0, curTcy0 = tcy0;
            var levels = new WaveletLevel53[decompositionLevels];

            for (var lev = 1; lev <= decompositionLevels; lev++)
            {
                int u0Parity = curTcx0 & 1;
                int v0Parity = curTcy0 & 1;
                (int[,] ll, int[,] hl, int[,] lh, int[,] hh) =
                    Forward53_OneLevel(currentLL, u0Parity, v0Parity);

                // The canvas of LL at the NEXT level is (ceil(currentTcx0/2), ceil(currentTcy0/2)).
                int nextTcx0 = (curTcx0 + 1) >> 1;
                int nextTcy0 = (curTcy0 + 1) >> 1;

                levels[lev - 1] = new WaveletLevel53(hl, lh, hh, u0Parity, v0Parity);

                currentLL = ll;
                curTcx0 = nextTcx0;
                curTcy0 = nextTcy0;
            }
            return (currentLL, levels);
        }

        private static (int[,] ll, int[,] hl, int[,] lh, int[,] hh) Forward53_OneLevel(
            int[,] x, int u0Parity, int v0Parity)
        {
            int h = x.GetLength(0), w = x.GetLength(1);
            int[,] a = Clone2D(x);

            // VER_SD then HOR_SD (matches the inverse pipeline's HOR_SR then VER_SR).
            var colBuf = new int[h];
            for (var c = 0; c < w; c++)
            {
                for (var r = 0; r < h; r++) colBuf[r] = a[r, c];
                int[] colOut = Forward53_1D(colBuf, v0Parity);
                for (var r = 0; r < h; r++) a[r, c] = colOut[r];
            }
            var rowBuf = new int[w];
            for (var r = 0; r < h; r++)
            {
                for (var c = 0; c < w; c++) rowBuf[c] = a[r, c];
                int[] rowOut = Forward53_1D(rowBuf, u0Parity);
                for (var c = 0; c < w; c++) a[r, c] = rowOut[c];
            }

            int llW = (w + (u0Parity == 0 ? 1 : 0)) / 2;
            int hlW = w - llW;
            int llH = (h + (v0Parity == 0 ? 1 : 0)) / 2;
            int lhH = h - llH;

            int[,] ll = new int[llH, llW];
            int[,] hl = new int[llH, hlW];
            int[,] lh = new int[lhH, llW];
            int[,] hh = new int[lhH, hlW];

            for (var r = 0; r < h; r++)
            {
                int vP = (r + v0Parity) & 1;
                int sr = r / 2;
                for (var c = 0; c < w; c++)
                {
                    int hP = (c + u0Parity) & 1;
                    int sc = c / 2;
                    switch ((hP, vP))
                    {
                        case (0, 0): ll[sr, sc] = a[r, c]; break;
                        case (1, 0): hl[sr, sc] = a[r, c]; break;
                        case (0, 1): lh[sr, sc] = a[r, c]; break;
                        default: hh[sr, sc] = a[r, c]; break;
                    }
                }
            }
            return (ll, hl, lh, hh);
        }

        private static int[] Forward53_1D(int[] x, int parity)
        {
            int length = x.Length;
            if (length == 1)
            {
                int v = x[0];
                return new[] { parity == 0 ? v : v * 2 };
            }

            const int pad = 2;
            var buf = new int[length + 2 * pad];
            Array.Copy(x, 0, buf, pad, length);
            SymmetricExtension.Fill(buf, pad, length);

            int firstEven = (parity == 0) ? 0 : 1;
            int firstOdd = (parity == 0) ? 1 : 0;

            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] -= (buf[b - 1] + buf[b + 1]) >> 1;
            }
            SymmetricExtension.Fill(buf, pad, length);
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += (buf[b - 1] + buf[b + 1] + 2) >> 2;
            }

            var result = new int[length];
            Array.Copy(buf, pad, result, 0, length);
            return result;
        }

        // ==== Forward 9/7 multi-level helper ==============================

        private static (float[,] llDeepest, WaveletLevel97[] levels) Forward97_MultiLevel(
            float[,] x, int decompositionLevels, int tcx0, int tcy0)
        {
            float[,] currentLL = Clone2D(x);
            int curTcx0 = tcx0, curTcy0 = tcy0;
            var levels = new WaveletLevel97[decompositionLevels];

            for (var lev = 1; lev <= decompositionLevels; lev++)
            {
                int u0Parity = curTcx0 & 1;
                int v0Parity = curTcy0 & 1;
                (float[,] ll, float[,] hl, float[,] lh, float[,] hh) =
                    Forward97_OneLevel(currentLL, u0Parity, v0Parity);

                int nextTcx0 = (curTcx0 + 1) >> 1;
                int nextTcy0 = (curTcy0 + 1) >> 1;

                levels[lev - 1] = new WaveletLevel97(hl, lh, hh, u0Parity, v0Parity);
                currentLL = ll;
                curTcx0 = nextTcx0;
                curTcy0 = nextTcy0;
            }
            return (currentLL, levels);
        }

        private static (float[,] ll, float[,] hl, float[,] lh, float[,] hh) Forward97_OneLevel(
            float[,] x, int u0Parity, int v0Parity)
        {
            int h = x.GetLength(0), w = x.GetLength(1);
            float[,] a = Clone2D(x);

            var colBuf = new float[h];
            for (var c = 0; c < w; c++)
            {
                for (var r = 0; r < h; r++) colBuf[r] = a[r, c];
                float[] colOut = Forward97_1D(colBuf, v0Parity);
                for (var r = 0; r < h; r++) a[r, c] = colOut[r];
            }
            var rowBuf = new float[w];
            for (var r = 0; r < h; r++)
            {
                for (var c = 0; c < w; c++) rowBuf[c] = a[r, c];
                float[] rowOut = Forward97_1D(rowBuf, u0Parity);
                for (var c = 0; c < w; c++) a[r, c] = rowOut[c];
            }

            int llW = (w + (u0Parity == 0 ? 1 : 0)) / 2;
            int hlW = w - llW;
            int llH = (h + (v0Parity == 0 ? 1 : 0)) / 2;
            int lhH = h - llH;

            float[,] ll = new float[llH, llW];
            float[,] hl = new float[llH, hlW];
            float[,] lh = new float[lhH, llW];
            float[,] hh = new float[lhH, hlW];

            for (var r = 0; r < h; r++)
            {
                int vP = (r + v0Parity) & 1;
                int sr = r / 2;
                for (var c = 0; c < w; c++)
                {
                    int hP = (c + u0Parity) & 1;
                    int sc = c / 2;
                    switch ((hP, vP))
                    {
                        case (0, 0): ll[sr, sc] = a[r, c]; break;
                        case (1, 0): hl[sr, sc] = a[r, c]; break;
                        case (0, 1): lh[sr, sc] = a[r, c]; break;
                        default: hh[sr, sc] = a[r, c]; break;
                    }
                }
            }
            return (ll, hl, lh, hh);
        }

        private static float[] Forward97_1D(float[] x, int parity)
        {
            int length = x.Length;
            if (length == 1)
            {
                float v = x[0];
                return new[] { parity == 0 ? v : v * 2f };
            }

            const int pad = 2;
            var buf = new float[length + 2 * pad];
            Array.Copy(x, 0, buf, pad, length);
            SymmetricExtension.Fill(buf, pad, length);

            int firstEven = (parity == 0) ? 0 : 1;
            int firstOdd = (parity == 0) ? 1 : 0;

            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Alpha * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Beta * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);
            for (int local = firstOdd; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Gamma * (buf[b - 1] + buf[b + 1]);
            }
            SymmetricExtension.Fill(buf, pad, length);
            for (int local = firstEven; local < length; local += 2)
            {
                int b = local + pad;
                buf[b] += WaveletConstants.Delta * (buf[b - 1] + buf[b + 1]);
            }
            for (int local = firstOdd; local < length; local += 2)
                buf[local + pad] *= WaveletConstants.K;
            for (int local = firstEven; local < length; local += 2)
                buf[local + pad] *= WaveletConstants.InvK;

            var result = new float[length];
            Array.Copy(buf, pad, result, 0, length);
            return result;
        }

        // ==== Generic 2D helpers ===========================================

        private static int[,] Clone2D(int[,] src)
        {
            int h = src.GetLength(0), w = src.GetLength(1);
            var copy = new int[h, w];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        private static float[,] Clone2D(float[,] src)
        {
            int h = src.GetLength(0), w = src.GetLength(1);
            var copy = new float[h, w];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        private static int[,] Fill(int h, int w, int v)
        {
            var arr = new int[h, w];
            for (var r = 0; r < h; r++) for (var c = 0; c < w; c++) arr[r, c] = v;
            return arr;
        }

        private static int[,] MakeRandomInt(int h, int w, int seed)
        {
            var rng = new Random(seed);
            var arr = new int[h, w];
            for (var r = 0; r < h; r++)
                for (var c = 0; c < w; c++)
                    arr[r, c] = rng.Next(-256, 256);
            return arr;
        }

        private static float[,] MakeRandomFloat(int h, int w, int seed)
        {
            var rng = new Random(seed);
            var arr = new float[h, w];
            for (var r = 0; r < h; r++)
                for (var c = 0; c < w; c++)
                    arr[r, c] = (float)(rng.NextDouble() * 512.0 - 256.0);
            return arr;
        }

        private static void AssertEqual2D(int[,] expected, int[,] actual)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (var r = 0; r < expected.GetLength(0); r++)
                for (var c = 0; c < expected.GetLength(1); c++)
                    Assert.Equal(expected[r, c], actual[r, c]);
        }

        private static void AssertClose2D(float[,] expected, float[,] actual, float tol)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (var r = 0; r < expected.GetLength(0); r++)
                for (var c = 0; c < expected.GetLength(1); c++)
                {
                    float diff = MathF.Abs(expected[r, c] - actual[r, c]);
                    Assert.True(diff <= tol,
                        $"At ({r},{c}): expected {expected[r, c]:R}, got {actual[r, c]:R}");
                }
        }

        private static void AssertCloseElementwise2D(float[,] expected, float[,] actual, float tol)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (var r = 0; r < expected.GetLength(0); r++)
                for (var c = 0; c < expected.GetLength(1); c++)
                {
                    float diff = MathF.Abs(expected[r, c] - actual[r, c]);
                    Assert.True(diff <= tol,
                        $"At ({r},{c}): expected {expected[r, c]:R}, got {actual[r, c]:R}, diff {diff:R} > {tol}");
                }
        }
    }
}
