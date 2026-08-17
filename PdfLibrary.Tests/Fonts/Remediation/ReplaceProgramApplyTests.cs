using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 6: <see cref="PdfDocumentEditor.ReplaceCompositeProgram"/> — the write half of the
/// whole-face swap — and the close-by-construction gate (spec §3 gate 1): propose → apply → save →
/// reload → a full preflight reports zero font-program findings of ANY sub-clause, and no rule id
/// the CIDSystemInfo/descriptor rewrites graze (the 6.2.11.1 / 6.2.11.3.1 territory owned by
/// <c>FontDictionaryRule</c>) newly appears. Shares <see cref="ReplaceProgramFixtures"/> with
/// <see cref="ReplaceProgramProposalTests"/> (Task 5) so the two suites cannot silently diverge on
/// the document shape the gate depends on.
/// </summary>
public sealed class ReplaceProgramApplyTests
{
    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

    private static ReplaceProgramProposal ProposeReplacement(PdfDocument doc)
    {
        var provider = new StubFontProvider(ReplaceProgramFixtures.LiberationSansBytes());
        FontRemediationProposal result =
            ReplaceProgramFixtures.Planner(provider).Propose(doc, [("font-program", 1)]);
        return Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
    }

    [Fact]
    public void Applying_a_replacement_rewrites_the_whole_descendant_chain()
    {
        PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();
        ReplaceProgramProposal proposal = ProposeReplacement(doc);

        // Captured BEFORE doc.Edit() — Edit() mutates the SAME in-memory objects, so reading
        // "original" values afterward would compare the edited document against itself and pass
        // even if ReplaceCompositeProgram touched /W or /ToUnicode.
        var preEditDescendant = Assert.IsType<PdfDictionary>(doc.GetObject(proposal.Font.ObjectNumber));
        string? originalW = preEditDescendant.Get("W")?.ToPdfString();
        var preEditWrapper = Assert.IsType<PdfDictionary>(doc.GetObject(proposal.CompositeFont.ObjectNumber));
        var preEditToUnicode = Assert.IsType<PdfStream>(Resolve(doc, preEditWrapper.Get("ToUnicode")));
        byte[] originalToUnicodeBytes = preEditToUnicode.GetDecodedData(doc.Decryptor);

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);

        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        var wrapperDict = Assert.IsType<PdfDictionary>(reloaded.GetObject(proposal.CompositeFont.ObjectNumber));
        var descendant = Assert.IsType<PdfDictionary>(reloaded.GetObject(proposal.Font.ObjectNumber));
        var descriptor = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant.Get("FontDescriptor")));

        // Descendant /Subtype == CIDFontType2.
        var subtype = Assert.IsType<PdfName>(Resolve(reloaded, descendant.Get("Subtype")));
        Assert.Equal("CIDFontType2", subtype.Value);

        // /CIDToGIDMap is a stream whose decoded bytes == CidReplacementMap.ToStreamBytes(...).
        var cidToGidStream = Assert.IsType<PdfStream>(Resolve(reloaded, descendant.Get("CIDToGIDMap")));
        byte[] expectedCidToGid = CidReplacementMap.ToStreamBytes(proposal.CidToGid, proposal.MaxCid);
        Assert.Equal(expectedCidToGid, cidToGidStream.GetDecodedData(reloaded.Decryptor));

        // /CIDSystemInfo Registry "Adobe" / Ordering "Identity" / Supplement 0.
        var cidSystemInfo = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant.Get("CIDSystemInfo")));
        var registry = Assert.IsType<PdfString>(Resolve(reloaded, cidSystemInfo.Get("Registry")));
        Assert.Equal("Adobe", registry.Value);
        var ordering = Assert.IsType<PdfString>(Resolve(reloaded, cidSystemInfo.Get("Ordering")));
        Assert.Equal("Identity", ordering.Value);
        var supplement = Assert.IsType<PdfInteger>(Resolve(reloaded, cidSystemInfo.Get("Supplement")));
        Assert.Equal(0, supplement.Value);

        // /BaseFont == proposal.NewBaseFont on BOTH wrapper and descendant.
        var wrapperBaseFont = Assert.IsType<PdfName>(Resolve(reloaded, wrapperDict.Get("BaseFont")));
        Assert.Equal(proposal.NewBaseFont, wrapperBaseFont.Value);
        var descendantBaseFont = Assert.IsType<PdfName>(Resolve(reloaded, descendant.Get("BaseFont")));
        Assert.Equal(proposal.NewBaseFont, descendantBaseFont.Value);

        // Descriptor /FontName == proposal.NewBaseFont.
        var fontName = Assert.IsType<PdfName>(Resolve(reloaded, descriptor.Get("FontName")));
        Assert.Equal(proposal.NewBaseFont, fontName.Value);

        // /FontFile2 decodes to proposal.Program; /FontFile3 absent.
        var fontFile2 = Assert.IsType<PdfStream>(Resolve(reloaded, descriptor.Get("FontFile2")));
        Assert.Equal(proposal.Program, fontFile2.GetDecodedData(reloaded.Decryptor));
        Assert.Null(descriptor.Get("FontFile3"));

        // /CIDSet absent.
        Assert.Null(descriptor.Get("CIDSet"));

        // /Flags == proposal.DescriptorFlags.
        var flags = Assert.IsType<PdfInteger>(Resolve(reloaded, descriptor.Get("Flags")));
        Assert.Equal(proposal.DescriptorFlags, flags.Value);

        // /W and /ToUnicode byte-unchanged from the PRE-EDIT document (captured above, before
        // doc.Edit() mutated the original objects in place).
        Assert.Equal(originalW, descendant.Get("W")?.ToPdfString());

        var reloadedToUnicode = Assert.IsType<PdfStream>(Resolve(reloaded, wrapperDict.Get("ToUnicode")));
        Assert.Equal(originalToUnicodeBytes, reloadedToUnicode.GetDecodedData(reloaded.Decryptor));
    }

    [Fact]
    public void A_replaced_document_closes_notdef_and_creates_no_width_finding()
    {
        // THE close-by-construction gate (spec §3 gate 1 — the compose step is load-bearing): full
        // preflight BEFORE and AFTER, comparing the FULL rule-id sets — not just font-program — so
        // the CIDSystemInfo/descriptor rewrites this operation makes cannot quietly trip
        // FontDictionaryRule's 6.2.11.1 / 6.2.11.3.1 territory without the test noticing.
        //
        // BOTH checks run against SAVED BYTES, not one in-memory and one round-tripped: a rule that
        // fires only on the hand-built in-memory form (e.g. something the save/load pass normalises)
        // would otherwise inflate beforeRuleIds and could silently absorb a genuinely new finding.
        PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();
        var preEditMs = new MemoryStream();
        doc.Save(preEditMs);
        byte[] originalBytes = preEditMs.ToArray();

        PreflightResult before = Preflighter.Check(originalBytes, ConformanceProfile.PdfA2b);
        Assert.Contains(before.Findings, f => f.RuleId == "font-program" && f.Clause.Contains("6.2.11.8"));
        var beforeRuleIds = before.Findings.Select(f => f.RuleId).ToHashSet();

        ReplaceProgramProposal proposal = ProposeReplacement(doc);
        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);
        var ms = new MemoryStream();
        editor.Save(ms);
        byte[] savedBytes = ms.ToArray();

        PreflightResult after = Preflighter.Check(savedBytes, ConformanceProfile.PdfA2b);
        var afterRuleIds = after.Findings.Select(f => f.RuleId).ToHashSet();

        Assert.DoesNotContain(after.Findings, f => f.RuleId == "font-program");
        var newRuleIds = afterRuleIds.Except(beforeRuleIds).ToArray();
        Assert.True(newRuleIds.Length == 0,
            $"New rule ids appeared after the replacement that were absent before: {string.Join(", ", newRuleIds)}");
    }

    [Fact]
    public void A_cid0_replacement_removes_fontfile3()
    {
        PdfDocument doc = ReplaceProgramFixtures.DeadCid0Doc();
        ReplaceProgramProposal proposal = ProposeReplacement(doc);

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);

        var ms = new MemoryStream();
        editor.Save(ms);
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        var descendant = Assert.IsType<PdfDictionary>(reloaded.GetObject(proposal.Font.ObjectNumber));
        var descriptor = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant.Get("FontDescriptor")));

        Assert.Null(descriptor.Get("FontFile3"));
        var fontFile2 = Assert.IsType<PdfStream>(Resolve(reloaded, descriptor.Get("FontFile2")));
        Assert.Equal(proposal.Program, fontFile2.GetDecodedData(reloaded.Decryptor));

        var subtype = Assert.IsType<PdfName>(Resolve(reloaded, descendant.Get("Subtype")));
        Assert.Equal("CIDFontType2", subtype.Value);
    }

    [Fact]
    public void A_non_truetype_proposal_is_rejected_rather_than_written_as_sfnt()
    {
        // A hand-built ReplaceProgramProposal (its ctor is public) carrying a non-TrueType Format
        // must be refused, not silently written into /FontFile2 as if it were valid sfnt bytes —
        // ReplaceCompositeProgram only ever writes a TrueType program there. The planner itself
        // never produces this shape (ReplaceProgramProposalTests covers that decline), so this
        // exercises the editor's own backstop directly.
        PdfDocument doc = ReplaceProgramFixtures.DeadCid2Doc();
        ReplaceProgramProposal proposal = ProposeReplacement(doc);
        ReplaceProgramProposal badProposal = proposal with { Format = FontProgramFormat.CidFontType0C };

        using PdfDocumentEditor editor = doc.Edit();
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => editor.ReplaceCompositeProgram(badProposal));
        Assert.Equal("proposal", ex.ParamName);
    }
}
