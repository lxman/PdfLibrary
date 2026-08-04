using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Tests.Fonts;

public class SubstituteFontResolverLadderTests
{
    /// <summary>Returns a fixed face for any request, recording what it was asked for.</summary>
    private sealed class RecordingProvider(byte[] data, int faceIndex) : ISystemFontProvider
    {
        public FontRequest? LastRequest { get; private set; }
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => null;
        public FontMatch? Resolve(FontRequest request)
        {
            LastRequest = request;
            return new FontMatch(data, faceIndex);
        }
    }

    private sealed class NullProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => null;
        public FontMatch? Resolve(FontRequest request) => null;
    }

    private static byte[] RealFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void Load_asks_the_provider_to_resolve_rather_than_fetching_raw_bytes()
    {
        var provider = new RecordingProvider(RealFont(), 0);
        var resolver = new SubstituteFontResolver(provider);

        EmbeddedFontMetrics? metrics = resolver.Resolve("NewCenturySchlbk-Italic", null);

        Assert.NotNull(metrics);
        Assert.NotNull(provider.LastRequest);
        Assert.Equal("NewCenturySchlbk-Italic", provider.LastRequest!.BaseFont);
        Assert.True(provider.LastRequest.Italic);
    }

    [Fact]
    public void A_provider_that_resolves_nothing_yields_null()
    {
        var resolver = new SubstituteFontResolver(new NullProvider());

        Assert.Null(resolver.Resolve("NoSuchFont", null));
    }
}
