using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

public class FontRemediationPlannerEmbedTests
{
    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        new(provider ?? SystemFontLocator.Default);

    [Fact]
    public void Proposes_an_embed_for_an_unembedded_simple_font()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedArial();

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var proposal = Assert.IsType<EmbedProposal>(Assert.Single(result.Fonts));
        Assert.Equal("font-embedded", proposal.RuleId);
        Assert.NotEmpty(proposal.Program);
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
    }

    [Fact]
    public void The_source_description_names_the_face_and_its_origin()
    {
        // Design §7: the panel row IS the licensing confirmation, so the row's text must name the
        // face. A proposal whose description said "embed font" would satisfy a non-empty check and
        // fail the actual requirement.
        using PdfDocument document = EmbedFixtures.UnembeddedArial();

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var proposal = Assert.IsType<EmbedProposal>(Assert.Single(result.Fonts));
        Assert.Contains("Arial", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system fonts", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_source_description_comes_from_the_resolved_bytes_not_the_request()
    {
        // A locator whose fuzzy ladder returns Courier for a font that is not installed must not
        // produce a description claiming the requested name. The confirmation has to name what
        // will actually be written to the file.
        var locator = new StubFontProvider(EmbedFixtures.CourierBytes());
        using PdfDocument document = EmbedFixtures.UnembeddedNamed("Frutiger-Light");

        FontRemediationProposal result = Planner(locator).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var proposal = Assert.IsType<EmbedProposal>(Assert.Single(result.Fonts));
        Assert.DoesNotContain("Frutiger", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Courier", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declines_when_no_matching_font_is_installed()
    {
        using PdfDocument document = EmbedFixtures.UnembeddedNamed("Frutiger-Light");

        FontRemediationProposal result = Planner(new StubFontProvider(null)).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("Frutiger-Light", decline.Reason);
        Assert.Contains("installed", decline.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declines_a_font_whose_vendor_restricts_embedding()
    {
        // fsType bit 1. This check is what makes §7's consent meaningful rather than decorative:
        // the user confirming an embed cannot consent on the vendor's behalf.
        var locator = new StubFontProvider(EmbedFixtures.RestrictedEmbeddingFont());
        using PdfDocument document = EmbedFixtures.UnembeddedArial();

        FontRemediationProposal result = Planner(locator).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("restricted", decline.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declines_a_composite_font_and_names_the_reason()
    {
        // Not a capability gap to paper over: under Identity-H the document's CIDs are the ORIGINAL
        // program's glyph indices, so a substitute with a different glyph order renders real glyphs
        // in the wrong places. That is F-4's problem and it must not be silently attempted here.
        using PdfDocument document = EmbedFixtures.UnembeddedType0();

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.DescendantObjectNumber(document))]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("composite", decline.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declines_a_direct_font_dictionary_rather_than_throwing()
    {
        using PdfDocument document = EmbedFixtures.DirectFontDictionary();

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.PageObjectNumber(document))]);

        Assert.All(result.Fonts, p => Assert.IsType<DeclineProposal>(p));
    }

    [Fact]
    public void The_proposal_targets_the_program_holder()
    {
        // §3.2: /FontFile* lives on the program holder. A proposal naming the logical font would
        // write a program somewhere no viewer reads it — valid syntax, invisible failure.
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        int expected = EmbedFixtures.ProgramHolderObjectNumber(document);

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        Assert.Equal(expected, Assert.Single(result.Fonts).Font.ObjectNumber);
    }

    [Fact]
    public void The_planner_does_not_mutate_the_document()
    {
        // §6: "The planner never mutates." Proposals are bytes in memory until the user saves.
        using PdfDocument document = EmbedFixtures.UnembeddedArial();
        int objectCountBefore = EmbedFixtures.ObjectCount(document);

        Planner().Propose(document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        Assert.Equal(objectCountBefore, EmbedFixtures.ObjectCount(document));
        Assert.False(EmbedFixtures.HasFontFile(document));
    }
}
