using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The /FontDescriptor is the only style signal a subset-named, non-embedded font has: "ABCDEF+XYZ123"
/// spells nothing, so /Flags Serif or FixedPitch is what decides between Times, Courier and Helvetica.
/// A regression dropped those two bits on the way to the provider (FontRequest had nowhere to carry
/// them), which silently re-pointed every such font at Helvetica — a metric and shape change with no
/// error anywhere. These tests pin the descriptor signal at both ends of the hand-off: that the
/// resolver puts it into the request, and that the locator's synthetic-name step still reads it.
/// </summary>
public class SubstituteFontDescriptorStyleTests
{
    /// <summary>PDF /Flags bit 1 (value 1) FixedPitch, bit 2 (value 2) Serif — ISO 32000-1 table 123.</summary>
    private static PdfFontDescriptor DescriptorWithFlags(int flags) =>
        new(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("FontDescriptor"),
            [new PdfName("FontName")] = new PdfName("XYZ123"),
            [new PdfName("Flags")] = new PdfInteger(flags),
        });

    /// <summary>Captures the requests it is handed and resolves every one of them, so a test can read
    /// back exactly what the resolver decided before the provider's own ladder gets a say.</summary>
    private sealed class RecordingProvider(byte[] data) : ISystemFontProvider
    {
        public List<FontRequest> Requests { get; } = [];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public byte[]? GetFontData(string baseFontName) => null;

        public FontMatch? Resolve(FontRequest request)
        {
            Requests.Add(request);
            return new FontMatch(data, 0);
        }
    }

    /// <summary>Implements ONLY GetFontData — the shape every third-party provider written before
    /// <see cref="ISystemFontProvider.Resolve"/> existed has. Answers for standard-14 names alone,
    /// which is what such providers were built to do.</summary>
    private sealed class Std14OnlyLegacyProvider(byte[] data) : ISystemFontProvider
    {
        public List<string> Asked { get; } = [];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }

        public byte[]? GetFontData(string baseFontName)
        {
            Asked.Add(baseFontName);
            return baseFontName is "Helvetica" or "Times-Roman" or "Times" or "Courier" ? data : null;
        }
    }

    private static byte[] RealFont() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    [Fact]
    public void A_descriptor_serif_flag_reaches_the_provider_as_a_serif_request()
    {
        var provider = new RecordingProvider(RealFont());
        var resolver = new SubstituteFontResolver(provider);

        resolver.Resolve("ABCDEF+XYZ123", DescriptorWithFlags(0x02));

        FontRequest request = Assert.Single(provider.Requests);
        Assert.True(request.Serif,
            "the descriptor's Serif flag is the only signal this name carries; dropping it sends a "
            + "serif face to Helvetica.");
        Assert.False(request.Mono);
    }

    [Fact]
    public void A_descriptor_fixed_pitch_flag_reaches_the_provider_as_a_mono_request()
    {
        var provider = new RecordingProvider(RealFont());
        var resolver = new SubstituteFontResolver(provider);

        resolver.Resolve("ABCDEF+XYZ456", DescriptorWithFlags(0x01));

        FontRequest request = Assert.Single(provider.Requests);
        Assert.True(request.Mono);
        Assert.False(request.Serif);
    }

    [Fact]
    public void A_legacy_provider_that_only_knows_standard14_names_still_resolves()
    {
        // Master retried GetFontData with the synthetic standard-14 name when the /BaseFont missed.
        // A GetFontData-only provider keys off that string alone, so without the retry an opaque
        // subset name resolves to nothing where it used to resolve — the exact opposite of the
        // "keep working unchanged" contract ISystemFontProvider.Resolve documents.
        var provider = new Std14OnlyLegacyProvider(RealFont());
        var resolver = new SubstituteFontResolver(provider);

        Assert.NotNull(resolver.Resolve("ABCDEF+FooSans", null));
        Assert.Contains("Helvetica", provider.Asked);
    }

    [Fact]
    public void A_serif_request_with_an_opaque_name_lands_where_Times_lands()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? serif = locator.Resolve(new FontRequest("XYZ123", Bold: false, Italic: false, Serif: true));
        FontMatch? times = locator.Resolve(new FontRequest("Times", Bold: false, Italic: false));
        Assert.SkipWhen(times is null, "no serif family installed on this machine");

        Assert.NotNull(serif);
        Assert.Equal(times!.Data, serif!.Data);
    }

    [Fact]
    public void A_mono_request_with_an_opaque_name_lands_where_Courier_lands()
    {
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? mono = locator.Resolve(new FontRequest("XYZ456", Bold: false, Italic: false, Mono: true));
        FontMatch? courier = locator.Resolve(new FontRequest("Courier", Bold: false, Italic: false));
        Assert.SkipWhen(courier is null, "no monospace family installed on this machine");

        Assert.NotNull(mono);
        Assert.Equal(courier!.Data, mono!.Data);
    }

    [Fact]
    public void The_name_derived_family_survives_a_request_whose_flags_say_nothing()
    {
        // The two signals are independent: ORing them is required in both directions, so a descriptor
        // that sets no flags must not erase the family the /BaseFont spells out.
        var locator = new SystemFontLocator(SystemFontLocator.DefaultFontDirectories());
        Assert.SkipWhen(locator.GetAvailableFontFamilies().Count == 0, "no system fonts on this machine");

        FontMatch? byName = locator.Resolve(
            new FontRequest("ABCDEF+OpaqueMonoThing", Bold: false, Italic: false));
        FontMatch? courier = locator.Resolve(new FontRequest("Courier", Bold: false, Italic: false));
        Assert.SkipWhen(courier is null, "no monospace family installed on this machine");

        Assert.NotNull(byName);
        Assert.Equal(courier!.Data, byName!.Data);
    }
}
