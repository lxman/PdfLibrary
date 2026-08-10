using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

public class FontRemediationPlannerEmbedTests
{
    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        new(provider ?? SystemFontLocator.Default);

    /// <summary>
    /// Derives the family name the planner itself would read from <paramref name="programBytes"/> —
    /// via the same classify-then-read-the-name-table path <c>FontRemediationPlanner.ProposeEmbed</c>
    /// uses (§7: the description names the RESOLVED program, never the request). Used so a test's
    /// expected family is computed from the bytes at runtime rather than hardcoded, which is the
    /// only way the expectation stays machine-independent.
    /// </summary>
    private static string FamilyNameFromBytes(byte[] programBytes)
    {
        ClassifiedProgram? classified = FontProgramClassifier.Classify(programBytes, faceIndex: 0);
        Assert.NotNull(classified);
        EmbeddedFontMetrics metrics = classified.Format == FontProgramFormat.Type1
            ? new EmbeddedFontMetrics(classified.Program, length1: 0, length2: 0, length3: 0)
            : new EmbeddedFontMetrics(classified.Program);
        Assert.NotNull(metrics.FamilyName);
        return metrics.FamilyName!;
    }

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
        //
        // The expected family is never hardcoded: whichever face SystemFontLocator actually resolves
        // for "Arial" on THIS machine (Arial itself, or a substitute such as Liberation Sans / DejaVu
        // Sans / Nimbus Sans on a box without Arial) is captured once, fed through a StubFontProvider
        // so the planner is deterministic, and the expectation is read back from those same bytes'
        // name table — proving the description names what was actually resolved, not a fixed string.
        FontMatch? match = SystemFontLocator.Default.Resolve(new FontRequest("Arial", Bold: false, Italic: false));
        Assert.SkipWhen(match is null, "No system font resolved on this machine to build the fixture from.");
        string expectedFamily = FamilyNameFromBytes(match!.Data);

        using PdfDocument document = EmbedFixtures.UnembeddedArial();

        FontRemediationProposal result = Planner(new StubFontProvider(match.Data)).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var proposal = Assert.IsType<EmbedProposal>(Assert.Single(result.Fonts));
        Assert.Contains(expectedFamily, proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("system fonts", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_source_description_comes_from_the_resolved_bytes_not_the_request()
    {
        // A locator whose fuzzy ladder returns Courier for a font that is not installed must not
        // produce a description claiming the requested name. The confirmation has to name what
        // will actually be written to the file.
        //
        // "Courier" itself is never hardcoded in the assertion below: on a machine without a real
        // Courier face, EmbedFixtures.CourierBytes() resolves whatever substitute is installed
        // (Nimbus Mono, Liberation Mono, DejaVu Sans Mono, ...), and the expected family is read back
        // from those same bytes. What genuinely discriminates the requirement — that the description
        // must NOT name the requested "Frutiger" — is unchanged.
        byte[]? courierBytes = EmbedFixtures.TryCourierBytes();
        Assert.SkipWhen(courierBytes is null, "No monospace system font resolved on this machine to build the fixture from.");
        string expectedFamily = FamilyNameFromBytes(courierBytes!);

        var locator = new StubFontProvider(courierBytes);
        using PdfDocument document = EmbedFixtures.UnembeddedNamed("Frutiger-Light");

        FontRemediationProposal result = Planner(locator).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var proposal = Assert.IsType<EmbedProposal>(Assert.Single(result.Fonts));
        Assert.DoesNotContain("Frutiger", proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(expectedFamily, proposal.SourceDescription, StringComparison.OrdinalIgnoreCase);
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
    public void Declines_an_unaddressable_font_rather_than_throwing()
    {
        // Final-review finding: the previous version of this test passed the PAGE's object number,
        // which FontInventory.Find cannot resolve to a font — so Propose hit `continue`, result.Fonts
        // came back EMPTY, and an Assert.All over an empty collection proved only "does not throw".
        // ProposeEmbed's !entry.IsAddressable branch had zero coverage. An indirect Type0 wrapper
        // over a DIRECT descendant is the only shape that genuinely reaches it through Propose.
        using PdfDocument document = EmbedFixtures.UnaddressableType0();

        FontRemediationProposal result = Planner().Propose(
            document, [("font-embedded", EmbedFixtures.UnaddressableObjectNumber)]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        // The REASON is what discriminates: with the !IsAddressable branch removed, this font still
        // declines — one branch later, as a composite — so a bare "it declined" assertion would pass
        // over the very defect this test exists to catch.
        Assert.Contains("directly", decline.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("composite", decline.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Declines_a_program_that_no_simple_font_dictionary_may_carry()
    {
        // Fix 1's planner half: PdfDocumentEditor.EmbedProgram refuses a program ISO 32000-2 Table
        // 124 pairs with no simple-font /Subtype (a CID-keyed program, or one whose shape cannot be
        // read at all). The planner must predict that refusal — exactly as it already does for Type 1
        // PFB segment lengths — so a proposal never survives to throw during a user's Save.
        var locator = new StubFontProvider(EmbedFixtures.UnreadableOpenTypeProgram());
        using PdfDocument document = EmbedFixtures.UnembeddedArial();

        FontRemediationProposal result = Planner(locator).Propose(
            document, [("font-embedded", EmbedFixtures.FontObjectNumber(document))]);

        var decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("Table 124", decline.Reason, StringComparison.Ordinal);
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
    public void ProposeEmbed_targets_the_program_holder_even_when_it_differs_from_the_logical_font_id()
    {
        // The_proposal_targets_the_program_holder above cannot actually discriminate: for
        // EmbedFixtures.UnembeddedArial() (a simple font) entry.ProgramHolderId equals entry.Id, so
        // "entry.ProgramHolderId ?? entry.Id" and a bare "entry.Id" would pass that test identically.
        // FontInventory only ever produces a ProgramHolderId DIFFERENT from Id for a Type0 font
        // (FontInventory.BuildEntry), and F-2 declines every composite Kind one branch earlier in
        // ProposeEmbed — so that divergence is genuinely unreachable through Propose() in this
        // increment. This test hand-builds a FontInventoryEntry (mirroring the hand-built Finding in
        // Propose_DoesNotDropASecondDirectFontWithADifferentProgramHolder, which hand-builds a case
        // the live rules cannot produce end-to-end either) with a non-composite Kind whose
        // ProgramHolderId differs from Id, and calls ProposeEmbed directly to prove the targeting
        // expression itself — §3.2's central invariant — ahead of the composite-font increment that
        // will make it reachable through Propose.
        var entry = new FontInventoryEntry(
            Id: new FontId(30),
            ProgramHolderId: new FontId(999),
            BaseFont: "Arial",
            SubsetTag: null,
            FamilyName: "Arial",
            Kind: FontKind.TrueType,
            IsEmbedded: false,
            HasToUnicode: false,
            HasWidths: true,
            IsAddressable: true,
            UsedCodes: [],
            PagesUsedOn: []);
        using var document = new PdfDocument();

        FontProposal proposal = Planner().ProposeEmbed(document, entry, "font-embedded");

        var embed = Assert.IsType<EmbedProposal>(proposal);
        Assert.Equal(999, embed.Font.ObjectNumber);
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
