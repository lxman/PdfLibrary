using PdfLibrary.Editing.Forms;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FieldDaParserTests
{
    [Fact]
    public void Parse_FontSizeColor()
    {
        FieldDa da = FieldDaParser.Parse("/Helv 12 Tf 0 0 1 rg");
        Assert.Equal("Helv", da.FontName);
        Assert.Equal(12, da.FontSize);
        Assert.Equal("0 0 1 rg", da.ColorOps.Trim());
    }

    [Fact]
    public void Parse_AutoSize_Zero()
    {
        FieldDa da = FieldDaParser.Parse("/Helv 0 Tf 0 g");
        Assert.Equal(0, da.FontSize);
        Assert.Equal("Helv", da.FontName);
    }

    [Fact]
    public void Parse_NullOrMissingTf_FallsBackToHelvAutoSize()
    {
        FieldDa da = FieldDaParser.Parse(null);
        Assert.Equal("Helv", da.FontName);
        Assert.Equal(0, da.FontSize);
    }

    [Fact]
    public void Parse_DefaultColor_WhenNoColorOp()
    {
        FieldDa da = FieldDaParser.Parse("/Helv 10 Tf");
        Assert.Equal("0 g", da.ColorOps.Trim());
    }
}
