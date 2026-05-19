using Jp2Codec.Mq;
using Jp2Codec.Tests.Mq;
using Jp2Codec.Tier1;

namespace Jp2Codec.Tests.Tier1
{
    public sealed class Tier1CoefficientExtractorTests
    {
        [Fact]
        public void Extract_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => Tier1CoefficientExtractor.Extract(null!));
        }

        [Fact]
        public void Extract_FreshState_AllZeros()
        {
            var s = new Tier1State(5, 7);
            int[,] grid = Tier1CoefficientExtractor.Extract(s);

            Assert.Equal(7, grid.GetLength(0)); // rows = Height
            Assert.Equal(5, grid.GetLength(1)); // cols = Width
            for (var y = 0; y < 7; y++)
            for (var x = 0; x < 5; x++)
                Assert.Equal(0, grid[y, x]);
        }

        [Fact]
        public void Extract_OutputShape_MatchesHeightThenWidth()
        {
            // 3×11 state — verify the [y, x] indexing convention by writing
            // a known value in a non-square configuration where mixing the
            // axes would produce a visible bug.
            var s = new Tier1State(3, 11);
            s.SetFlag(2, 10, Tier1State.SignificanceFlag);
            s.SetMagnitude(2, 10, 42);

            int[,] grid = Tier1CoefficientExtractor.Extract(s);

            Assert.Equal(11, grid.GetLength(0));
            Assert.Equal(3, grid.GetLength(1));
            Assert.Equal(42, grid[10, 2]);
        }

        [Fact]
        public void Extract_PaddedRows_Excluded()
        {
            // Height 5 → PaddedHeight 8. Writing into padded rows 5..7 must
            // not appear in the output, which is [Height, Width].
            var s = new Tier1State(4, 5);
            for (var y = s.Height; y < s.PaddedHeight; y++)
            for (var x = 0; x < s.Width; x++)
            {
                s.SetFlag(x, y, Tier1State.SignificanceFlag);
                s.SetMagnitude(x, y, 999);
            }

            int[,] grid = Tier1CoefficientExtractor.Extract(s);

            Assert.Equal(5, grid.GetLength(0));
            Assert.Equal(4, grid.GetLength(1));
            for (var y = 0; y < 5; y++)
            for (var x = 0; x < 4; x++)
                Assert.Equal(0, grid[y, x]);
        }

        [Fact]
        public void Extract_SignificantPositive_ReturnsMagnitude()
        {
            var s = new Tier1State(4, 4);
            s.SetFlag(1, 2, Tier1State.SignificanceFlag);
            s.SetMagnitude(1, 2, 32); // magnitude with leading bit at bp 5

            int[,] grid = Tier1CoefficientExtractor.Extract(s);
            Assert.Equal(32, grid[2, 1]);
        }

        [Fact]
        public void Extract_SignificantNegative_ReturnsNegatedMagnitude()
        {
            var s = new Tier1State(4, 4);
            s.SetFlag(0, 0, Tier1State.SignificanceFlag);
            s.SetFlag(0, 0, Tier1State.SignFlag);
            s.SetMagnitude(0, 0, 32);

            int[,] grid = Tier1CoefficientExtractor.Extract(s);
            Assert.Equal(-32, grid[0, 0]);
        }

        [Fact]
        public void Extract_SignWithoutSignificance_StaysZero()
        {
            // Pathological case: sign flag without significance flag should
            // never happen during decoding, but the extractor must not
            // surface a negative value if it does — it gates on sig first.
            var s = new Tier1State(4, 4);
            s.SetFlag(2, 2, Tier1State.SignFlag);
            s.SetMagnitude(2, 2, 16);

            int[,] grid = Tier1CoefficientExtractor.Extract(s);
            Assert.Equal(0, grid[2, 2]);
        }

        [Fact]
        public void Extract_AccumulatedBits_ReturnsFullMagnitude()
        {
            // Simulate a coefficient that became significant at bp 5
            // (magnitude bit 32) and then received refinement bits at
            // bp 4 (16) and bp 2 (4) — total accumulated 52.
            var s = new Tier1State(4, 4);
            s.SetFlag(3, 1, Tier1State.SignificanceFlag);
            s.SetMagnitude(3, 1, 32 | 16 | 4);

            int[,] grid = Tier1CoefficientExtractor.Extract(s);
            Assert.Equal(52, grid[1, 3]);
        }

        [Fact]
        public void Extract_MixedPattern_Preserved()
        {
            // 4×4 grid with a sparse pattern of sig/non-sig values, each
            // significant entry independently positive or negative, each
            // with a distinct magnitude. Verifies row/column iteration is
            // independent and every cell read uses the right flags.
            var s = new Tier1State(4, 4);
            // (x, y, magnitude, isNegative)
            (int X, int Y, int Mag, bool Neg)[] entries =
            [
                (0, 0, 64, false),
                (3, 0, 16, true),
                (1, 2, 32 | 8, false),
                (2, 3, 32, true),
            ];
            foreach ((int x, int y, int mag, bool neg) in entries)
            {
                s.SetFlag(x, y, Tier1State.SignificanceFlag);
                if (neg) s.SetFlag(x, y, Tier1State.SignFlag);
                s.SetMagnitude(x, y, mag);
            }

            int[,] grid = Tier1CoefficientExtractor.Extract(s);
            Assert.Equal(64, grid[0, 0]);
            Assert.Equal(-16, grid[0, 3]);
            Assert.Equal(40, grid[2, 1]);
            Assert.Equal(-32, grid[3, 2]);

            // Every other cell must be zero.
            for (var y = 0; y < 4; y++)
            for (var x = 0; x < 4; x++)
            {
                bool isEntry = false;
                foreach ((int ex, int ey, _, _) in entries)
                    if (ex == x && ey == y) { isEntry = true; break; }
                if (!isEntry) Assert.Equal(0, grid[y, x]);
            }
        }

        [Fact]
        public void Extract_ViaDecoder_DelegatesToExtractor()
        {
            // Drive the decoder through one CUP pass that decodes nothing
            // (all RL-skip), then verify ExtractCoefficients agrees with a
            // direct extraction from the same state.
            const int W = 4, H = 4;
            const int FirstBp = 5;

            var encContexts = Jp2MqContextSet.CreateInitialised();
            var enc = new Jp2MqEncoder();
            for (var c = 0; c < W; c++)
                enc.Encode(0, ref encContexts[Jp2MqContextSet.RunLength]);
            enc.Flush();
            byte[] data = enc.ToArray();

            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.LL, FirstBp);
            dec.RunPasses(new Jp2MqDecoder(data, 0, data.Length), passCount: 1);

            int[,] viaDecoder = dec.ExtractCoefficients();
            int[,] viaExtractor = Tier1CoefficientExtractor.Extract(dec.State);

            Assert.Equal(viaExtractor.GetLength(0), viaDecoder.GetLength(0));
            Assert.Equal(viaExtractor.GetLength(1), viaDecoder.GetLength(1));
            for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
                Assert.Equal(viaExtractor[y, x], viaDecoder[y, x]);
        }

        [Fact]
        public void Extract_ViaDecoder_AfterSignificantDecode_PicksUpSignedMagnitude()
        {
            // End-to-end attribution check at the extractor layer: hand-seed
            // a state to a known significant configuration (one positive,
            // one negative), then run the decoder's ExtractCoefficients and
            // confirm the signed magnitudes flow out correctly.
            const int W = 3, H = 2;
            var dec = new Tier1CodeBlockDecoder(W, H, SubbandOrientation.HH, firstBitPlane: 4);

            dec.State.SetFlag(0, 0, Tier1State.SignificanceFlag);
            dec.State.SetMagnitude(0, 0, 16); // +16

            dec.State.SetFlag(2, 1, Tier1State.SignificanceFlag);
            dec.State.SetFlag(2, 1, Tier1State.SignFlag);
            dec.State.SetMagnitude(2, 1, 24); // -24

            int[,] grid = dec.ExtractCoefficients();
            Assert.Equal(16, grid[0, 0]);
            Assert.Equal(-24, grid[1, 2]);
            Assert.Equal(0, grid[0, 1]);
            Assert.Equal(0, grid[0, 2]);
            Assert.Equal(0, grid[1, 0]);
            Assert.Equal(0, grid[1, 1]);
        }
    }
}
