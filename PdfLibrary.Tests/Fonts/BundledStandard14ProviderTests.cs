using System.Collections.Generic;
using PdfLibrary.Fonts;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The bundled provider is POLICY ONLY — it holds no font bytes. It decides which /BaseFont names
/// a bundled face may answer for. With the default staged locator it can also answer after an
/// unknown face/family genuinely misses but before the locator's synthetic system-font floor;
/// non-staged providers still receive non-alias requests unchanged.
///
/// <para>These tests use a fake byte source and a recording inner provider, so they assert the
/// routing decisions without needing any real font file. Whether the bytes are a real Liberation
/// face is Task 5/6's problem; whether the RIGHT face is asked for is this task's.</para>
/// </summary>
public class BundledStandard14ProviderTests
{
    /// <summary>Records what was asked of it and answers nothing, so a fall-through is observable.</summary>
    private sealed class RecordingInner : ISystemFontProvider
    {
        public readonly List<string> Asked = [];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public FontMatch? Resolve(FontRequest request) { Asked.Add(request.BaseFont); return null; }
    }

    /// <summary>Models the default locator's three-stage contract: explicit PostScript/family stages
    /// can miss or hit before its public Resolve continues to a synthetic system-font fallback.</summary>
    private sealed class StagedInner(FontMatch? explicitMatch, FontMatch? syntheticFallback)
        : ISystemFontProvider, IStagedSystemFontProvider
    {
        public readonly List<string> BeforeSyntheticAsked = [];
        public readonly List<string> FullAsked = [];
        public IReadOnlyCollection<string> GetAvailableFontFamilies() => [];
        public bool IsFontAvailable(string familyName) => false;
        public string? FindFirstAvailable(IEnumerable<string> candidates) => null;
        public void RefreshCache() { }
        public FontMatch? ResolveBeforeSyntheticFallback(FontRequest request)
        {
            BeforeSyntheticAsked.Add(request.BaseFont);
            return explicitMatch;
        }
        public FontMatch? Resolve(FontRequest request)
        {
            FullAsked.Add(request.BaseFont);
            return syntheticFallback;
        }
    }

    /// <summary>Answers for every face with a 1-byte marker, so "which face was requested" is the
    /// only thing under test.</summary>
    private sealed class FakeBytes
    {
        public readonly List<string> Requested = [];
        public byte[]? For(string face) { Requested.Add(face); return [0x01]; }
    }

    private static FontRequest Req(string baseFont, bool bold = false, bool italic = false) =>
        new(baseFont, bold, italic, Serif: false, Mono: false, ExplicitBold: bold, ExplicitItalic: italic);

    [Theory]
    [InlineData("Helvetica", "LiberationSans-Regular")]
    [InlineData("Arial", "LiberationSans-Regular")]
    [InlineData("Times-Roman", "LiberationSerif-Regular")]
    [InlineData("TimesNewRoman", "LiberationSerif-Regular")]
    [InlineData("Courier", "LiberationMono-Regular")]
    [InlineData("CourierNew", "LiberationMono-Regular")]
    public void AliasedFamiliesResolveToTheirBundledFace(string baseFont, string expectedFace)
    {
        var bytes = new FakeBytes();
        var inner = new RecordingInner();
        var provider = new BundledStandard14Provider(bytes.For, inner);

        Assert.NotNull(provider.Resolve(Req(baseFont)));
        Assert.Equal([expectedFace], bytes.Requested);
        Assert.Empty(inner.Asked);   // answered here; the inner provider must never see it
    }

    [Theory]
    [InlineData("Helvetica-Bold", "LiberationSans-Bold")]
    [InlineData("Helvetica-Oblique", "LiberationSans-Italic")]
    [InlineData("Helvetica-BoldOblique", "LiberationSans-BoldItalic")]
    [InlineData("Times-Bold", "LiberationSerif-Bold")]
    [InlineData("Times-Italic", "LiberationSerif-Italic")]
    [InlineData("Times-BoldItalic", "LiberationSerif-BoldItalic")]
    [InlineData("Courier-BoldOblique", "LiberationMono-BoldItalic")]
    public void StyleIsCarriedOntoTheBundledFace(string baseFont, string expectedFace)
    {
        var bytes = new FakeBytes();
        var provider = new BundledStandard14Provider(bytes.For, new RecordingInner());

        Assert.NotNull(provider.Resolve(Req(baseFont,
            bold: baseFont.Contains("Bold"),
            italic: baseFont.Contains("Italic") || baseFont.Contains("Oblique"))));
        Assert.Equal([expectedFace], bytes.Requested);
    }

    [Theory]
    // Liberation has NO Symbol and NO Dingbats face. Answering a Latin face for symbol-encoded
    // content produces confident garbage, so these must fall through untouched.
    [InlineData("Symbol")]
    [InlineData("ZapfDingbats")]
    // No Liberation equivalent exists for the rest of the base-35 set.
    [InlineData("Palatino-Roman")]
    [InlineData("Bookman-Demi")]
    [InlineData("AvantGarde-Book")]
    [InlineData("NewCenturySchlbk-Roman")]
    [InlineData("ZapfChancery-MediumItalic")]
    // A genuinely named face is not a direct alias. A non-staged inner receives it unchanged.
    [InlineData("FooCorpSans")]
    [InlineData("ABCDEF+FooCorpSans")]
    public void Unsupported_and_nonstaged_unknown_requests_fall_through(string baseFont)
    {
        var bytes = new FakeBytes();
        var inner = new RecordingInner();
        var provider = new BundledStandard14Provider(bytes.For, inner);

        Assert.Null(provider.Resolve(Req(baseFont)));
        Assert.Empty(bytes.Requested);          // no bundled face was even considered
        Assert.Equal([baseFont], inner.Asked);  // and the inner provider got the original request
    }

    [Fact]
    public void ASubsetTagDoesNotHideAnAliasedFamily()
    {
        // Base35Aliases.Split strips "ABCDEF+"; the provider must not miss an aliased family behind one.
        var bytes = new FakeBytes();
        var provider = new BundledStandard14Provider(bytes.For, new RecordingInner());

        Assert.NotNull(provider.Resolve(Req("ABCDEF+Helvetica")));
        Assert.Equal(["LiberationSans-Regular"], bytes.Requested);
    }

    [Fact]
    public void WhenTheCallerHasNoBundledFaceItFallsThrough()
    {
        // The byte source is a delegate the host supplies. A host that ships nothing must degrade to
        // exactly today's behaviour rather than failing to resolve at all.
        var inner = new RecordingInner();
        var provider = new BundledStandard14Provider(_ => null, inner);

        Assert.Null(provider.Resolve(Req("Helvetica")));
        Assert.Equal(["Helvetica"], inner.Asked);
    }

    [Fact]
    public void Synthetic_fallback_uses_bundled_face_after_explicit_locator_stages_miss()
    {
        var bytes = new FakeBytes();
        var systemFallback = new FontMatch([0x22], 0);
        var inner = new StagedInner(explicitMatch: null, systemFallback);
        var provider = new BundledStandard14Provider(bytes.For, inner);

        FontMatch match = Assert.IsType<FontMatch>(provider.Resolve(Req("AlArabiya")));

        Assert.Equal([0x01], match.Data);
        Assert.Equal(["AlArabiya"], inner.BeforeSyntheticAsked);
        Assert.Empty(inner.FullAsked);
        Assert.Equal(["LiberationSans-Regular"], bytes.Requested);
    }

    [Fact]
    public void Explicit_locator_match_wins_before_bundled_synthetic_fallback()
    {
        var explicitMatch = new FontMatch([0x33], 0);
        var inner = new StagedInner(explicitMatch, syntheticFallback: new FontMatch([0x22], 0));
        var bytes = new FakeBytes();
        var provider = new BundledStandard14Provider(bytes.For, inner);

        FontMatch match = Assert.IsType<FontMatch>(provider.Resolve(Req("AlArabiya")));

        Assert.Same(explicitMatch, match);
        Assert.Equal(["AlArabiya"], inner.BeforeSyntheticAsked);
        Assert.Empty(inner.FullAsked);
        Assert.Empty(bytes.Requested);
    }

    [Fact]
    public void Missing_bundled_synthetic_face_preserves_the_locators_full_fallback()
    {
        var systemFallback = new FontMatch([0x22], 0);
        var inner = new StagedInner(explicitMatch: null, systemFallback);
        var provider = new BundledStandard14Provider(_ => null, inner);

        Assert.Same(systemFallback, provider.Resolve(Req("AlArabiya")));
        Assert.Equal(["AlArabiya"], inner.BeforeSyntheticAsked);
        Assert.Equal(["AlArabiya"], inner.FullAsked);
    }

    [Theory]
    [InlineData("Symbol")]
    [InlineData("ZapfDingbats")]
    [InlineData("Palatino-Roman")]
    [InlineData("Bookman-Demi")]
    public void Unsupported_base35_family_never_synthesizes_a_Latin_bundled_face(string baseFont)
    {
        var systemFallback = new FontMatch([0x22], 0);
        var inner = new StagedInner(explicitMatch: null, systemFallback);
        var bytes = new FakeBytes();
        var provider = new BundledStandard14Provider(bytes.For, inner);

        Assert.Same(systemFallback, provider.Resolve(Req(baseFont)));
        Assert.Equal([baseFont], inner.BeforeSyntheticAsked);
        Assert.Equal([baseFont], inner.FullAsked);
        Assert.Empty(bytes.Requested);
    }
}
