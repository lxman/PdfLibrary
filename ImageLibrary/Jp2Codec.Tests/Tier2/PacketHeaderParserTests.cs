using Jp2Codec.Codestream.Segments;
using Jp2Codec.Tier2;

namespace Jp2Codec.Tests.Tier2
{
    public sealed class PacketHeaderParserTests
    {
        // -----------------------------------------------------------------
        // Empty packet (zero-length flag == 0)
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_ZeroFlag_ReturnsEmptyAndLeavesStateUnchanged()
        {
            var subband = new PrecinctSubband(2, 2);
            var precinct = new Precinct(new[] { subband });

            // Single '0' bit followed by 7 zero padding bits.
            byte[] data = { 0x00 };
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 0, reader, CodeBlockStyle.None);

            Assert.True(header.IsEmpty);
            Assert.Empty(header.Contributions);
            for (var y = 0; y < 2; y++)
                for (var x = 0; x < 2; x++)
                {
                    Assert.False(subband.CodeBlocks[y, x].Included);
                    Assert.Equal(3, subband.CodeBlocks[y, x].Lblock);
                    Assert.Equal(0, subband.CodeBlocks[y, x].CompletedPasses);
                }
        }

        // -----------------------------------------------------------------
        // Block not first-included at this layer
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_BlockNotIncludedYet_LeavesBlockStateUnchanged()
        {
            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });

            // Bits: 1 (non-empty) + 0 (inclusion tag tree '0' for leaf >= 1 at threshold 1)
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);
            w.WriteBit(0);
            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 0, reader, CodeBlockStyle.None);

            Assert.False(header.IsEmpty);
            Assert.Empty(header.Contributions);
            Assert.False(subband.CodeBlocks[0, 0].Included);
            Assert.Equal(3, subband.CodeBlocks[0, 0].Lblock);
        }

        // -----------------------------------------------------------------
        // First inclusion
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_FirstInclusion_ReportsContributionAndUpdatesState()
        {
            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });

            // Build header bits for: first inclusion at layer 0, zero-bp = 2,
            // 1 new pass, no Lblock increment, body length 5.
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);              // non-empty
            w.WriteBit(1);              // inclusion tag tree: '1' (value 0 at threshold 1)
            // zero-bp tag tree, value = 2: emits '0', '0', '1' across thresholds 1..3
            w.WriteBit(0);
            w.WriteBit(0);
            w.WriteBit(1);
            w.WriteBit(0);              // new passes: '0' = 1
            w.WriteBit(0);              // Lblock increment: '0' = 0
            w.WriteBits(5, 3);          // body length = 5 in Lblock(3) + log2(1)=0 bits

            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 0, reader, CodeBlockStyle.None);

            Assert.False(header.IsEmpty);
            Assert.Single(header.Contributions);
            CodeBlockContribution c = header.Contributions[0];
            Assert.Equal(0, c.SubbandIndex);
            Assert.Equal(0, c.X);
            Assert.Equal(0, c.Y);
            Assert.True(c.IsFirstInclusion);
            Assert.Equal(2, c.ZeroBitPlanesIfFirst);
            Assert.Equal(1, c.TotalNewCodingPasses);
            Assert.Equal(5, c.TotalBodyLength);

            CodeBlockState s = subband.CodeBlocks[0, 0];
            Assert.True(s.Included);
            Assert.Equal(2, s.ZeroBitPlanes);
            Assert.Equal(3, s.Lblock);
            Assert.Equal(1, s.CompletedPasses);
        }

        // -----------------------------------------------------------------
        // Subsequent inclusion uses single bit, not the tag trees
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_SubsequentInclusion_UsesInclusionBitAndPreservesZeroBitPlanes()
        {
            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });
            // Mark block as already included with prior state.
            subband.CodeBlocks[0, 0].Included = true;
            subband.CodeBlocks[0, 0].ZeroBitPlanes = 4;

            // Bits: 1 (non-empty) + 1 (inclusion bit) + '10' (2 passes)
            // + '0' (Lblock inc = 0) + body length 10 in Lblock(3)+log2(2)=4 bits
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);              // non-empty
            w.WriteBit(1);              // contributes this packet
            w.WriteBit(1); w.WriteBit(0); // 2 passes
            w.WriteBit(0);              // Lblock inc = 0
            w.WriteBits(10, 4);         // body length = 10

            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 1, reader, CodeBlockStyle.None);

            Assert.False(header.IsEmpty);
            Assert.Single(header.Contributions);
            CodeBlockContribution c = header.Contributions[0];
            Assert.False(c.IsFirstInclusion);
            Assert.Equal(0, c.ZeroBitPlanesIfFirst);     // not reported on non-first contributions
            Assert.Equal(2, c.TotalNewCodingPasses);
            Assert.Equal(10, c.TotalBodyLength);

            // Stored state intact / advanced.
            CodeBlockState s = subband.CodeBlocks[0, 0];
            Assert.Equal(4, s.ZeroBitPlanes);
            Assert.Equal(3, s.Lblock);
            Assert.Equal(2, s.CompletedPasses);
        }

        // -----------------------------------------------------------------
        // Lblock increment is applied and persists across packets
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_LblockIncrement_PersistsAcrossPackets()
        {
            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });
            subband.CodeBlocks[0, 0].Included = true;
            subband.CodeBlocks[0, 0].ZeroBitPlanes = 0;

            // Packet 1: contributes, Lblock += 2, 1 new pass, body length 7.
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);              // non-empty
            w.WriteBit(1);              // inclusion bit
            w.WriteBit(0);              // 1 new pass
            w.WriteBit(1); w.WriteBit(1); w.WriteBit(0); // Lblock inc = 2 (codeword '110')
            // After increment, Lblock = 3 + 2 = 5. log2(1) = 0. Read 5 bits = body length.
            w.WriteBits(7, 5);
            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 1, reader, CodeBlockStyle.None);

            Assert.Single(header.Contributions);
            Assert.Equal(7, header.Contributions[0].TotalBodyLength);
            Assert.Equal(5, subband.CodeBlocks[0, 0].Lblock);
        }

        // -----------------------------------------------------------------
        // Multi-subband walk (resolution > 0 has 3 subbands)
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_ThreeSubbands_WalksThemInOrder()
        {
            var hl = new PrecinctSubband(1, 1);
            var lh = new PrecinctSubband(1, 1);
            var hh = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { hl, lh, hh });

            // For each subband, write a "not included this packet" inclusion
            // tag tree bit ('0'), making the precinct yield zero contributions
            // but still walking all three subbands.
            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);   // non-empty
            w.WriteBit(0);   // subband 0 (HL) tag tree '0'
            w.WriteBit(0);   // subband 1 (LH) tag tree '0'
            w.WriteBit(0);   // subband 2 (HH) tag tree '0'

            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 0, reader, CodeBlockStyle.None);

            Assert.False(header.IsEmpty);
            Assert.Empty(header.Contributions);
        }

        // -----------------------------------------------------------------
        // 2 × 2 grid: mixed first / subsequent inclusion across blocks
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_TwoByTwoGrid_FirstInclusionAtVaryingLayers()
        {
            // 2×2 subband. Conceptual inclusion-layer table for the leaves
            // (encoder's view, indexed [y, x]):
            //   y=0: [0, 0]
            //   y=1: [1, 1]
            // ⇒ blocks (0,0) and (1,0) first-include at layer 0;
            //   blocks (0,1) and (1,1) defer to layer 1.
            //
            // Parser visit order is (y, x) → (0,0), (1,0), (0,1), (1,1).
            // For threshold 1 the inclusion tag tree emits bits like so:
            //   block (0,0): root '1' (real_min=0), leaf '1' (real=0). Two bits.
            //   block (1,0): root already known; leaf '1'. One bit.
            //   block (0,1): root known; leaf '0' (real=1, low→1, exit). One bit.
            //   block (1,1): same as (0,1). One bit.
            //
            // Zero-bp tag tree (separate tree, leaves chosen as all-zeros so
            // the min-under-root is 0):
            //   block (0,0): root '1' + leaf '1'. Two bits.
            //   block (1,0): root already known; leaf '1'. One bit.

            var subband = new PrecinctSubband(2, 2);
            var precinct = new Precinct(new[] { subband });

            var w = new PacketHeaderBitWriter();
            w.WriteBit(1);                          // non-empty packet

            // ---- Block (0, 0): first inclusion, zero-bp = 0, 1 pass, body 6
            w.WriteBit(1); w.WriteBit(1);           // inclusion tag tree (root + leaf)
            w.WriteBit(1); w.WriteBit(1);           // zero-bp tag tree (root + leaf)
            w.WriteBit(0);                          // 1 new pass
            w.WriteBit(0);                          // Lblock increment = 0
            w.WriteBits(6, 3);                      // body length 6 in Lblock(3) bits

            // ---- Block (1, 0): first inclusion, root nodes already known
            w.WriteBit(1);                          // inclusion tag tree (leaf only)
            w.WriteBit(1);                          // zero-bp tag tree (leaf only)
            w.WriteBit(0);                          // 1 new pass
            w.WriteBit(0);                          // Lblock increment = 0
            w.WriteBits(6, 3);

            // ---- Block (0, 1): not first-included this packet
            w.WriteBit(0);

            // ---- Block (1, 1): not first-included this packet
            w.WriteBit(0);

            byte[] data = w.ToBytes();
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeader header = PacketHeaderParser.Parse(precinct, layerIndex: 0, reader, CodeBlockStyle.None);

            Assert.False(header.IsEmpty);
            Assert.Equal(2, header.Contributions.Count);

            CodeBlockContribution c0 = header.Contributions[0];
            Assert.Equal(0, c0.X);
            Assert.Equal(0, c0.Y);
            Assert.True(c0.IsFirstInclusion);
            Assert.Equal(6, c0.TotalBodyLength);

            CodeBlockContribution c1 = header.Contributions[1];
            Assert.Equal(1, c1.X);
            Assert.Equal(0, c1.Y);
            Assert.True(c1.IsFirstInclusion);
            Assert.Equal(6, c1.TotalBodyLength);

            // CodeBlocks is indexed [y, x] — assert only the first-row entries
            // got marked included.
            Assert.True(subband.CodeBlocks[0, 0].Included);
            Assert.True(subband.CodeBlocks[0, 1].Included);
            Assert.False(subband.CodeBlocks[1, 0].Included);
            Assert.False(subband.CodeBlocks[1, 1].Included);
        }

        // -----------------------------------------------------------------
        // Multi-layer state retention: inclusion tag tree progresses
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_TwoLayerScenario_InclusionTreeStateCarriesAcrossPackets()
        {
            // Block first-included at layer 1. At layer 0, tag tree query at
            // threshold 1 emits '0' (low climbs to 1). At layer 1, query at
            // threshold 2 with saved low=1 sees real=1, emits '1' (known).

            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });

            // Packet at layer 0: non-empty + '0' tag tree bit.
            var w0 = new PacketHeaderBitWriter();
            w0.WriteBit(1);
            w0.WriteBit(0);
            byte[] data0 = w0.ToBytes();
            var r0 = new PacketHeaderBitReader(data0, 0, data0.Length);
            PacketHeader h0 = PacketHeaderParser.Parse(precinct, 0, r0, CodeBlockStyle.None);
            Assert.False(h0.IsEmpty);
            Assert.Empty(h0.Contributions);
            Assert.False(subband.CodeBlocks[0, 0].Included);

            // Packet at layer 1: non-empty + '1' (first inclusion) +
            // zero-bp = 0 ('1') + 1 pass ('0') + Lblock inc 0 ('0') + body 4 in 3 bits.
            var w1 = new PacketHeaderBitWriter();
            w1.WriteBit(1);
            w1.WriteBit(1);
            w1.WriteBit(1);                  // zero-bp = 0
            w1.WriteBit(0);                  // 1 pass
            w1.WriteBit(0);                  // Lblock inc = 0
            w1.WriteBits(4, 3);              // body = 4
            byte[] data1 = w1.ToBytes();
            var r1 = new PacketHeaderBitReader(data1, 0, data1.Length);
            PacketHeader h1 = PacketHeaderParser.Parse(precinct, 1, r1, CodeBlockStyle.None);
            Assert.False(h1.IsEmpty);
            Assert.Single(h1.Contributions);

            CodeBlockContribution c = h1.Contributions[0];
            Assert.True(c.IsFirstInclusion);
            Assert.Equal(0, c.ZeroBitPlanesIfFirst);
            Assert.Equal(4, c.TotalBodyLength);
            Assert.True(subband.CodeBlocks[0, 0].Included);
        }

        // -----------------------------------------------------------------
        // Reader is byte-aligned after parse (next byte is packet body)
        // -----------------------------------------------------------------

        [Fact]
        public void Parse_LeavesReaderAtNextByteBoundary()
        {
            var subband = new PrecinctSubband(1, 1);
            var precinct = new Precinct(new[] { subband });
            byte[] data = { 0x00, 0xAB };   // empty packet flag + a "body" byte
            var reader = new PacketHeaderBitReader(data, 0, data.Length);

            PacketHeaderParser.Parse(precinct, 0, reader, CodeBlockStyle.None);

            // The next bit read should come from 0xAB — we don't expose byte
            // position directly, but ReadBit() returning 1 (MSB of 0xAB = 1)
            // confirms we're aligned to it.
            Assert.Equal(1, reader.ReadBit());      // 0xAB = 1010_1011 → top bit 1
            Assert.Equal(0, reader.ReadBit());
            Assert.Equal(1, reader.ReadBit());
            Assert.Equal(0, reader.ReadBit());
        }
    }
}
