using Jp2Codec.Color;

namespace Jp2Codec.Tests.Color
{
    public sealed class InverseRctTests
    {
        // ==== Hand-built vector (single sample) ==================================

        [Fact]
        public void KnownVector_RgbRoundTrip_RecoversOriginal()
        {
            // (R, G, B) = (200, 100, 50)
            // Forward (G.1..G.3):
            //   Y_0 = floor((200 + 2*100 + 50) / 4) = floor(450/4) = 112
            //   Y_1 = B - G   =  50 - 100 = -50
            //   Y_2 = R - G   = 200 - 100 = 100
            int[,] c0 = { { 112 } };
            int[,] c1 = { { -50 } };
            int[,] c2 = { { 100 } };

            InverseRct.Apply(c0, c1, c2);

            // Inverse should land back on (R, G, B) = (200, 100, 50)
            // with C0=R, C1=G, C2=B per the encoder's component ordering.
            Assert.Equal(200, c0[0, 0]); // R
            Assert.Equal(100, c1[0, 0]); // G
            Assert.Equal(50, c2[0, 0]);  // B
        }

        [Fact]
        public void Zero_MapsToZero()
        {
            int[,] c0 = { { 0, 0 } };
            int[,] c1 = { { 0, 0 } };
            int[,] c2 = { { 0, 0 } };

            InverseRct.Apply(c0, c1, c2);

            Assert.Equal(0, c0[0, 0]);
            Assert.Equal(0, c0[0, 1]);
            Assert.Equal(0, c1[0, 0]);
            Assert.Equal(0, c1[0, 1]);
            Assert.Equal(0, c2[0, 0]);
            Assert.Equal(0, c2[0, 1]);
        }

        // ==== Property-based roundtrip ===========================================

        [Fact]
        public void Forward_Inverse_IdentityOnSmallGrid()
        {
            // Seeded values pulled from a small grid covering positive,
            // negative, and zero RGB samples (allowed when the input was
            // signed or post-level-shift). The roundtrip is exact for the
            // reversible transform.
            int[,] original0 = {
                { 200, 0, -100, 255 },
                { 50, 128, -1, 1 },
                { 10, 20, 30, 40 },
            };
            int[,] original1 = {
                { 100, 0, 50, 200 },
                { 25, 64, 0, 0 },
                { 5, 15, 25, 35 },
            };
            int[,] original2 = {
                { 50, 0, 200, -10 },
                { 75, 192, 1, -1 },
                { 0, 10, 20, 30 },
            };

            (int[,] y0, int[,] y1, int[,] y2) =
                ForwardRct(original0, original1, original2);

            InverseRct.Apply(y0, y1, y2);

            for (var y = 0; y < 3; y++)
                for (var x = 0; x < 4; x++)
                {
                    Assert.Equal(original0[y, x], y0[y, x]);
                    Assert.Equal(original1[y, x], y1[y, x]);
                    Assert.Equal(original2[y, x], y2[y, x]);
                }
        }

        [Fact]
        public void Forward_Inverse_HandlesNegativeChromaSums()
        {
            // Negative (Y_1 + Y_2) is the regression case for naive integer
            // division: C# -1 / 4 == 0, but spec floor(-1/4) == -1. The 5/3
            // path produces negative chroma when R or B is well below G.
            int[,] original0 = { { 10, 20, 30 } };   // R
            int[,] original1 = { { 200, 220, 240 } }; // G
            int[,] original2 = { { 5, 15, 25 } };    // B

            (int[,] y0, int[,] y1, int[,] y2) =
                ForwardRct(original0, original1, original2);

            InverseRct.Apply(y0, y1, y2);

            for (var x = 0; x < 3; x++)
            {
                Assert.Equal(original0[0, x], y0[0, x]);
                Assert.Equal(original1[0, x], y1[0, x]);
                Assert.Equal(original2[0, x], y2[0, x]);
            }
        }

        // ==== Validation =========================================================

        [Fact]
        public void MismatchedDimensions_Throws()
        {
            var c0 = new int[2, 3];
            var c1 = new int[2, 3];
            var c2 = new int[2, 4]; // wrong width

            Assert.Throws<ArgumentException>(() => InverseRct.Apply(c0, c1, c2));
        }

        [Fact]
        public void MismatchedHeights_Throws()
        {
            var c0 = new int[2, 3];
            var c1 = new int[3, 3]; // wrong height
            var c2 = new int[2, 3];

            Assert.Throws<ArgumentException>(() => InverseRct.Apply(c0, c1, c2));
        }

        [Fact]
        public void NullArg_Throws()
        {
            var c0 = new int[1, 1];
            var c1 = new int[1, 1];
            Assert.Throws<ArgumentNullException>(() => InverseRct.Apply(null!, c1, c0));
            Assert.Throws<ArgumentNullException>(() => InverseRct.Apply(c0, null!, c0));
            Assert.Throws<ArgumentNullException>(() => InverseRct.Apply(c0, c1, null!));
        }

        // ==== Forward RCT test helper ============================================

        /// <summary>
        /// Forward RCT per G.1..G.3 — kept inside the test class so the
        /// roundtrip tests can build hand-encoded inputs without exposing
        /// encode-side code from production. Returns NEW arrays.
        /// </summary>
        private static (int[,] y0, int[,] y1, int[,] y2) ForwardRct(
            int[,] c0, int[,] c1, int[,] c2)
        {
            int height = c0.GetLength(0);
            int width = c0.GetLength(1);
            var y0 = new int[height, width];
            var y1 = new int[height, width];
            var y2 = new int[height, width];

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    int i0 = c0[y, x];
                    int i1 = c1[y, x];
                    int i2 = c2[y, x];
                    y0[y, x] = (i0 + 2 * i1 + i2) >> 2; // floor((I_0 + 2*I_1 + I_2) / 4)
                    y1[y, x] = i2 - i1;
                    y2[y, x] = i0 - i1;
                }
            }
            return (y0, y1, y2);
        }
    }
}
