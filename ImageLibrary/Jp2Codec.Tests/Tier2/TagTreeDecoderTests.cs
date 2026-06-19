using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class TagTreeDecoderTests
    {
        /// <summary>
        /// Encoder mirror of <see cref="TagTreeDecoder"/>. Given a 2-D array of
        /// values, emits a bit stream that the decoder reproduces — used as
        /// the test oracle: build random values, encode, decode, assert match.
        /// </summary>
        private sealed class TagTreeEncoder
        {
            private readonly int[,] _values;
            private readonly int[][,] _low;
            private readonly bool[][,] _known;
            private readonly int _levels;

            public TagTreeEncoder(int[,] values)
            {
                _values = values;
                int w = values.GetLength(1);
                int h = values.GetLength(0);
                var lows = new List<int[,]>();
                var knowns = new List<bool[,]>();
                int curW = w, curH = h;
                lows.Add(new int[curH, curW]);
                knowns.Add(new bool[curH, curW]);
                while (curW > 1 || curH > 1)
                {
                    curW = (curW + 1) / 2;
                    curH = (curH + 1) / 2;
                    lows.Add(new int[curH, curW]);
                    knowns.Add(new bool[curH, curW]);
                }
                _levels = lows.Count;
                _low = lows.ToArray();
                _known = knowns.ToArray();
            }

            // Compute the minimum value of leaves that descend from internal node (x, y) at the given level.
            private int MinUnder(int level, int x, int y)
            {
                int leafLeft = x << level;
                int leafTop = y << level;
                int leafRight = Math.Min(leafLeft + (1 << level), _values.GetLength(1));
                int leafBottom = Math.Min(leafTop + (1 << level), _values.GetLength(0));
                var min = int.MaxValue;
                for (int iy = leafTop; iy < leafBottom; iy++)
                    for (int ix = leafLeft; ix < leafRight; ix++)
                        if (_values[iy, ix] < min) min = _values[iy, ix];
                return min;
            }

            public List<int> Encode(int x, int y, int threshold)
            {
                var bits = new List<int>();
                var parentLow = 0;
                for (int level = _levels - 1; level >= 0; level--)
                {
                    int xi = x >> level;
                    int yi = y >> level;
                    int low = Math.Max(_low[level][yi, xi], parentLow);
                    bool known = _known[level][yi, xi];
                    int realValue = MinUnder(level, xi, yi);

                    while (low < threshold && !known)
                    {
                        if (low == realValue)
                        {
                            bits.Add(1);
                            known = true;
                        }
                        else
                        {
                            bits.Add(0);
                            low++;
                        }
                    }
                    _low[level][yi, xi] = low;
                    _known[level][yi, xi] = known;
                    parentLow = low;
                }
                return bits;
            }
        }

        private static byte[] BitsToBytes(List<int> bits)
        {
            int totalBytes = (bits.Count + 7) / 8;
            var buf = new byte[totalBytes];
            for (var i = 0; i < bits.Count; i++)
            {
                int bytePos = i >> 3;
                int bitPos = 7 - (i & 7);
                if (bits[i] != 0) buf[bytePos] |= (byte)(1 << bitPos);
            }
            return buf;
        }

        [Fact]
        public void Decode_1x1TreeValueZero_ImmediatelyKnown()
        {
            // Smallest case: a single-node tree with value 0. Encoder writes '1' (already at value).
            // Decoder at threshold 1 reads 1 bit, sees '1' → value known to be 0; returns true (0 < 1).
            byte[] bits = { 0b10000000 };
            var r = new PacketHeaderBitReader(bits, 0, 1);
            var d = new TagTreeDecoder(1, 1);
            Assert.True(d.DecodeLessThan(0, 0, threshold: 1, r));
        }

        [Fact]
        public void Decode_1x1TreeValueAtLeastOne_FirstZeroEndsThresholdOneQuery()
        {
            // Encoder for value=3 at threshold=1 writes '0' (low=0→1; loop exits).
            byte[] bits = { 0b00000000 };
            var r = new PacketHeaderBitReader(bits, 0, 1);
            var d = new TagTreeDecoder(1, 1);
            Assert.False(d.DecodeLessThan(0, 0, threshold: 1, r));
        }

        [Theory]
        [InlineData(0, 5)]
        [InlineData(1, 5)]
        [InlineData(3, 5)]
        [InlineData(7, 10)]
        [InlineData(15, 20)]
        public void RoundTrip_1x1Tree_ValueRecoveredAtThresholdAboveValue(int leafValue, int threshold)
        {
            var enc = new TagTreeEncoder(new[,] { { leafValue } });
            byte[] data = BitsToBytes(enc.Encode(0, 0, threshold));
            var r = new PacketHeaderBitReader(data, 0, data.Length);
            var d = new TagTreeDecoder(1, 1);

            bool below = d.DecodeLessThan(0, 0, threshold, r);
            Assert.Equal(leafValue < threshold, below);
        }

        [Fact]
        public void RoundTrip_2x2Tree_AllFourLeavesDecodeCorrectly()
        {
            int[,] values = { { 1, 4 }, { 2, 0 } };
            var enc = new TagTreeEncoder(values);
            // Encode each leaf at threshold 5 (larger than any value, so all should report true).
            var allBits = new List<int>();
            allBits.AddRange(enc.Encode(0, 0, 5));
            allBits.AddRange(enc.Encode(1, 0, 5));
            allBits.AddRange(enc.Encode(0, 1, 5));
            allBits.AddRange(enc.Encode(1, 1, 5));
            byte[] data = BitsToBytes(allBits);

            var r = new PacketHeaderBitReader(data, 0, data.Length);
            var d = new TagTreeDecoder(2, 2);

            Assert.True(d.DecodeLessThan(0, 0, 5, r));
            Assert.True(d.DecodeLessThan(1, 0, 5, r));
            Assert.True(d.DecodeLessThan(0, 1, 5, r));
            Assert.True(d.DecodeLessThan(1, 1, 5, r));
        }

        [Fact]
        public void RoundTrip_PreservesStateAcrossProgressiveThresholds()
        {
            // Same leaf queried at increasing thresholds: the decoder should
            // reuse what it learned at lower thresholds and only consume the
            // incremental bits the encoder added.
            int[,] values = { { 4 } };
            var enc = new TagTreeEncoder(values);
            var allBits = new List<int>();
            allBits.AddRange(enc.Encode(0, 0, 1));   // value=4 ≥ 1 → '0'
            allBits.AddRange(enc.Encode(0, 0, 3));   // climb to 3, still no known → '00'
            allBits.AddRange(enc.Encode(0, 0, 6));   // climb to 4, mark known → '001'
            byte[] data = BitsToBytes(allBits);

            var r = new PacketHeaderBitReader(data, 0, data.Length);
            var d = new TagTreeDecoder(1, 1);
            Assert.False(d.DecodeLessThan(0, 0, 1, r));
            Assert.False(d.DecodeLessThan(0, 0, 3, r));
            Assert.True(d.DecodeLessThan(0, 0, 6, r));
        }

        [Fact]
        public void RoundTrip_4x4Tree_RandomValuesDecodeCorrectly()
        {
            int[,] values =
            {
                { 0, 3, 1, 2 },
                { 5, 0, 4, 6 },
                { 2, 1, 0, 7 },
                { 3, 4, 5, 8 },
            };
            var enc = new TagTreeEncoder(values);
            var allBits = new List<int>();
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    allBits.AddRange(enc.Encode(x, y, 9));
            byte[] data = BitsToBytes(allBits);

            var r = new PacketHeaderBitReader(data, 0, data.Length);
            var d = new TagTreeDecoder(4, 4);
            for (var y = 0; y < 4; y++)
                for (var x = 0; x < 4; x++)
                    Assert.True(d.DecodeLessThan(x, y, 9, r),
                        $"leaf ({x}, {y}) value {values[y, x]} should report < 9");
        }

        [Fact]
        public void DecodeValue_RecoversExactLeafValue_OnFreshTree()
        {
            int[,] values = { { 7 } };
            var enc = new TagTreeEncoder(values);
            // To recover an exact value of 7, the encoder needs threshold = 8.
            byte[] data = BitsToBytes(enc.Encode(0, 0, 8));

            var r = new PacketHeaderBitReader(data, 0, data.Length);
            var d = new TagTreeDecoder(1, 1);
            int v = d.DecodeValue(0, 0, r);
            Assert.Equal(7, v);
        }

        [Fact]
        public void DecodeLessThan_BadCoordinates_Throws()
        {
            var d = new TagTreeDecoder(4, 4);
            var r = new PacketHeaderBitReader(new byte[] { 0 }, 0, 1);
            Assert.Throws<ArgumentOutOfRangeException>(() => d.DecodeLessThan(-1, 0, 1, r));
            Assert.Throws<ArgumentOutOfRangeException>(() => d.DecodeLessThan(0, 4, 1, r));
        }
    }
}
