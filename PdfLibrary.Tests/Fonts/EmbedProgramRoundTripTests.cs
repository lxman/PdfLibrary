using PdfLibrary.Builder;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// F-2's definition of done (design §11): stage the fix, apply it, re-run preflight, assert the
/// finding is gone — and assert the text did not move while doing it.
/// </summary>
public class EmbedProgramRoundTripTests
{
    /// <summary>
    /// A one-page document drawing text in Helvetica with no embedded program. PdfDocumentBuilder
    /// emits a bare /Type1 /BaseFont /Helvetica dict with no /FontFile when LoadFont was never
    /// called, which is exactly the defect font-embedded fires on — so no corpus file is needed
    /// and this runs identically on a dev machine and a Linux CI runner.
    /// </summary>
    private static byte[] UnembeddedDocument() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Wave the quick brown fox 12345", 72, 700).Font("Helvetica", 18))
            .ToByteArray();

    private static IReadOnlyList<Finding> Preflight(PdfDocument document) =>
        Preflighter.Check(document, ConformanceProfile.PdfA2b).Findings;

    [Fact]
    public void The_fixture_actually_fails_the_rule()
    {
        // Validate the fixture before trusting it. A synthetic fixture that fails for the wrong
        // reason — or does not fail at all — proves nothing, and this project has been caught by
        // exactly that before (design §11, "Fixtures").
        using PdfDocument document = PdfDocument.Load(new MemoryStream(UnembeddedDocument()));

        Assert.Contains(Preflight(document), f => f.RuleId == "font-embedded");
    }

    [Fact]
    public void Staging_and_applying_the_embed_closes_the_finding()
    {
        byte[] original = UnembeddedDocument();
        using var output = new MemoryStream();

        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            Assert.NotNull(finding.ObjectNumber);

            FontRemediationProposal proposal = new FontRemediationPlanner(SystemFontLocator.Default)
                .Propose(document, [("font-embedded", finding.ObjectNumber!.Value)]);

            var embed = proposal.Fonts.OfType<EmbedProposal>().SingleOrDefault();
            Assert.SkipWhen(embed is null,
                "No substitute for Helvetica resolved on this machine; the embed path cannot run.");

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed!.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument after = PdfDocument.Load(output);
        Assert.DoesNotContain(Preflight(after), f => f.RuleId == "font-embedded");
    }

    [Fact]
    public void Embedding_does_not_move_the_text()
    {
        // Design §11's non-regression gate, pinning §5.1. A test that only checked conformance
        // would pass a fix that embedded a font and quietly reflowed the page.
        byte[] original = UnembeddedDocument();

        List<TextFragment> before;
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
            before = document.GetPage(0)!.ExtractTextWithFragments().Fragments;

        using var output = new MemoryStream();
        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            FontRemediationProposal proposal = new FontRemediationPlanner(SystemFontLocator.Default)
                .Propose(document, [("font-embedded", finding.ObjectNumber!.Value)]);
            var embed = proposal.Fonts.OfType<EmbedProposal>().SingleOrDefault();
            Assert.SkipWhen(embed is null, "No substitute resolved on this machine.");

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed!.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument afterDocument = PdfDocument.Load(output);
        List<TextFragment> after = afterDocument.GetPage(0)!.ExtractTextWithFragments().Fragments;

        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Text, after[i].Text);
            Assert.Equal(before[i].X, after[i].X, precision: 4);
            Assert.Equal(before[i].Y, after[i].Y, precision: 4);
            Assert.Equal(before[i].Width, after[i].Width, precision: 4);
        }
    }

    [Fact]
    public void The_embedded_program_is_a_full_program_carrying_no_subset_tag()
    {
        // §1.1: F-2 embeds the FULL program, which produces no subset tag, which is what keeps
        // F-2's output outside F-3's scope by construction. A subset tag here would mean someone
        // added subsetting to the embed path and silently coupled the two increments.
        byte[] original = UnembeddedDocument();
        using var output = new MemoryStream();

        using (PdfDocument document = PdfDocument.Load(new MemoryStream(original)))
        {
            Finding finding = Preflight(document).First(f => f.RuleId == "font-embedded");
            FontRemediationProposal proposal = new FontRemediationPlanner(SystemFontLocator.Default)
                .Propose(document, [("font-embedded", finding.ObjectNumber!.Value)]);
            var embed = proposal.Fonts.OfType<EmbedProposal>().SingleOrDefault();
            Assert.SkipWhen(embed is null, "No substitute resolved on this machine.");

            using var editor = new PdfDocumentEditor(document);
            editor.EmbedProgram(embed!.Font, embed.Program, embed.Format);
            editor.Save(output);
        }

        output.Position = 0;
        using PdfDocument after = PdfDocument.Load(output);
        Assert.All(FontInventory.Read(after), entry => Assert.Null(entry.SubsetTag));
    }
}
