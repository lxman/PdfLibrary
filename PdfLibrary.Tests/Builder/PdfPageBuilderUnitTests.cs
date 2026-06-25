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
}
