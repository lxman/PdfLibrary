using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// <see cref="FontRemediationPlanner.AssessCandidate"/>: runs a CALLER-SUPPLIED substitute's bytes
/// through the same gate chain <c>ProposeEmbed</c> uses, but splits the outcome into hard blocks
/// (fsType, PFB, Table 124, unclassifiable, entry shape — a licensing/mechanical fact) and warnings
/// (coverage shortfall, Symbol/Latin mismatch — the user's judgement, since they picked this
/// candidate on purpose). Case 7 pins that <c>ProposeEmbed</c> itself is unchanged by the refactor
/// that shares gates between the two paths.
/// </summary>
public class CandidateAssessmentTests
{
    private static PdfName N(string s) => new(s);

    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        new(provider ?? SystemFontLocator.Default);

    private static byte[] LiberationSansRegularBytes() =>
        File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory, "Resources", "Liberation", "LiberationSans-Regular.ttf"));

    private static byte[] PublicPixelBytes() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Resources", "PublicPixel.ttf"));

    private static FontInventoryEntry FindEntry(PdfDocument document, int objectNumber)
    {
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(document);
        FontInventoryEntry? entry = FontInventory.Find(inventory, objectNumber);
        Assert.NotNull(entry);
        return entry!;
    }

    // A font dictionary whose /Differences maps code 200 to uni4E00 (CJK), everything else falling
    // back to WinAnsiEncoding — mirrors FontRemediationPlannerCoverageTests.BuildDocument exactly, so
    // the coverage gap the coverage-warning case exercises is proven genuine the same way.
    private static PdfDocument BuildCoverageGapDocument()
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
            [N("Parent")] = new PdfIndirectReference(2, 0),
            [N("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), new PdfInteger(612), new PdfInteger(792)),
            [N("Contents")] = new PdfIndirectReference(11, 0),
            [N("Resources")] = new PdfDictionary { [N("Font")] = new PdfDictionary { [N("F0")] = new PdfIndirectReference(30, 0) } },
        };
        doc.AddObject(3, 0, page);
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"), [N("Kids")] = new PdfArray(new PdfIndirectReference(3, 0)), [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(1, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = new PdfIndirectReference(2, 0) });
        doc.Trailer.Dictionary[N("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    [Fact]
    public void Accepts_a_good_candidate_with_no_hard_block_and_no_warnings()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.FontObjectNumber(document));
        byte[] candidate = LiberationSansRegularBytes();

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", candidate, faceIndex: 0, sourceDescription: "Liberation Sans (Regular)");

        Assert.Null(result.HardBlockReason);
        Assert.Empty(result.Warnings);
        Assert.NotNull(result.Proposal);
        Assert.Equal(FontProgramFormat.TrueType, result.Format);
        var embed = Assert.IsType<EmbedProposal>(result.Proposal);
        Assert.Equal(FontProgramFormat.TrueType, embed.Format);
        Assert.Same(candidate, embed.Program);
        Assert.Equal("Liberation Sans (Regular)", embed.SourceDescription);
        Assert.Equal(entry.ProgramHolderId ?? entry.Id, embed.Font);
    }

    [Fact]
    public void Hard_blocks_a_candidate_whose_vendor_restricts_embedding()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.FontObjectNumber(document));
        byte[] candidate = EmbedFixtures.RestrictedEmbeddingFont();

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", candidate, faceIndex: 0, sourceDescription: "Arial (restricted)");

        Assert.NotNull(result.HardBlockReason);
        Assert.Contains("restricted", result.HardBlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Proposal);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Hard_blocks_unclassifiable_bytes()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.FontObjectNumber(document));

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", [0x00, 0x01], faceIndex: 0, sourceDescription: "garbage");

        Assert.NotNull(result.HardBlockReason);
        Assert.Null(result.Format);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public void Hard_blocks_a_program_no_simple_font_dictionary_may_carry()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.FontObjectNumber(document));

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", EmbedFixtures.UnreadableOpenTypeProgram(),
            faceIndex: 0, sourceDescription: "unreadable");

        Assert.NotNull(result.HardBlockReason);
        Assert.Contains("Table 124", result.HardBlockReason, StringComparison.Ordinal);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public void Warns_but_does_not_hard_block_a_coverage_shortfall()
    {
        using PdfDocument document = BuildCoverageGapDocument();
        FontInventoryEntry entry = FindEntry(document, 30);
        byte[] candidate = PublicPixelBytes();

        // Fixture validation, mirroring FontRemediationPlannerCoverageTests: prove the candidate
        // genuinely lacks the CJK glyph before trusting a result built on it.
        var metrics = new EmbeddedFontMetrics(candidate);
        Assert.True(metrics.IsValid, "fixture broken: PublicPixel.ttf did not parse as a valid font");
        (ushort gid, _) = metrics.TestCmapLookup(0x4E00);
        Assert.Equal(0, gid);

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", candidate, faceIndex: 0, sourceDescription: "PublicPixel");

        Assert.Null(result.HardBlockReason);
        Assert.NotNull(result.Proposal);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("1 character", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("U+4E00", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Warns_but_does_not_hard_block_a_symbol_mismatch()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedNamed("Symbol");
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.FontObjectNumber(document));
        byte[] candidate = PublicPixelBytes();

        var metrics = new EmbeddedFontMetrics(candidate);
        Assert.False(metrics.HasSymbolCmapEncoding(), "fixture broken: PublicPixel.ttf must NOT be symbol-encoded");

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", candidate, faceIndex: 0, sourceDescription: "PublicPixel");

        Assert.Null(result.HardBlockReason);
        Assert.NotNull(result.Proposal);
        string warning = Assert.Single(result.Warnings);
        Assert.Contains("symbol-encoded", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("garbage", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hard_blocks_a_composite_entry_regardless_of_candidate_bytes()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedType0();
        FontInventoryEntry entry = FindEntry(document, EmbedFixtures.DescendantObjectNumber(document));

        CandidateAssessment result = Planner().AssessCandidate(
            document, entry, "font-embedded", LiberationSansRegularBytes(), faceIndex: 0, sourceDescription: "Liberation Sans");

        Assert.NotNull(result.HardBlockReason);
        Assert.Contains("composite", result.HardBlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Proposal);
    }

    [Fact]
    public void Regression_ProposeEmbed_still_declines_the_coverage_case()
    {
        // Pins that the automatic path's behaviour is unchanged by the refactor that shares gates
        // with AssessCandidate: coverage stays a decline there, never a warning.
        var locator = new StubFontProvider(PublicPixelBytes());
        using PdfDocument document = BuildCoverageGapDocument();

        FontRemediationProposal result = Planner(locator).Propose(document, [("font-embedded", 30)]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("1 character", decline.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("U+4E00", decline.Reason, StringComparison.Ordinal);
        Assert.Contains(".notdef", decline.Reason, StringComparison.Ordinal);
    }
}
