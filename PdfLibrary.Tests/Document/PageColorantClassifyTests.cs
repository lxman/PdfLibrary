using PdfLibrary.Document;
using Xunit;

namespace PdfLibrary.Tests.Document;

public class PageColorantClassifyTests
{
    [Theory]
    [InlineData("Cyan", ColorantKind.Process)]
    [InlineData("Magenta", ColorantKind.Process)]
    [InlineData("Yellow", ColorantKind.Process)]
    [InlineData("Black", ColorantKind.Process)]
    [InlineData("All", ColorantKind.All)]
    [InlineData("None", ColorantKind.None)]
    [InlineData("PANTONE 185 C", ColorantKind.Spot)]
    [InlineData("Spot Varnish", ColorantKind.Spot)]
    public void Classify_MapsNameToKind(string name, ColorantKind expected)
    {
        Assert.Equal(expected, PageColorant.Classify(name));
    }
}
