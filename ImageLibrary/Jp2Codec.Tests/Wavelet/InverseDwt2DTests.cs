using System;
using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class InverseDwt2DTests
    {
        private const float Tolerance97 = 5e-3f;

        // ==== 5/3: constant signal reconstructs ============================

        [Fact]
        public void Reverse53_ConstantSignal_FlatReconstruction()
        {
            // 4×4 parent, parity (0,0). One forward 2D step:
            //   LL all c, HL all 0, LH all 0, HH all 0.
            const int c = 33;
            int[,] ll = Fill(2, 2, c);
            int[,] hl = Fill(2, 2, 0);
            int[,] lh = Fill(2, 2, 0);
            int[,] hh = Fill(2, 2, 0);

            int[,] result = InverseDwt2D.Reverse53(ll, hl, lh, hh, u0Parity: 0, v0Parity: 0);
            AssertConst(result, expected: c);
        }

        // ==== 5/3: round-trip across parities + dimensions =================

        [Theory]
        [InlineData(8, 8, 0, 0)]
        [InlineData(7, 8, 0, 0)]
        [InlineData(8, 7, 0, 0)]
        [InlineData(7, 7, 0, 0)]
        [InlineData(8, 8, 1, 0)]
        [InlineData(8, 8, 0, 1)]
        [InlineData(8, 8, 1, 1)]
        [InlineData(7, 7, 1, 1)]
        [InlineData(13, 11, 1, 0)]
        public void Reverse53_RoundTrip_RandomImage(int height, int width, int u0Parity, int v0Parity)
        {
            int[,] x = MakeRandomInt(height, width, seed: 1000 + height * 31 + width * 7 + (u0Parity << 1) + v0Parity);
            (int[,] ll, int[,] hl, int[,] lh, int[,] hh) = Forward53_2D(x, u0Parity, v0Parity);
            int[,] recovered = InverseDwt2D.Reverse53(ll, hl, lh, hh, u0Parity, v0Parity);

            Assert.Equal(height, recovered.GetLength(0));
            Assert.Equal(width, recovered.GetLength(1));
            AssertEqual2D(x, recovered);
        }

        // ==== 9/7: constant signal reconstructs ============================

        [Fact]
        public void Reverse97_ConstantSignal_FlatReconstruction()
        {
            const float c = 5.5f;
            float[,] ll = Fill(2, 2, c);
            float[,] hl = Fill(2, 2, 0f);
            float[,] lh = Fill(2, 2, 0f);
            float[,] hh = Fill(2, 2, 0f);

            float[,] result = InverseDwt2D.Reverse97(ll, hl, lh, hh, u0Parity: 0, v0Parity: 0);
            AssertClose2D(result, c, 1e-4f);
        }

        // ==== 9/7: round-trip ==============================================

        [Theory]
        [InlineData(8, 8, 0, 0)]
        [InlineData(7, 8, 0, 0)]
        [InlineData(8, 7, 0, 0)]
        [InlineData(7, 7, 0, 0)]
        [InlineData(8, 8, 1, 0)]
        [InlineData(8, 8, 0, 1)]
        [InlineData(8, 8, 1, 1)]
        [InlineData(11, 13, 1, 1)]
        public void Reverse97_RoundTrip_RandomImage(int height, int width, int u0Parity, int v0Parity)
        {
            float[,] x = MakeRandomFloat(height, width, seed: 5000 + height * 31 + width * 7 + (u0Parity << 1) + v0Parity);
            (float[,] ll, float[,] hl, float[,] lh, float[,] hh) = Forward97_2D(x, u0Parity, v0Parity);
            float[,] recovered = InverseDwt2D.Reverse97(ll, hl, lh, hh, u0Parity, v0Parity);

            Assert.Equal(height, recovered.GetLength(0));
            Assert.Equal(width, recovered.GetLength(1));
            AssertCloseElementwise2D(x, recovered, Tolerance97);
        }

        // ==== Shape validation =============================================

        [Fact]
        public void Reverse53_InconsistentSubbandShapes_Throws()
        {
            int[,] ll = new int[2, 2];
            int[,] hl = new int[3, 2]; // wrong row count
            int[,] lh = new int[2, 2];
            int[,] hh = new int[2, 2];
            Assert.Throws<ArgumentException>(() =>
                InverseDwt2D.Reverse53(ll, hl, lh, hh, 0, 0));
        }

        [Fact]
        public void Reverse97_InconsistentSubbandShapes_Throws()
        {
            float[,] ll = new float[2, 2];
            float[,] hl = new float[2, 2];
            float[,] lh = new float[2, 3]; // wrong column count
            float[,] hh = new float[2, 2];
            Assert.Throws<ArgumentException>(() =>
                InverseDwt2D.Reverse97(ll, hl, lh, hh, 0, 0));
        }

        // ==== Test helpers =================================================

        /// <summary>
        /// One-level forward 2D 5/3 DWT used to round-trip the inverse.
        /// Mirrors the inverse pipeline: forward 1D on each column (VER_SD),
        /// then forward 1D on each row (HOR_SD), then 2D_DEINTERLEAVE.
        /// </summary>
        private static (int[,] ll, int[,] hl, int[,] lh, int[,] hh) Forward53_2D(
            int[,] x, int u0Parity, int v0Parity)
        {
            int h = x.GetLength(0), w = x.GetLength(1);
            var a = new int[h, w];
            for (var r = 0; r < h; r++) for (var c = 0; c < w; c++) a[r, c] = x[r, c];

            // Forward order in the spec is VER_SD then HOR_SD (so the inverse
            // unwinds with HOR_SR then VER_SR — same as our InverseDwt2D).
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

            // 2D_DEINTERLEAVE into 4 subbands.
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

        private static (float[,] ll, float[,] hl, float[,] lh, float[,] hh) Forward97_2D(
            float[,] x, int u0Parity, int v0Parity)
        {
            int h = x.GetLength(0), w = x.GetLength(1);
            var a = new float[h, w];
            for (var r = 0; r < h; r++) for (var c = 0; c < w; c++) a[r, c] = x[r, c];

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

        private static int[,] Fill(int h, int w, int v)
        {
            var arr = new int[h, w];
            for (var r = 0; r < h; r++) for (var c = 0; c < w; c++) arr[r, c] = v;
            return arr;
        }

        private static float[,] Fill(int h, int w, float v)
        {
            var arr = new float[h, w];
            for (var r = 0; r < h; r++) for (var c = 0; c < w; c++) arr[r, c] = v;
            return arr;
        }

        private static int[,] MakeRandomInt(int h, int w, int seed)
        {
            var rng = new Random(seed);
            var arr = new int[h, w];
            for (var r = 0; r < h; r++)
                for (var c = 0; c < w; c++)
                    arr[r, c] = rng.Next(-1024, 1024);
            return arr;
        }

        private static float[,] MakeRandomFloat(int h, int w, int seed)
        {
            var rng = new Random(seed);
            var arr = new float[h, w];
            for (var r = 0; r < h; r++)
                for (var c = 0; c < w; c++)
                    arr[r, c] = (float)(rng.NextDouble() * 2048.0 - 1024.0);
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

        private static void AssertConst(int[,] arr, int expected)
        {
            for (var r = 0; r < arr.GetLength(0); r++)
                for (var c = 0; c < arr.GetLength(1); c++)
                    Assert.Equal(expected, arr[r, c]);
        }

        private static void AssertClose2D(float[,] arr, float expected, float tol)
        {
            for (var r = 0; r < arr.GetLength(0); r++)
                for (var c = 0; c < arr.GetLength(1); c++)
                    Assert.InRange(arr[r, c], expected - tol, expected + tol);
        }

        private static void AssertCloseElementwise2D(float[,] expected, float[,] actual, float tol)
        {
            Assert.Equal(expected.GetLength(0), actual.GetLength(0));
            Assert.Equal(expected.GetLength(1), actual.GetLength(1));
            for (var r = 0; r < expected.GetLength(0); r++)
            {
                for (var c = 0; c < expected.GetLength(1); c++)
                {
                    float diff = MathF.Abs(expected[r, c] - actual[r, c]);
                    Assert.True(
                        diff <= tol,
                        $"At ({r},{c}): expected {expected[r, c]:R}, got {actual[r, c]:R}, diff {diff:R} > {tol}");
                }
            }
        }
    }
}
