using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The enumeration surface answers "list what you have" for the manual picker. It is a
/// read-only projection of the metadata index the locator already holds — no new scanning.
/// </summary>
public class FontEnumerationTests
{
    [Fact]
    public void DefaultImplementationEnumeratesNothing()
    {
        // A provider that never opts in (every pre-L-3 implementor) must keep compiling
        // and answer with an empty list, not throw.
        var bare = new BareProvider();
        Assert.Empty(((ISystemFontProvider)bare).EnumerateFaces());
    }

    [Fact]
    public void SystemLocatorEnumeratesRealFaces()
    {
        IReadOnlyList<SystemFontFace> faces = SystemFontLocator.Default.EnumerateFaces();
        Assert.NotEmpty(faces);                                    // every dev/CI box has fonts
        Assert.All(faces, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Family));
            Assert.False(string.IsNullOrWhiteSpace(f.Path));
        });
        // Style flags must be live data, not defaults: any real system has at least one bold face.
        Assert.Contains(faces, f => f.Bold);
    }

    [Fact]
    public void BundledProviderDelegatesEnumerationToInner()
    {
        var inner = new CannedEnumerationProvider();
        var provider = new BundledStandard14Provider(_ => null, inner);
        Assert.Same(inner.Canned, provider.EnumerateFaces());
    }

    private sealed class BareProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }

    private sealed class CannedEnumerationProvider : ISystemFontProvider
    {
        public readonly IReadOnlyList<SystemFontFace> Canned =
            [new SystemFontFace("Canned", "Canned-Regular", false, false, "c:/canned.ttf", 0)];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public IReadOnlyList<SystemFontFace> EnumerateFaces() => Canned;
    }
}
