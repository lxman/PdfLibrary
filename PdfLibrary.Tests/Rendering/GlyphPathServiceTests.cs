using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Rendering;

namespace PdfLibrary.Tests.Rendering;

public class GlyphPathServiceTests
{
    private static EmbeddedFontMetrics PixelMetrics() =>
        new(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf")));

    [Fact]
    public void GetGlyphPath_ForRealTrueTypeGlyph_ReturnsNonEmptyPath_AndCaches()
    {
        EmbeddedFontMetrics m = PixelMetrics();
        Assert.True(m.IsValid);
        ushort gid = m.GetGlyphId('A');
        Assert.NotEqual(0, gid);
        GlyphOutline? outline = m.GetGlyphOutline(gid);
        Assert.NotNull(outline);

        var service = new GlyphPathService();
        IPathBuilder p1 = service.GetGlyphPath(m, gid, fontSize: 100, outline!, resolvedGlyphName: null);
        IPathBuilder p2 = service.GetGlyphPath(m, gid, fontSize: 100, outline!, resolvedGlyphName: null);

        Assert.False(p1.IsEmpty);
        Assert.Same(p1, p2); // cached: same instance back
    }
}
