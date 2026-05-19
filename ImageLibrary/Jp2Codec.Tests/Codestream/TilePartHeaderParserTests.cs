using Jp2Codec.Codestream;

namespace Jp2Codec.Tests.Codestream
{
    public sealed class TilePartHeaderParserTests
    {
        [Fact]
        public void Parse_BareSotAndSod_ReturnsTilePartWithNoOverrides()
        {
            byte[] data = new HeaderBytes()
                .Sot(tileIndex: 0, psot: 100, tpsot: 0, tnsot: 1)
                .Marker(0xFF93)                                 // SOD (no length)
                .Bytes(0xAB, 0xCD)                              // packet body bytes (not consumed by header parser)
                .ToArray();

            var r = new CodestreamReader(data);
            TilePartHeader header = TilePartHeaderParser.Parse(r, numberOfComponents: 1);

            Assert.Equal(0, header.Sot.TileIndex);
            Assert.Equal(100u, header.Sot.TilePartLength);
            Assert.Null(header.CodOverride);
            Assert.Null(header.QcdOverride);
            Assert.Empty(header.Comments);
            Assert.Equal(header.PacketBodyStartPosition, r.Position);
            // The next byte is the first body byte we wrote.
            Assert.Equal(0xAB, r.ReadByte());
        }

        [Fact]
        public void Parse_TilePartWithCodOverride_PopulatesCodOverride()
        {
            byte[] data = new HeaderBytes()
                .Sot(tileIndex: 0, psot: 0, tpsot: 0, tnsot: 1)
                .Cod(decompositionLevels: 1)
                .Marker(0xFF93)
                .ToArray();

            var r = new CodestreamReader(data);
            TilePartHeader header = TilePartHeaderParser.Parse(r, numberOfComponents: 1);
            Assert.NotNull(header.CodOverride);
            Assert.Equal(1, header.CodOverride!.DecompositionLevels);
        }

        [Fact]
        public void Parse_TilePartMissingSod_Throws()
        {
            byte[] data = new HeaderBytes()
                .Sot()
                .Cod()
                .ToArray();
            var r = new CodestreamReader(data);
            Assert.Throws<InvalidDataException>(() =>
                TilePartHeaderParser.Parse(r, numberOfComponents: 1));
        }

        [Fact]
        public void Parse_TilePartStartsWithoutSot_Throws()
        {
            byte[] data = new HeaderBytes()
                .Cod()
                .Marker(0xFF93)
                .ToArray();
            var r = new CodestreamReader(data);
            Assert.Throws<InvalidDataException>(() =>
                TilePartHeaderParser.Parse(r, numberOfComponents: 1));
        }
    }
}
