using System;
using Jp2Codec.Color;

namespace Jp2Codec.Tests.Color
{
    public sealed class InverseIctTests
    {
        // ==== Hand-built vector (single sample) ==================================

        [Fact]
        public void KnownVector_RgbRoundTrip_RecoversOriginalWithinFloatTolerance()
        {
            // (R, G, B) = (200, 100, 50) → forward ICT (G.7..G.9):
            //   Y_0 = 0.299*200 + 0.587*100 + 0.114*50  = 124.2
            //   Y_1 = -0.16875*200 - 0.33126*100 + 0.5*50 = -41.876
            //   Y_2 = 0.5*200 - 0.41869*100 - 0.08131*50 = 54.0655
            float[,] c0 = { { 124.2f } };
            float[,] c1 = { { -41.876f } };
            float[,] c2 = { { 54.0655f } };

            InverseIct.Apply(c0, c1, c2);

            Assert.Equal(200.0f, c0[0, 0], 2); // R
            Assert.Equal(100.0f, c1[0, 0], 2); // G
            Assert.Equal(50.0f, c2[0, 0], 2);  // B
        }

        [Fact]
        public void Zero_MapsToZero()
        {
            float[,] c0 = { { 0f, 0f } };
            float[,] c1 = { { 0f, 0f } };
            float[,] c2 = { { 0f, 0f } };

            InverseIct.Apply(c0, c1, c2);

            Assert.Equal(0f, c0[0, 0]);
            Assert.Equal(0f, c0[0, 1]);
            Assert.Equal(0f, c1[0, 0]);
            Assert.Equal(0f, c1[0, 1]);
            Assert.Equal(0f, c2[0, 0]);
            Assert.Equal(0f, c2[0, 1]);
        }

        [Fact]
        public void LumaOnly_ZeroChroma_GivesEqualRgb()
        {
            // Y=128, Cb=0, Cr=0 → R=G=B=128 (greyscale).
            float[,] c0 = { { 128f } };
            float[,] c1 = { { 0f } };
            float[,] c2 = { { 0f } };

            InverseIct.Apply(c0, c1, c2);

            Assert.Equal(128f, c0[0, 0], 3);
            Assert.Equal(128f, c1[0, 0], 3);
            Assert.Equal(128f, c2[0, 0], 3);
        }

        // ==== Property-based roundtrip ===========================================

        [Fact]
        public void Forward_Inverse_IdentityOnSmallGrid()
        {
            // Spec note F.13 warns that the reconstructed values may stray
            // from the originals due to float rounding; the matrix itself
            // is exactly invertible only to within ~1e-3 because of the
            // 5-digit constants. We allow a 0.001 tolerance per sample.
            float[,] original0 = {
                { 200f, 0f, 100f, 255f },
                { 50f, 128f, 25f, 1f },
                { 10f, 20f, 30f, 40f },
            };
            float[,] original1 = {
                { 100f, 0f, 50f, 200f },
                { 25f, 64f, 12f, 50f },
                { 5f, 15f, 25f, 35f },
            };
            float[,] original2 = {
                { 50f, 0f, 200f, 64f },
                { 75f, 192f, 100f, 200f },
                { 1f, 10f, 20f, 30f },
            };

            (float[,] y0, float[,] y1, float[,] y2) =
                ForwardIct(original0, original1, original2);

            InverseIct.Apply(y0, y1, y2);

            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 4; x++)
                {
                    Assert.Equal(original0[y, x], y0[y, x], 2);
                    Assert.Equal(original1[y, x], y1[y, x], 2);
                    Assert.Equal(original2[y, x], y2[y, x], 2);
                }
        }

        [Fact]
        public void Forward_Inverse_NegativeShiftedValues()
        {
            // After encoder-side level shift, components are centered
            // around zero (e.g. 8-bit unsigned → range [-128, 127]). Make
            // sure the ICT still roundtrips for the negative half.
            float[,] original0 = { { -128f, -64f, 64f, 127f } };
            float[,] original1 = { { -100f, 50f, -50f, 100f } };
            float[,] original2 = { { 0f, -32f, 32f, -64f } };

            (float[,] y0, float[,] y1, float[,] y2) =
                ForwardIct(original0, original1, original2);

            InverseIct.Apply(y0, y1, y2);

            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(original0[0, x], y0[0, x], 2);
                Assert.Equal(original1[0, x], y1[0, x], 2);
                Assert.Equal(original2[0, x], y2[0, x], 2);
            }
        }

        // ==== Validation =========================================================

        [Fact]
        public void MismatchedDimensions_Throws()
        {
            float[,] c0 = new float[2, 3];
            float[,] c1 = new float[2, 3];
            float[,] c2 = new float[2, 4];

            Assert.Throws<ArgumentException>(() => InverseIct.Apply(c0, c1, c2));
        }

        [Fact]
        public void NullArg_Throws()
        {
            float[,] c0 = new float[1, 1];
            float[,] c1 = new float[1, 1];
            Assert.Throws<ArgumentNullException>(() => InverseIct.Apply(null!, c1, c0));
            Assert.Throws<ArgumentNullException>(() => InverseIct.Apply(c0, null!, c0));
            Assert.Throws<ArgumentNullException>(() => InverseIct.Apply(c0, c1, null!));
        }

        // ==== Forward ICT test helper ============================================

        /// <summary>
        /// Forward ICT per G.7..G.9. Kept inside the test class so the
        /// roundtrip tests can build hand-encoded inputs without exposing
        /// encode-side code from production. Returns NEW arrays.
        /// </summary>
        private static (float[,] y0, float[,] y1, float[,] y2) ForwardIct(
            float[,] c0, float[,] c1, float[,] c2)
        {
            int height = c0.GetLength(0);
            int width = c0.GetLength(1);
            var y0 = new float[height, width];
            var y1 = new float[height, width];
            var y2 = new float[height, width];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    float i0 = c0[y, x];
                    float i1 = c1[y, x];
                    float i2 = c2[y, x];
                    y0[y, x] = 0.299f * i0 + 0.587f * i1 + 0.114f * i2;
                    y1[y, x] = -0.16875f * i0 - 0.33126f * i1 + 0.5f * i2;
                    y2[y, x] = 0.5f * i0 - 0.41869f * i1 - 0.08131f * i2;
                }
            }
            return (y0, y1, y2);
        }
    }
}
