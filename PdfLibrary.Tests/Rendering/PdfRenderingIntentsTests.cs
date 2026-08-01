using ICCSharp.Profile;
using PdfLibrary.Rendering.Icc;
using Xunit;

namespace PdfLibrary.Tests.Rendering;

public class PdfRenderingIntentsTests
{
    [Theory]
    [InlineData("Perceptual", RenderingIntent.Perceptual)]
    [InlineData("Saturation", RenderingIntent.Saturation)]
    [InlineData("AbsoluteColorimetric", RenderingIntent.AbsoluteColorimetric)]
    [InlineData("RelativeColorimetric", RenderingIntent.RelativeColorimetric)]
    [InlineData(null, RenderingIntent.RelativeColorimetric)]
    [InlineData("NoSuchIntent", RenderingIntent.RelativeColorimetric)]
    public void Map_follows_pdf_name_semantics(string? name, RenderingIntent expected)
        => Assert.Equal(expected, PdfRenderingIntents.Map(name));
}
