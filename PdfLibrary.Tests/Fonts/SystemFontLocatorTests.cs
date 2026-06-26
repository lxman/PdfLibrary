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

    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void GetFontData_FindsSubstitute_ForHelvetica()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // A file named like one of Helvetica's candidates; content is the fixture's bytes.
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"),
                      Path.Combine(dir, "LiberationSans-Regular.ttf"));

            var locator = new SystemFontLocator([dir]);
            byte[]? data = locator.GetFontData("Helvetica");

            Assert.NotNull(data);
            Assert.Equal(Fixture(), data);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void GetFontData_ReturnsNull_WhenNoSubstitutePresent()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var locator = new SystemFontLocator([dir]);
            Assert.Null(locator.GetFontData("Helvetica"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void IsFontAvailable_TrueForIndexedFile_FalseOtherwise()
    {
        string dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"),
                      Path.Combine(dir, "NimbusSans-Regular.ttf"));

            var locator = new SystemFontLocator([dir]);

            Assert.True(locator.IsFontAvailable("NimbusSans-Regular"));
            Assert.False(locator.IsFontAvailable("NoSuchFontFile"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
