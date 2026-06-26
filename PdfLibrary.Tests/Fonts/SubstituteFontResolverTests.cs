using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts;

public class SubstituteFontResolverTests
{
    [Theory]
    [InlineData("Times-Bold", false, false, true, false, true, false)]      // serif, bold
    [InlineData("CourierNewPSMT", false, false, false, true, false, false)] // mono
    [InlineData("Helvetica-Oblique", false, false, false, false, false, true)] // sans, italic
    [InlineData("ABCDEF+Garamond", false, false, true, false, false, false)] // serif by name
    public void Classify_FromName_NoDescriptor(string baseFont, bool _, bool __,
        bool expectSerif, bool expectMono, bool expectBold, bool expectItalic)
    {
        (bool serif, bool mono, bool bold, bool italic) = SubstituteFontResolver.Classify(baseFont, null);
        Assert.Equal(expectSerif, serif);
        Assert.Equal(expectMono, mono);
        Assert.Equal(expectBold, bold);
        Assert.Equal(expectItalic, italic);
    }

    [Theory]
    [InlineData(false, false, false, false, "Helvetica")]
    [InlineData(true, false, true, false, "Times-Bold")]
    [InlineData(false, true, false, true, "Courier-Italic")]
    [InlineData(true, false, true, true, "Times-BoldItalic")]
    public void SyntheticStd14Name_Maps(bool serif, bool mono, bool bold, bool italic, string expected)
        => Assert.Equal(expected, SubstituteFontResolver.SyntheticStd14Name(serif, mono, bold, italic));

    [Fact]
    public void Resolve_LoadsAndCachesSubstituteMetrics()
    {
        byte[] fontBytes = File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        var provider = new FakeProvider(fontBytes);

        var resolver = new SubstituteFontResolver(provider);
        EmbeddedFontMetrics? m1 = resolver.Resolve("Helvetica", null);
        EmbeddedFontMetrics? m2 = resolver.Resolve("Helvetica", null);

        Assert.NotNull(m1);
        Assert.True(m1!.IsValid);
        Assert.Same(m1, m2);                       // cached by BaseFont
        Assert.True(provider.Requested.Count >= 1); // asked the provider for bytes
    }

    private sealed class FakeProvider(byte[] bytes) : ISystemFontProvider
    {
        public List<string> Requested { get; } = [];
        public byte[]? GetFontData(string baseFontName) { Requested.Add(baseFontName); return bytes; }
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => Array.Empty<string>();
        public bool IsFontAvailable(string familyName) => true;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }
}
