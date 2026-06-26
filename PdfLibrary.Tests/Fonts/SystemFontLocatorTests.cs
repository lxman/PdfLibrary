using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

public class SystemFontLocatorTests
{
    // A minimal provider that does not override GetFontData — proves the default is null.
    private sealed class BareProvider : ISystemFontProvider
    {
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => Array.Empty<string>();
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
    }

    [Fact]
    public void GetFontData_DefaultInterfaceImplementation_ReturnsNull()
    {
        ISystemFontProvider provider = new BareProvider();
        Assert.Null(provider.GetFontData("Helvetica"));
    }
}
