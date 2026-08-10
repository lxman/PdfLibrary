using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The coverage gate: <c>FontRemediationPlanner.ProposeEmbed</c> must decline an embed when the
/// resolved candidate program cannot render a code the font actually draws, rather than embedding a
/// program that bakes that code in as <c>.notdef</c> permanently (unlike a transient render gap on a
/// machine lacking the substitute — <see cref="GlyphCoverage"/>'s own doc comment).
///
/// <para>Fixture mirrors <see cref="GlyphCoverageTests"/>: <c>PublicPixel.ttf</c> as the resolved
/// candidate (covers Latin, lacks CJK U+4E00), and a font dictionary whose <c>/Differences</c> maps
/// code 200 to glyph name <c>uni4E00</c> — AGL-derivable, so <see cref="GlyphCoverage.UncoveredCodes"/>
/// can report it.</para>
/// </summary>
public class FontRemediationPlannerCoverageTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);
    private static PdfArray Rect(int x0, int y0, int x1, int y1) =>
        new(new PdfInteger(x0), new PdfInteger(y0), new PdfInteger(x1), new PdfInteger(y1));

    private static byte[] PublicPixelBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    /// <summary>An unembedded simple TrueType font, <c>/FirstChar 65</c>/<c>/LastChar 200</c>, whose
    /// <c>/Differences</c> overrides code 200 to <c>uni4E00</c> (CJK, outside WinAnsiEncoding's
    /// default) — everything else falls back to WinAnsiEncoding's ordinary Latin mapping.</summary>
    private static PdfDocument BuildDocument()
    {
        var doc = new PdfDocument();
        doc.AddObject(30, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("TrueType"),
            [N("BaseFont")] = N("TestFont"),
            [N("FirstChar")] = new PdfInteger(65),
            [N("LastChar")] = new PdfInteger(200),
            [N("Encoding")] = new PdfDictionary
            {
                [N("BaseEncoding")] = N("WinAnsiEncoding"),
                [N("Differences")] = new PdfArray(new PdfInteger(200), N("uni4E00")),
            },
        });
        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes("BT /F0 12 Tf (A) Tj ET")));

        var page = new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(2),
            [N("MediaBox")] = Rect(0, 0, 612, 792),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = Ref(30) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(Ref(3)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(2) });
        doc.Trailer.Dictionary[N("Root")] = Ref(1);
        return doc;
    }

    /// <summary>FIXTURE VALIDATION, not the behavior under test: proves the fixture genuinely exercises
    /// the gap the gate exists to catch, before trusting any planner result built on it. Mirrors
    /// <see cref="GlyphCoverageTests.Fixture_DiscriminatesCoveredFromUncovered"/> and
    /// <see cref="GlyphCoverageTests.CjkMappedCode_AgainstLatinCandidate_IsReported"/> directly.</summary>
    [Fact]
    public void Fixture_GenuinelyLacksTheGlyphAndFailsCoverage()
    {
        var metrics = new EmbeddedFontMetrics(PublicPixelBytes());
        Assert.True(metrics.IsValid, "fixture broken: PublicPixel.ttf did not parse as a valid font");

        (ushort gid, _) = metrics.TestCmapLookup(0x4E00);
        Assert.Equal(0, gid); // candidate genuinely lacks the CJK glyph

        using PdfDocument document = BuildDocument();
        PdfDictionary fontDict = Assert.IsType<PdfDictionary>(document.GetObject(30));
        PdfFont? font = PdfFont.Create(fontDict, document);
        Assert.NotNull(font);

        IReadOnlyList<int> uncovered = GlyphCoverage.UncoveredCodes(font!, metrics, font!.FirstChar, font.LastChar);
        Assert.Contains(200, uncovered); // fixture genuinely fails font-embedded coverage
    }

    [Fact]
    public void Declines_an_embed_whose_candidate_cannot_cover_a_drawn_code()
    {
        var locator = new StubFontProvider(PublicPixelBytes());
        using PdfDocument document = BuildDocument();

        FontRemediationProposal result = new FontRemediationPlanner(locator).Propose(
            document, [("font-embedded", 30)]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("1 character", decline.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("U+4E00", decline.Reason, StringComparison.Ordinal);
        Assert.Contains(".notdef", decline.Reason, StringComparison.Ordinal);
    }
}
