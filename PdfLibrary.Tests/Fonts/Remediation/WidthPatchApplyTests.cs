using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4a Task 4: <see cref="PdfDocumentEditor.ReplaceProgramBytes"/> — the write half of the width
/// patch — and the close-by-construction gate (spec §2 gate 1): propose → apply → save → reload →
/// <see cref="FontProgramRule"/> reports zero findings of ANY font-program sub-clause. Shares
/// <see cref="WidthPatchFixtures"/> with <see cref="WidthPatchProposalTests"/> (Task 3) so the two
/// suites cannot diverge on the document shape the gate depends on.
/// </summary>
public sealed class WidthPatchApplyTests
{
    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

    private static Finding[] RuleFindings(PdfDocument doc) =>
        new FontProgramRule().Check(new ConformanceContext(doc, ConformanceProfile.PdfA2b)).ToArray();

    [Fact]
    public void Replace_writes_a_new_fontfile2_and_touches_nothing_else()
    {
        PdfDocument doc = WidthPatchFixtures.MismatchDoc();
        byte[] replacement = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 507); // distinct from the 450 the fixture embeds
        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceProgramBytes(new FontId(1), replacement);

        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        var fontDict = Assert.IsType<PdfDictionary>(reloaded.GetObject(1));

        // /Subtype untouched.
        var subtype = Assert.IsType<PdfName>(Resolve(reloaded, fontDict.Get("Subtype")));
        Assert.Equal("TrueType", subtype.Value);

        // /Widths untouched.
        var widths = Assert.IsType<PdfArray>(Resolve(reloaded, fontDict.Get("Widths")));
        Assert.Single(widths);
        Assert.Equal(507, widths[0].ToDouble(), 0);

        var descriptor = Assert.IsType<PdfDictionary>(Resolve(reloaded, fontDict.Get("FontDescriptor")));

        // Descriptor metric entries the operation must not touch: still exactly what the fixture wrote.
        var descType = Assert.IsType<PdfName>(Resolve(reloaded, descriptor.Get("Type")));
        Assert.Equal("FontDescriptor", descType.Value);
        var fontName = Assert.IsType<PdfName>(Resolve(reloaded, descriptor.Get("FontName")));
        Assert.Equal("ABCDEE+ZeroAdvance", fontName.Value);
        var flags = Assert.IsType<PdfInteger>(Resolve(reloaded, descriptor.Get("Flags")));
        Assert.Equal(32, flags.Value);

        // /FontFile2: new bytes, byte-equal to the replacement.
        var fontFile2 = Assert.IsType<PdfStream>(Resolve(reloaded, descriptor.Get("FontFile2")));
        byte[] decoded = fontFile2.GetDecodedData(reloaded.Decryptor);
        Assert.Equal(replacement, decoded);
    }

    [Fact]
    public void Replace_without_an_existing_fontfile2_throws()
    {
        // The fixture minus the /FontFile2 entry: the planner never proposes a patch for a font
        // without one (ProposeWidthPatch declines it), so this exercises the backstop directly.
        PdfDocument doc = WidthPatchFixtures.MismatchDoc();
        var descriptor = Assert.IsType<PdfDictionary>(doc.GetObject(2));
        descriptor.Remove(new PdfName("FontFile2"));

        using PdfDocumentEditor editor = doc.Edit();
        Assert.Throws<InvalidOperationException>(
            () => editor.ReplaceProgramBytes(new FontId(1), ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 507)));
    }

    [Fact]
    public void Propose_apply_save_reload_closes_the_width_finding()
    {
        // THE close-by-construction gate (spec §2 gate 1):
        PdfDocument doc = WidthPatchFixtures.MismatchDoc();
        Assert.Single(RuleFindings(doc)); // 6.2.11.5 present before
        FontRemediationProposal proposal = WidthPatchFixtures.Planner().Propose(doc, [("font-program", 1)]);
        PatchWidthsProposal patch = Assert.IsType<PatchWidthsProposal>(Assert.Single(proposal.Fonts));
        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceProgramBytes(patch.Font, patch.PatchedProgram);
        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Empty(RuleFindings(reloaded)); // no font-program finding of ANY sub-clause
    }

    /// <summary>
    /// Tracker issue 45 was filed on the premise that two descriptors sharing one input
    /// <c>/FontFile2</c> stream could clobber each other's repairs. The write operation has always
    /// registered a new stream and changed only the selected descriptor's reference, so the opposite
    /// is true: the descriptors can safely diverge when their declared advances differ. Grouping them
    /// as one physical holder would turn these two independently valid repairs into a false
    /// merge-width-conflict decline.
    /// </summary>
    [Fact]
    public void Two_descriptors_sharing_the_input_stream_apply_independent_width_patches_without_clobbering()
    {
        PdfDocument doc = ReplaceProgramFixtures.SharedProgramStreamDoc(
            descendant2Width: 700, wrapper1Codes: [0x41], wrapper2Codes: [0x43]);
        FontRemediationProposal proposal = ReplaceProgramFixtures.Planner()
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);
        PatchWidthsProposal[] patches = proposal.Fonts.OfType<PatchWidthsProposal>().ToArray();
        Assert.Equal(2, patches.Length);
        Assert.Empty(proposal.Fonts.OfType<DeclineProposal>());

        using PdfDocumentEditor editor = doc.Edit();
        foreach (PatchWidthsProposal patch in patches)
            editor.ReplaceProgramBytes(patch.Font, patch.PatchedProgram);

        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        var descendant4 = Assert.IsType<PdfDictionary>(reloaded.GetObject(4));
        var descendant14 = Assert.IsType<PdfDictionary>(reloaded.GetObject(14));
        var descriptor2 = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant4.Get("FontDescriptor")));
        var descriptor17 = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant14.Get("FontDescriptor")));
        var programRef2 = Assert.IsType<PdfIndirectReference>(descriptor2.Get("FontFile2"));
        var programRef17 = Assert.IsType<PdfIndirectReference>(descriptor17.Get("FontFile2"));
        Assert.NotEqual(programRef2.ObjectNumber, programRef17.ObjectNumber);

        var program2 = Assert.IsType<PdfStream>(Resolve(reloaded, programRef2));
        var program17 = Assert.IsType<PdfStream>(Resolve(reloaded, programRef17));
        Assert.Equal(500, new EmbeddedFontMetrics(program2.GetDecodedData(reloaded.Decryptor)).GetAdvanceWidth(1));
        Assert.Equal(700, new EmbeddedFontMetrics(program17.GetDecodedData(reloaded.Decryptor)).GetAdvanceWidth(1));
        Assert.Empty(RuleFindings(reloaded));
    }
}
