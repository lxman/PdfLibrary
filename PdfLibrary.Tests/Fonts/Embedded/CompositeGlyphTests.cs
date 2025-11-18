using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf;

namespace PdfLibrary.Tests.Fonts.Embedded;

/// <summary>
/// Tests for composite glyph parsing and resolution
/// </summary>
public class CompositeGlyphTests
{
    [Fact]
    public void CompositeGlyph_ParsesMultipleComponents()
    {
        // Create mock data for a composite glyph with 2 components
        // Component 1: flags=0x0027 (MORE_COMPONENTS | ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES)
        // Component 2: flags=0x0007 (ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES)
        byte[] headerData = new byte[]
        {
            0xFF, 0xFF,  // numberOfContours: -1 (composite)
            0x00, 0x00,  // xMin: 0
            0x00, 0x00,  // yMin: 0
            0x00, 0x64,  // xMax: 100
            0x00, 0x64   // yMax: 100
        };

        byte[] glyphData = new byte[]
        {
            // Component 1
            0x00, 0x27,  // flags: MORE_COMPONENTS | ARG_1_AND_2_ARE_WORDS | ARGS_ARE_XY_VALUES
            0x00, 0x42,  // glyphIndex: 66
            0x01, 0x00,  // arg1: 256
            0x02, 0x00,  // arg2: 512

            // Component 2
            0x00, 0x01,  // flags: ARG_1_AND_2_ARE_WORDS (no MORE_COMPONENTS)
            0x00, 0x85,  // glyphIndex: 133
            0x00, 0x64,  // arg1: 100
            0x00, 0xC8   // arg2: 200
        };

        var reader = new BigEndianReader(glyphData);
        var header = new GlyphHeader(headerData); // Composite glyph (negative contours)
        var composite = new CompositeGlyph(reader, header);

        Assert.Equal(2, composite.Components.Count);

        // Verify component 1
        Assert.Equal(66, composite.Components[0].GlyphIndex);
        Assert.Equal(256, composite.Components[0].Argument1);
        Assert.Equal(512, composite.Components[0].Argument2);
        Assert.Equal(1.0f, composite.Components[0].A); // Identity transform
        Assert.Equal(0.0f, composite.Components[0].B);
        Assert.Equal(0.0f, composite.Components[0].C);
        Assert.Equal(1.0f, composite.Components[0].D);

        // Verify component 2
        Assert.Equal(133, composite.Components[1].GlyphIndex);
        Assert.Equal(100, composite.Components[1].Argument1);
        Assert.Equal(200, composite.Components[1].Argument2);
    }

    [Fact]
    public void CompositeGlyph_ParsesScaleTransform()
    {
        // Component with uniform scale: flags=0x0009 (WE_HAVE_A_SCALE | ARG_1_AND_2_ARE_WORDS)
        byte[] headerData = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x64 };
        byte[] glyphData = new byte[]
        {
            0x00, 0x09,  // flags: WE_HAVE_A_SCALE | ARG_1_AND_2_ARE_WORDS
            0x00, 0x42,  // glyphIndex: 66
            0x01, 0x00,  // arg1: 256
            0x02, 0x00,  // arg2: 512
            0x60, 0x00   // scale: 1.5 in F2DOT14 format (24576/16384 = 1.5)
        };

        var reader = new BigEndianReader(glyphData);
        var header = new GlyphHeader(headerData);
        var composite = new CompositeGlyph(reader, header);

        Assert.Single(composite.Components);
        Assert.Equal(1.5f, composite.Components[0].A, precision: 3);
        Assert.Equal(1.5f, composite.Components[0].D, precision: 3);
        Assert.Equal(0.0f, composite.Components[0].B);
        Assert.Equal(0.0f, composite.Components[0].C);
    }

    [Fact]
    public void CompositeGlyph_ParsesXYScaleTransform()
    {
        // Component with X/Y scale: flags=0x0041 (WE_HAVE_AN_X_AND_Y_SCALE | ARG_1_AND_2_ARE_WORDS)
        byte[] headerData = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x64 };
        byte[] glyphData = new byte[]
        {
            0x00, 0x41,  // flags: WE_HAVE_AN_X_AND_Y_SCALE | ARG_1_AND_2_ARE_WORDS
            0x00, 0x42,  // glyphIndex: 66
            0x01, 0x00,  // arg1: 256
            0x02, 0x00,  // arg2: 512
            0x60, 0x00,  // scaleX: 1.5 in F2DOT14 (24576/16384 = 1.5)
            0x40, 0x00   // scaleY: 1.0 in F2DOT14 (16384/16384 = 1.0)
        };

        var reader = new BigEndianReader(glyphData);
        var header = new GlyphHeader(headerData);
        var composite = new CompositeGlyph(reader, header);

        Assert.Single(composite.Components);
        Assert.Equal(1.5f, composite.Components[0].A, precision: 3);
        Assert.Equal(1.0f, composite.Components[0].D, precision: 3);
    }

    [Fact]
    public void CompositeGlyph_Parses2x2Transform()
    {
        // Component with full 2x2 matrix: flags=0x0081 (WE_HAVE_A_TWO_BY_TWO | ARG_1_AND_2_ARE_WORDS)
        byte[] headerData = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x64 };
        byte[] glyphData = new byte[]
        {
            0x00, 0x81,  // flags: WE_HAVE_A_TWO_BY_TWO | ARG_1_AND_2_ARE_WORDS
            0x00, 0x42,  // glyphIndex: 66
            0x01, 0x00,  // arg1: 256
            0x02, 0x00,  // arg2: 512
            0x40, 0x00,  // a: 1.0 in F2DOT14 (16384/16384 = 1.0)
            0x20, 0x00,  // b: 0.5 in F2DOT14 (8192/16384 = 0.5)
            0x10, 0x00,  // c: 0.25 in F2DOT14 (4096/16384 = 0.25)
            0x40, 0x00   // d: 1.0 in F2DOT14 (16384/16384 = 1.0)
        };

        var reader = new BigEndianReader(glyphData);
        var header = new GlyphHeader(headerData);
        var composite = new CompositeGlyph(reader, header);

        Assert.Single(composite.Components);
        Assert.Equal(1.0f, composite.Components[0].A, precision: 3);
        Assert.Equal(0.5f, composite.Components[0].B, precision: 3);
        Assert.Equal(0.25f, composite.Components[0].C, precision: 3);
        Assert.Equal(1.0f, composite.Components[0].D, precision: 3);
    }

    [Fact]
    public void CompositeGlyph_ParsesByteArguments()
    {
        // Component with byte arguments: flags=0x0020 (MORE_COMPONENTS, no ARG_1_AND_2_ARE_WORDS)
        byte[] headerData = new byte[] { 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x64 };
        byte[] glyphData = new byte[]
        {
            0x00, 0x22,  // flags: MORE_COMPONENTS | ARGS_ARE_XY_VALUES (byte args)
            0x00, 0x42,  // glyphIndex: 66
            0x10,        // arg1: 16 (signed byte)
            0xF0,        // arg2: -16 (signed byte, 0xF0 = -16)

            0x00, 0x02,  // flags: ARGS_ARE_XY_VALUES (last component)
            0x00, 0x85,  // glyphIndex: 133
            0x20,        // arg1: 32
            0xE0         // arg2: -32
        };

        var reader = new BigEndianReader(glyphData);
        var header = new GlyphHeader(headerData);
        var composite = new CompositeGlyph(reader, header);

        Assert.Equal(2, composite.Components.Count);
        Assert.Equal(16, composite.Components[0].Argument1);
        Assert.Equal(-16, composite.Components[0].Argument2);
        Assert.Equal(32, composite.Components[1].Argument1);
        Assert.Equal(-32, composite.Components[1].Argument2);
    }
}
