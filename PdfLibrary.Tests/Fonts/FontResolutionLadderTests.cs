using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class FontResolutionLadderTests
{
    [Fact]
    public void An_unmatchable_request_returns_null_and_adds_no_floor()
    {
        // SLICE 1 BOUNDARY. Slice 2 introduces a bundled Liberation floor and will deliberately
        // invert this assertion. Until then, failing every step must behave exactly as the engine
        // does today: return null, and let the caller draw nothing.
        var locator = new SystemFontLocator(["/definitely/not/a/real/path"]);

        Assert.Null(locator.Resolve(new FontRequest("NoSuchFontAnywhere", Bold: false, Italic: false)));
    }

    [Fact]
    public void Resolves_a_standard14_request_against_the_real_system_fonts()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? match = locator.Resolve(new FontRequest("Helvetica", Bold: false, Italic: false));

        Assert.NotNull(match);
        Assert.NotEmpty(match!.Data);
        Assert.True(match.FaceIndex >= 0);
    }

    [Fact]
    public void An_italic_request_resolves_to_a_face_whose_style_bit_is_set()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? match = locator.Resolve(new FontRequest("Times-Italic", Bold: false, Italic: true));
        Assert.NotNull(match);

        // The whole point of the ladder: the returned FACE must itself be italic, not merely a file
        // whose name looked right. This is the defect class that shipped on macOS.
        var metrics = new PdfLibrary.Fonts.Embedded.EmbeddedFontMetrics(match!.Data, match.FaceIndex);
        Assert.True(metrics.IsItalic);
    }

    [Fact]
    public void The_default_interface_implementation_keeps_existing_providers_working()
    {
        // An ISystemFontProvider written before Resolve existed must still function.
        ISystemFontProvider legacy = new LegacyProvider([1, 2, 3]);

        FontMatch? match = legacy.Resolve(new FontRequest("Anything", Bold: false, Italic: false));

        Assert.NotNull(match);
        Assert.Equal(new byte[] { 1, 2, 3 }, match!.Data);
        Assert.Equal(0, match.FaceIndex);
    }

    [Fact]
    public void The_default_implementation_still_selects_the_face_inside_a_collection()
    {
        // A provider implementing ONLY GetFontData hands back whole-file bytes. If the default
        // Resolve returned face 0 unconditionally it would silently undo the collection face
        // selection shipped in 6afbe7a for every third-party provider. The real font here is a
        // single-face file, so the assertion is that the selection RUNS and yields a valid index.
        byte[] bare = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        ISystemFontProvider legacy = new LegacyProvider(bare);

        FontMatch? match = legacy.Resolve(new FontRequest("Anything", Bold: false, Italic: true));

        Assert.NotNull(match);
        Assert.Equal(0, match!.FaceIndex);
    }

    private sealed class LegacyProvider(byte[] data) : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => data;
    }
}
