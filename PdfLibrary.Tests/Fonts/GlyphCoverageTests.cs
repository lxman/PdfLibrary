using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// <see cref="GlyphCoverage.UncoveredCodes"/> answers, for a font's DRAWN codes, which ones the
/// candidate embed program cannot render. Modeled on <see cref="LiberationSubstitutionAuditTests"/>'s
/// probe: same discrimination check (Latin covered, Cyrillic covered, CJK NOT covered) before any
/// result from the fixture is trusted.
///
/// <para>The candidate here is <c>PublicPixel.ttf</c>, the repo's existing test-only face
/// (Resources/PublicPixel.LICENSE.txt) — it covers Latin and Cyrillic but has no CJK glyphs, which
/// gives exactly the discriminating shape <see cref="LiberationSubstitutionAuditTests"/> uses for
/// Liberation without depending on a real Liberation installation being present on the machine.</para>
/// </summary>
public class GlyphCoverageTests
{
    private static EmbeddedFontMetrics LoadCandidate()
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));
        var metrics = new EmbeddedFontMetrics(bytes);
        Assert.True(metrics.IsValid, "fixture broken: PublicPixel.ttf did not parse as a valid font");
        return metrics;
    }

    private static bool Covers(EmbeddedFontMetrics face, int unicode)
    {
        if (unicode is <= 0 or > 0xFFFF) return false;
        (ushort gid, _) = face.TestCmapLookup((ushort)unicode);
        return gid != 0;
    }

    /// <summary>DISCRIMINATION CHECK, reused verbatim from <see cref="LiberationSubstitutionAuditTests"/>.
    /// A probe that cannot tell covered from uncovered reports a reassuring zero. PublicPixel must
    /// cover Latin 'A' and Cyrillic 'А', and must NOT cover CJK U+4E00.</summary>
    [Fact]
    public void Fixture_DiscriminatesCoveredFromUncovered()
    {
        EmbeddedFontMetrics candidate = LoadCandidate();

        Assert.True(Covers(candidate, 0x0041), "probe broken: PublicPixel does not cover 'A'");
        Assert.True(Covers(candidate, 0x0410), "probe broken: PublicPixel does not cover Cyrillic 'А'");
        Assert.False(Covers(candidate, 0x4E00), "probe broken: PublicPixel reports coverage of CJK U+4E00");
    }

    private static PdfName N(string s) => new(s);

    /// <summary>An unembedded simple TrueType font whose /Encoding is WinAnsiEncoding overridden via
    /// /Differences: code 200 draws glyph name "uni4E00" (CJK, AGL-derivable via the uniXXXX rule),
    /// code 201 draws "g999" (not an AGL name, no derivable Unicode). ASCII codes 65-90 are left at
    /// WinAnsiEncoding's default ('A'..'Z').</summary>
    private static PdfFont BuildFont()
    {
        var dict = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("TestFont"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(201),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(
                    new PdfInteger(200), N("uni4E00"), N("g999")),
            },
        };

        PdfFont? font = PdfFont.Create(dict);
        Assert.NotNull(font);
        return font!;
    }

    [Fact]
    public void AsciiOnlyFont_AgainstLatinCandidate_ReportsNothing()
    {
        PdfFont font = BuildFont();
        EmbeddedFontMetrics candidate = LoadCandidate();

        IReadOnlyList<int> uncovered = GlyphCoverage.UncoveredCodes(font, candidate, 65, 90);

        Assert.Empty(uncovered);
    }

    [Fact]
    public void CjkMappedCode_AgainstLatinCandidate_IsReported()
    {
        PdfFont font = BuildFont();
        EmbeddedFontMetrics candidate = LoadCandidate();

        IReadOnlyList<int> uncovered = GlyphCoverage.UncoveredCodes(font, candidate, 65, 201);

        Assert.Contains(200, uncovered);
    }

    [Fact]
    public void UnderivableGlyphName_IsNotReported()
    {
        PdfFont font = BuildFont();
        EmbeddedFontMetrics candidate = LoadCandidate();

        IReadOnlyList<int> uncovered = GlyphCoverage.UncoveredCodes(font, candidate, 65, 201);

        // "g999" is not an AGL name and not a uniXXXX name: nothing is provable about it, so it
        // must never appear in the result even though PublicPixel plainly has no such glyph.
        Assert.DoesNotContain(201, uncovered);
    }

    [Fact]
    public void CodesOutsideFirstLastChar_AreNeverProbed()
    {
        PdfFont font = BuildFont();
        EmbeddedFontMetrics candidate = LoadCandidate();

        // Bound the probe to the ASCII range only; the CJK-mapped code 200 lives outside it and must
        // never surface even though the font dictionary itself declares LastChar=201.
        IReadOnlyList<int> uncovered = GlyphCoverage.UncoveredCodes(font, candidate, 65, 90);

        Assert.DoesNotContain(200, uncovered);
        Assert.Empty(uncovered);
    }
}
