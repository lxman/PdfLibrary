using PdfLibrary.Builder;
using PdfLibrary.Builder.Page;

namespace PdfLibrary.Tests.Builder;

public class PdfPageBuilderUnitTests
{
    // Regression: the font-bearing AddText overload used to deposit raw coordinates,
    // ignoring the page's configured unit. On an inches page, (1, 1) must land at
    // (72, 72) points — exactly like the bare AddText(text, x, y) overload.
    [Fact]
    public void AddText_FontOverload_AppliesUnitConversion()
    {
        var page = new PdfPageBuilder(PdfPageSize.Letter).WithInches();

        page.AddText("hi", 1.0, 1.0, "Helvetica", 12);

        var text = Assert.IsType<PdfTextContent>(page.Content.Single());
        Assert.Equal(72.0, text.X, 3);
        Assert.Equal(72.0, text.Y, 3);
    }

    // Regression: AddLine ignored the page unit (raw coordinates), unlike AddText/AddRectangle.
    [Fact]
    public void AddLine_DoubleOverload_AppliesUnitConversion()
    {
        var page = new PdfPageBuilder(PdfPageSize.Letter).WithInches();

        page.AddLine(1, 1, 2, 2);

        var line = Assert.IsType<PdfLineContent>(page.Content.Single());
        Assert.Equal(72.0, line.X1, 3);
        Assert.Equal(72.0, line.Y1, 3);
        Assert.Equal(144.0, line.X2, 3);
        Assert.Equal(144.0, line.Y2, 3);
    }

    [Fact]
    public void AddLine_PdfLengthOverload_ConvertsExplicitUnits()
    {
        var page = new PdfPageBuilder(PdfPageSize.Letter); // default unit = points

        page.AddLine(1.0.Inches(), 1.0.Inches(), 2.0.Inches(), 2.0.Inches());

        var line = Assert.IsType<PdfLineContent>(page.Content.Single());
        Assert.Equal(72.0, line.X1, 3);
        Assert.Equal(72.0, line.Y1, 3);
        Assert.Equal(144.0, line.X2, 3);
        Assert.Equal(144.0, line.Y2, 3);
    }
}
