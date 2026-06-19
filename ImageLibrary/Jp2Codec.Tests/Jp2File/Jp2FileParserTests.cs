using Jp2Codec.Jp2File;

namespace Jp2Codec.Tests.Jp2File
{
    public sealed class Jp2FileParserTests
    {
        private static byte[] MinimalCodestream()
        {
            // Just an SOC + something to make it non-empty; the file parser
            // does not validate the codestream contents.
            return new byte[] { 0xFF, 0x4F, 0x00, 0x00 };
        }

        [Fact]
        public void Parse_RawJ2kCodestream_ReturnsRawInfo()
        {
            byte[] codestream = MinimalCodestream();
            Jp2FileInfo info = Jp2FileParser.Parse(codestream);

            Assert.False(info.IsJp2File);
            Assert.Equal(0, info.CodestreamOffset);
            Assert.Equal(codestream.Length, info.CodestreamLength);
            Assert.Equal(Jp2ColorSpace.Unspecified, info.ColorSpace);
        }

        [Fact]
        public void Parse_MinimalJp2FileWithSrgb_ReturnsCodestreamRange()
        {
            byte[] cs = MinimalCodestream();
            byte[] file = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2()
                .Jp2HeaderForSrgb(width: 16, height: 8, components: 3)
                .Jp2cBoxWithCodestream(cs)
                .ToArray();

            Jp2FileInfo info = Jp2FileParser.Parse(file);
            Assert.True(info.IsJp2File);
            Assert.Equal(16, info.Width);
            Assert.Equal(8, info.Height);
            Assert.Equal(3, info.NumberOfComponents);
            Assert.Equal(new[] { 8 }, info.BitsPerComponent);
            Assert.Equal(new[] { false }, info.ComponentSigned);
            Assert.Equal(Jp2ColorSpace.Srgb, info.ColorSpace);

            // Codestream byte range should round-trip the original bytes.
            var sliced = new byte[info.CodestreamLength];
            Buffer.BlockCopy(file, info.CodestreamOffset, sliced, 0, sliced.Length);
            Assert.Equal(cs, sliced);
        }

        [Fact]
        public void Parse_NotAJp2FileAndNotJ2k_Throws()
        {
            byte[] bytes = { 0x00, 0x01, 0x02, 0x03 };
            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(bytes));
        }

        [Fact]
        public void Parse_IndexedJp2WithPaletteAndCmap_ReturnsPaletteAndMapping()
        {
            byte[] cs = MinimalCodestream();
            var paletteRgb = new byte[4, 3]
            {
                {   0,   0,   0 },  // index 0 → black
                { 255,   0,   0 },  // index 1 → red
                {   0, 255,   0 },  // index 2 → green
                {   0,   0, 255 },  // index 3 → blue
            };
            byte[] file = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2()
                .Jp2HeaderForIndexedRgb(width: 4, height: 2, paletteRgb)
                .Jp2cBoxWithCodestream(cs)
                .ToArray();

            Jp2FileInfo info = Jp2FileParser.Parse(file);
            Assert.True(info.IsJp2File);
            Assert.NotNull(info.Palette);
            Assert.NotNull(info.ComponentMapping);
            Assert.Equal(4, info.Palette!.NumEntries);
            Assert.Equal(3, info.Palette.NumColumns);
            Assert.Equal(new[] { 8, 8, 8 }, info.Palette.BitDepths);
            Assert.Equal(new[] { false, false, false }, info.Palette.Signed);
            Assert.Equal(255, info.Palette.Entries[1, 0]);
            Assert.Equal(255, info.Palette.Entries[2, 1]);
            Assert.Equal(255, info.Palette.Entries[3, 2]);

            Assert.Equal(3, info.ComponentMapping!.NumChannels);
            Assert.Equal(new[] { 0, 0, 0 }, info.ComponentMapping.ComponentIndex);
            Assert.Equal(new byte[] { 1, 1, 1 }, info.ComponentMapping.MappingType);
            Assert.Equal(new byte[] { 0, 1, 2 }, info.ComponentMapping.PaletteColumn);
        }

        [Fact]
        public void Parse_PaletteWithSignedColumn_SignExtendsEntries()
        {
            // Build a pclr with one column at 8-bit SIGNED (high bit set) and
            // entries that span the negative range. Confirm sign-extension
            // through to the int entry table.
            Jp2Bytes b = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2();

            // jp2h
            uint Jp2h = 0x6A703268;
            uint Ihdr = 0x69686472;
            uint Colr = 0x636F6C72;
            uint Pclr = 0x70636C72;
            uint Cmap = 0x636D6170;

            int superAt = b.BeginBox(Jp2h);
            int ihdrAt = b.BeginBox(Ihdr);
            b.U32(1).U32(1).U16(1).U8(7).U8(0x07).U8(0).U8(0);
            b.EndBox(ihdrAt);

            int colrAt = b.BeginBox(Colr);
            b.U8(1).U8(0).U8(0).U32(17);  // greyscale
            b.EndBox(colrAt);

            int pclrAt = b.BeginBox(Pclr);
            b.U16(3);                 // NE = 3
            b.U8(1);                  // NPC = 1
            b.U8(0x87);               // signed, depth 8 (high bit set, low 7 = 7 → bd=8)
            b.U8(0x00).U8(0x7F).U8(0xFF); // entries 0=0, 1=127, 2=-1
            b.EndBox(pclrAt);

            int cmapAt = b.BeginBox(Cmap);
            b.U16(0).U8(1).U8(0);
            b.EndBox(cmapAt);

            b.EndBox(superAt);

            b.Jp2cBoxWithCodestream(MinimalCodestream());

            Jp2FileInfo info = Jp2FileParser.Parse(b.ToArray());
            Assert.NotNull(info.Palette);
            Assert.Equal(new[] { 8 }, info.Palette!.BitDepths);
            Assert.Equal(new[] { true }, info.Palette.Signed);
            Assert.Equal(0, info.Palette.Entries[0, 0]);
            Assert.Equal(127, info.Palette.Entries[1, 0]);
            Assert.Equal(-1, info.Palette.Entries[2, 0]);
        }

        [Fact]
        public void Parse_PaletteWithoutCmap_Throws()
        {
            // Build a jp2h with pclr but no cmap; per ISO 15444-1 the two must
            // appear together.
            Jp2Bytes b = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2();

            int superAt = b.BeginBox(0x6A703268);
            int ihdrAt = b.BeginBox(0x69686472);
            b.U32(1).U32(1).U16(1).U8(7).U8(0x07).U8(0).U8(0);
            b.EndBox(ihdrAt);

            int colrAt = b.BeginBox(0x636F6C72);
            b.U8(1).U8(0).U8(0).U32(17);
            b.EndBox(colrAt);

            int pclrAt = b.BeginBox(0x70636C72);
            b.U16(1).U8(1).U8(7).U8(0xFF);
            b.EndBox(pclrAt);

            b.EndBox(superAt);
            b.Jp2cBoxWithCodestream(MinimalCodestream());

            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(b.ToArray()));
        }

        [Fact]
        public void Parse_Jp2WithoutFtyp_Throws()
        {
            byte[] file = new Jp2Bytes()
                .SignatureBox()
                .Jp2HeaderForSrgb(8, 8, 1)
                .Jp2cBoxWithCodestream(MinimalCodestream())
                .ToArray();
            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(file));
        }

        [Fact]
        public void Parse_Jp2WithoutJp2c_Throws()
        {
            byte[] file = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2()
                .Jp2HeaderForSrgb(8, 8, 1)
                .ToArray();
            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(file));
        }

        [Fact]
        public void Parse_Jp2WithoutJp2h_Throws()
        {
            byte[] file = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2()
                .Jp2cBoxWithCodestream(MinimalCodestream())
                .ToArray();
            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(file));
        }

        [Fact]
        public void Parse_FtypMissingJp2Brand_Throws()
        {
            // Build an ftyp listing a non-jp2 brand only.
            Jp2Bytes b = new Jp2Bytes().SignatureBox();
            int at = b.BeginBox(0x66747970);
            b.U32(0x6A707820); // 'jpx ' major
            b.U32(0);
            b.U32(0x6A707820); // 'jpx ' compatibility
            b.EndBox(at);
            byte[] file = b
                .Jp2HeaderForSrgb(8, 8, 1)
                .Jp2cBoxWithCodestream(MinimalCodestream())
                .ToArray();
            Assert.Throws<InvalidDataException>(() => Jp2FileParser.Parse(file));
        }

        [Fact]
        public void Parse_Jp2WithMixedBitDepthsViaBpcc_ReturnsPerComponentDepths()
        {
            // Hand-build a jp2h with ihdr.BPC=0xFF followed by a bpcc box.
            Jp2Bytes b = new Jp2Bytes()
                .SignatureBox()
                .FtypBoxWithJp2();
            int jp2hAt = b.BeginBox(0x6A703268);

            int ihdrAt = b.BeginBox(0x69686472);
            b.U32(4).U32(4).U16(3);   // 4x4, 3 components
            b.U8(0xFF);               // BPC = 0xFF → bpcc follows
            b.U8(0x07).U8(0).U8(0);
            b.EndBox(ihdrAt);

            int bpccAt = b.BeginBox(0x62706363);
            b.U8(7);    // component 0 = 8-bit unsigned
            b.U8(0x8B); // component 1 = 12-bit signed (0x80 | 0x0B)
            b.U8(11);   // component 2 = 12-bit unsigned
            b.EndBox(bpccAt);

            int colrAt = b.BeginBox(0x636F6C72);
            b.U8(1).U8(0).U8(0).U32(17); // METH=1, EnumCS=17 (greyscale)
            b.EndBox(colrAt);

            b.EndBox(jp2hAt);
            byte[] file = b.Jp2cBoxWithCodestream(MinimalCodestream()).ToArray();

            Jp2FileInfo info = Jp2FileParser.Parse(file);
            Assert.Equal(3, info.NumberOfComponents);
            Assert.Equal(new[] { 8, 12, 12 }, info.BitsPerComponent);
            Assert.Equal(new[] { false, true, false }, info.ComponentSigned);
            Assert.Equal(Jp2ColorSpace.Greyscale, info.ColorSpace);
        }
    }
}
