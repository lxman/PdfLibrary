using Jp2Codec.Wavelet;

namespace Jp2Codec.Tests.Wavelet
{
    public sealed class SymmetricExtensionTests
    {
        // ==== Reflect: in-range indices pass through =======================

        [Theory]
        [InlineData(0, 7, 0)]
        [InlineData(3, 7, 3)]
        [InlineData(6, 7, 6)]
        public void Reflect_InRange_IsIdentity(int idx, int len, int expected)
        {
            Assert.Equal(expected, SymmetricExtension.Reflect(idx, len));
        }

        // ==== Reflect: simple left-side reflection =========================

        [Theory]
        [InlineData(-1, 7, 1)]   // ABCDEFG → ...B|A|B → index -1 = B
        [InlineData(-2, 7, 2)]   // ...C|B|A|B|C → index -2 = C
        [InlineData(-3, 7, 3)]   // ...D|C|B|A → index -3 = D
        public void Reflect_LeftBoundary(int idx, int len, int expected)
        {
            Assert.Equal(expected, SymmetricExtension.Reflect(idx, len));
        }

        // ==== Reflect: simple right-side reflection ========================

        [Theory]
        [InlineData(7, 7, 5)]    // ABCDEFG: idx 6 = G, idx 7 = F (rightmost-1 = 5)
        [InlineData(8, 7, 4)]    // idx 8 = E (rightmost-2 = 4)
        [InlineData(9, 7, 3)]    // idx 9 = D
        public void Reflect_RightBoundary(int idx, int len, int expected)
        {
            Assert.Equal(expected, SymmetricExtension.Reflect(idx, len));
        }

        // ==== Reflect: multi-bounce reflection (short signal) ==============

        [Fact]
        public void Reflect_MultiBounce_LengthFour()
        {
            // Length 4: ABCD, period = 6.
            // Pattern extended both directions: ...DCBA BCDC BABC DCBA...
            // Sequence by index from -7 to 12:
            // idx:    -7 -6 -5 -4 -3 -2 -1  0  1  2  3  4  5  6  7  8  9 10 11 12
            // val:     A  B  C  D  C  B  A  B  C  D  C  B  A  B  C  D  C  B  A  B
            // letters A=0, B=1, C=2, D=3
            int[] expected = { 1, 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 2, 1, 0, 1, 2, 3, 2, 1, 0 };
            for (int idx = -7, j = 0; idx <= 12; idx++, j++)
            {
                Assert.Equal(expected[j], SymmetricExtension.Reflect(idx, 4));
            }
        }

        [Fact]
        public void Reflect_MultiBounce_LengthTwo()
        {
            // Length 2: AB, period = 2. Pattern: ...BABA BABA...
            // idx: -3 -2 -1  0  1  2  3
            // val:  B  A  B  A  B  A  B
            Assert.Equal(1, SymmetricExtension.Reflect(-3, 2));
            Assert.Equal(0, SymmetricExtension.Reflect(-2, 2));
            Assert.Equal(1, SymmetricExtension.Reflect(-1, 2));
            Assert.Equal(0, SymmetricExtension.Reflect(0, 2));
            Assert.Equal(1, SymmetricExtension.Reflect(1, 2));
            Assert.Equal(0, SymmetricExtension.Reflect(2, 2));
            Assert.Equal(1, SymmetricExtension.Reflect(3, 2));
        }

        [Fact]
        public void Reflect_LengthOne_AlwaysReturnsZero()
        {
            for (int idx = -5; idx <= 5; idx++)
            {
                Assert.Equal(0, SymmetricExtension.Reflect(idx, 1));
            }
        }

        [Fact]
        public void Reflect_InvalidLength_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => SymmetricExtension.Reflect(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => SymmetricExtension.Reflect(0, -1));
        }

        // ==== Fill (int): mirror at the boundaries =========================

        [Fact]
        public void Fill_Int_TwoPadEachSide_ReflectsAcrossBoundary()
        {
            // Buffer: [_ _ | 10 20 30 40 50 | _ _]
            // After fill (period = 8):
            //   left padding: idx -1 → 20, idx -2 → 30
            //   right padding: idx 5 → 40, idx 6 → 30
            var buf = new int[9];
            for (var k = 0; k < 5; k++) buf[2 + k] = 10 + 10 * k;

            SymmetricExtension.Fill(buf, dataStart: 2, dataLength: 5);

            Assert.Equal(new[] { 30, 20, 10, 20, 30, 40, 50, 40, 30 }, buf);
        }

        [Fact]
        public void Fill_Int_AsymmetricPadding_OkayBothSides()
        {
            // 1 left, 3 right.
            var buf = new int[8];
            for (var k = 0; k < 4; k++) buf[1 + k] = 100 + k;
            // data = 100 101 102 103
            // left pad idx 0 (rel -1) → 101
            // right pad idx 5,6,7 (rel 4,5,6) → 102, 101, 100
            SymmetricExtension.Fill(buf, dataStart: 1, dataLength: 4);

            Assert.Equal(new[] { 101, 100, 101, 102, 103, 102, 101, 100 }, buf);
        }

        [Fact]
        public void Fill_Int_LengthOne_AllPadsBecomeTheLoneSample()
        {
            var buf = new int[7];
            buf[3] = 42;
            SymmetricExtension.Fill(buf, dataStart: 3, dataLength: 1);
            Assert.All(buf, v => Assert.Equal(42, v));
        }

        // ==== Fill (float): same shape ====================================

        [Fact]
        public void Fill_Float_ReflectsAcrossBoundary()
        {
            var buf = new float[9];
            for (var k = 0; k < 5; k++) buf[2 + k] = 0.5f + 0.5f * k;
            // data: 0.5, 1.0, 1.5, 2.0, 2.5
            SymmetricExtension.Fill(buf, dataStart: 2, dataLength: 5);

            float[] expected = { 1.5f, 1.0f, 0.5f, 1.0f, 1.5f, 2.0f, 2.5f, 2.0f, 1.5f };
            Assert.Equal(expected, buf);
        }

        [Fact]
        public void Fill_Float_MultiBounceShortData()
        {
            // length 2 data ABAB, pad 3 each side → BABABAB plus 3 reflections per side.
            var buf = new float[8];
            buf[3] = 1.0f;
            buf[4] = 2.0f;
            SymmetricExtension.Fill(buf, dataStart: 3, dataLength: 2);

            // Period = 2 for length-2 data → pattern ...1 2 1 2 1 2 1 2...
            // (whole-sample symmetric on length 2 degenerates to plain periodic)
            float[] expected = { 2f, 1f, 2f, 1f, 2f, 1f, 2f, 1f };
            Assert.Equal(expected, buf);
        }

        // ==== Fill: argument validation ====================================

        [Fact]
        public void Fill_OverlapsBufferEnd_Throws()
        {
            var buf = new int[5];
            Assert.Throws<ArgumentException>(() =>
                SymmetricExtension.Fill(buf, dataStart: 3, dataLength: 5));
        }

        [Fact]
        public void Fill_NegativeStart_Throws()
        {
            var buf = new int[5];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SymmetricExtension.Fill(buf, dataStart: -1, dataLength: 3));
        }

        [Fact]
        public void Fill_ZeroLength_Throws()
        {
            var buf = new int[5];
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SymmetricExtension.Fill(buf, dataStart: 0, dataLength: 0));
        }
    }
}
