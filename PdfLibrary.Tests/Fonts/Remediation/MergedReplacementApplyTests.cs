using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Content;
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
/// Per-holder-merge Task 5: <see cref="PdfDocumentEditor.ReplaceCompositeProgram"/>'s MULTI-TARGET
/// apply — the write half of Task 4's merged <see cref="ReplaceProgramProposal"/>s. Task 3 already
/// hoisted the program+descriptor writes above the target loop (one substitute program, one
/// descriptor, regardless of target count); this suite is about the per-target writes underneath
/// that hoist: exactly one <c>/CIDToGIDMap</c> write per DISTINCT descendant (targets sharing a
/// descendant — direct sharing, §6 — must carry an IDENTICAL map, asserted rather than trusted), and
/// every target's wrapper AND descendant <c>/BaseFont</c> renamed.
///
/// <para>Mirrors <see cref="ReplaceProgramApplyTests"/>' propose→apply→save→reload→re-preflight idiom
/// and <see cref="ReplaceProgramLayoutTests"/>' geometry-invariance assertion, but drives them against
/// Task 4's planner output on the two multi-target fixtures Task 1 landed:
/// <see cref="ReplaceProgramFixtures.SharedDescendantDoc"/> (direct sharing — two wrappers, ONE
/// descendant) and <see cref="ReplaceProgramFixtures.SharedDescriptorDoc"/> (descriptor-level sharing
/// — two wrappers, two DISTINCT descendants, one shared descriptor/program).</para>
/// </summary>
public sealed class MergedReplacementApplyTests
{
    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;

    private static ReplaceProgramProposal ProposeMerged(PdfDocument doc, params int[] objectNumbers)
    {
        var provider = new StubFontProvider(ReplaceProgramFixtures.LiberationSansBytes());
        FontRemediationProposal result = ReplaceProgramFixtures.Planner(provider)
            .Propose(doc, objectNumbers.Select(n => ("font-program", n)));
        return Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
    }

    // Same idiom as ReplaceProgramLayoutTests.AssertSameGeometry — copied rather than shared because
    // that one is private to its own test class.
    private static void AssertSameGeometry(IReadOnlyList<TextFragment> before, IReadOnlyList<TextFragment> after)
    {
        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Text, after[i].Text);
            Assert.Equal(before[i].X, after[i].X, precision: 4);
            Assert.Equal(before[i].Y, after[i].Y, precision: 4);
            Assert.Equal(before[i].Width, after[i].Width, precision: 4);
        }
    }

    /// <summary>
    /// The write-discipline test proper: direct sharing means BOTH targets name the same descendant
    /// (object 4), so a per-TARGET write loop (rather than a per-DISTINCT-descendant one) would call
    /// <c>RegisterObject</c> for the CIDToGIDMap stream twice, leaving the first registration an
    /// orphaned object nothing references — invisible from the descendant dictionary's own final
    /// state (last write wins, and both targets carry the identical map, so the SHAPE of the data
    /// looks right either way) but visible in the document's object count. Only the program stream
    /// (Task 3, hoisted) plus ONE map stream should be newly registered — not two.
    /// </summary>
    [Fact]
    public void SharedDescendantDoc_registers_the_shared_descendants_CIDToGIDMap_exactly_once()
    {
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        ReplaceProgramProposal proposal = ProposeMerged(doc, 1, 7);
        Assert.Equal(2, proposal.Targets.Count);
        Assert.Single(proposal.Targets.Select(t => t.Font.ObjectNumber).Distinct()); // direct sharing

        int objectsBefore = doc.Objects.Count;

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);

        int objectsAfter = doc.Objects.Count;

        Assert.Equal(2, objectsAfter - objectsBefore); // 1 program stream + 1 CIDToGIDMap stream
    }

    [Fact]
    public void SharedDescendantDoc_end_to_end_closes_the_finding_and_preserves_both_wrappers_text()
    {
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();

        var preEditMs = new MemoryStream();
        doc.Save(preEditMs);
        byte[] originalBytes = preEditMs.ToArray();
        PreflightResult before = Preflighter.Check(originalBytes, ConformanceProfile.PdfA2b);
        Assert.Contains(before.Findings, f => f.RuleId == "font-program" && f.Clause.Contains("6.2.11.8"));
        var beforeRuleIds = before.Findings.Select(f => f.RuleId).ToHashSet();

        List<TextFragment> beforeFragments = doc.GetPage(0)!.ExtractTextWithFragments().Fragments;
        Assert.NotEmpty(beforeFragments);

        ReplaceProgramProposal proposal = ProposeMerged(doc, 1, 7);
        Assert.Equal(2, proposal.Targets.Count);

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);
        var ms = new MemoryStream();
        editor.Save(ms);
        byte[] savedBytes = ms.ToArray();
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        // Both wrappers' text extracts exactly as before — a whole-face swap changes glyph SHAPES,
        // never WHERE a glyph sits or what code point it decodes to (ReplaceProgramLayoutTests' own
        // gate, exercised here across two wrappers sharing one rewritten descendant).
        List<TextFragment> afterFragments = reloaded.GetPage(0)!.ExtractTextWithFragments().Fragments;
        AssertSameGeometry(beforeFragments, afterFragments);

        // Re-preflight of the SAVED bytes: the notdef finding is gone, and nothing new appeared
        // (the same full-rule-id-set comparison ReplaceProgramApplyTests uses, so a stray
        // FontDictionaryRule regression from the shared-descendant rewrite couldn't hide).
        PreflightResult after = Preflighter.Check(savedBytes, ConformanceProfile.PdfA2b);
        Assert.DoesNotContain(after.Findings, f => f.RuleId == "font-program");
        var afterRuleIds = after.Findings.Select(f => f.RuleId).ToHashSet();
        var newRuleIds = afterRuleIds.Except(beforeRuleIds).ToArray();
        Assert.True(newRuleIds.Length == 0,
            $"New rule ids appeared after the replacement that were absent before: {string.Join(", ", newRuleIds)}");

        // Every target's wrapper AND descendant /BaseFont renamed.
        var wrapper1 = Assert.IsType<PdfDictionary>(reloaded.GetObject(1));
        var wrapper7 = Assert.IsType<PdfDictionary>(reloaded.GetObject(7));
        var descendant = Assert.IsType<PdfDictionary>(reloaded.GetObject(4));
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, wrapper1.Get("BaseFont"))).Value);
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, wrapper7.Get("BaseFont"))).Value);
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, descendant.Get("BaseFont"))).Value);
    }

    [Fact]
    public void SharedDescriptorDoc_end_to_end_closes_both_notdef_findings_and_preserves_both_wrappers_text()
    {
        PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc();

        var preEditMs = new MemoryStream();
        doc.Save(preEditMs);
        byte[] originalBytes = preEditMs.ToArray();
        PreflightResult before = Preflighter.Check(originalBytes, ConformanceProfile.PdfA2b);
        // Distinct descendants (4 and 14) sharing one descriptor/program: each wrapper draws its OWN
        // dead code (0x42 / 0x44) against the same 2-glyph substitute, so each gets its own genuine
        // 6.2.11.8 finding — reported against the WRAPPER (FontProgramRule.Make's own convention,
        // PdfFont.FontDictionary is the Type0 dictionary), i.e. objects 1 and 7.
        Assert.Contains(before.Findings,
            f => f.RuleId == "font-program" && f.ObjectNumber == 1 && f.Clause.Contains("6.2.11.8"));
        Assert.Contains(before.Findings,
            f => f.RuleId == "font-program" && f.ObjectNumber == 7 && f.Clause.Contains("6.2.11.8"));
        var beforeRuleIds = before.Findings.Select(f => f.RuleId).ToHashSet();

        List<TextFragment> beforeFragments = doc.GetPage(0)!.ExtractTextWithFragments().Fragments;
        Assert.NotEmpty(beforeFragments);

        ReplaceProgramProposal proposal = ProposeMerged(doc, 1, 7);
        Assert.Equal(2, proposal.Targets.Count);
        Assert.Equal(2, proposal.Targets.Select(t => t.Font.ObjectNumber).Distinct().Count()); // descriptor sharing: distinct descendants

        using PdfDocumentEditor editor = doc.Edit();
        editor.ReplaceCompositeProgram(proposal);
        var ms = new MemoryStream();
        editor.Save(ms);
        byte[] savedBytes = ms.ToArray();
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);

        var descendant4 = Assert.IsType<PdfDictionary>(reloaded.GetObject(4));
        var descendant14 = Assert.IsType<PdfDictionary>(reloaded.GetObject(14));

        // Both descendants resolve the SAME /FontDescriptor object — so the descriptor rewrite (the
        // hoisted §1/§4 writes) happened once/consistently for both, not via two separate writes that
        // merely happen to agree.
        var descriptorRef4 = Assert.IsType<PdfIndirectReference>(descendant4.Get("FontDescriptor"));
        var descriptorRef14 = Assert.IsType<PdfIndirectReference>(descendant14.Get("FontDescriptor"));
        Assert.Equal(descriptorRef4.ObjectNumber, descriptorRef14.ObjectNumber);

        // ONE /FontFile2 object serves both descendants: resolved independently through EACH
        // descendant's own /FontDescriptor, not assumed from the shared descriptor object number
        // above.
        var descriptorViaDescendant4 = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant4.Get("FontDescriptor")));
        var descriptorViaDescendant14 = Assert.IsType<PdfDictionary>(Resolve(reloaded, descendant14.Get("FontDescriptor")));
        var fontFile2Ref4 = Assert.IsType<PdfIndirectReference>(descriptorViaDescendant4.Get("FontFile2"));
        var fontFile2Ref14 = Assert.IsType<PdfIndirectReference>(descriptorViaDescendant14.Get("FontFile2"));
        Assert.Equal(fontFile2Ref4.ObjectNumber, fontFile2Ref14.ObjectNumber);
        var fontFile2Stream = Assert.IsType<PdfStream>(Resolve(reloaded, fontFile2Ref4));
        Assert.Equal(proposal.Program, fontFile2Stream.GetDecodedData(reloaded.Decryptor));

        // Both wrappers' text extracts exactly as before.
        List<TextFragment> afterFragments = reloaded.GetPage(0)!.ExtractTextWithFragments().Fragments;
        AssertSameGeometry(beforeFragments, afterFragments);

        // Re-preflight of the SAVED bytes shows BOTH the 0x42-class (descendant 4) and 0x44-class
        // (descendant 14) findings closed, and nothing new appeared.
        PreflightResult after = Preflighter.Check(savedBytes, ConformanceProfile.PdfA2b);
        Assert.DoesNotContain(after.Findings, f => f.RuleId == "font-program");
        var afterRuleIds = after.Findings.Select(f => f.RuleId).ToHashSet();
        var newRuleIds = afterRuleIds.Except(beforeRuleIds).ToArray();
        Assert.True(newRuleIds.Length == 0,
            $"New rule ids appeared after the replacement that were absent before: {string.Join(", ", newRuleIds)}");

        // Every target's wrapper AND descendant /BaseFont renamed.
        var wrapper1 = Assert.IsType<PdfDictionary>(reloaded.GetObject(1));
        var wrapper7 = Assert.IsType<PdfDictionary>(reloaded.GetObject(7));
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, wrapper1.Get("BaseFont"))).Value);
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, wrapper7.Get("BaseFont"))).Value);
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, descendant4.Get("BaseFont"))).Value);
        Assert.Equal(proposal.NewBaseFont, Assert.IsType<PdfName>(Resolve(reloaded, descendant14.Get("BaseFont"))).Value);
    }

    /// <summary>
    /// The guard: the planner's own direct-sharing output always carries an identical union map
    /// across targets naming the same descendant (Task 4's guarantee), but <c>ReplaceCompositeProgram</c>
    /// must not trust that blindly — <see cref="ReplaceProgramProposal"/>'s constructor is public, so a
    /// hand-built proposal can violate it. Silently picking whichever target's map wins the write
    /// order would mean one target's own glyph coverage of the shared descendant is simply discarded,
    /// with no error anywhere. Corrupting ONE target's map (still carrying the same keys, so the
    /// shapes otherwise look identical) must throw <see cref="InvalidOperationException"/> naming the
    /// shared descendant's object number.
    /// </summary>
    [Fact]
    public void Two_targets_sharing_one_descendant_with_unequal_maps_throw_naming_the_descendant()
    {
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        ReplaceProgramProposal proposal = ProposeMerged(doc, 1, 7);
        Assert.Equal(2, proposal.Targets.Count);
        Assert.Single(proposal.Targets.Select(t => t.Font.ObjectNumber).Distinct()); // both name descendant 4

        ReplaceTarget target0 = proposal.Targets[0];
        ReplaceTarget target1 = proposal.Targets[1];
        Assert.Equal(target0.Font.ObjectNumber, target1.Font.ObjectNumber);
        Assert.True(target1.CidToGid.ContainsKey(0x41));

        ReplaceTarget corruptedTarget1 = target1 with
        {
            CidToGid = new Dictionary<int, ushort>(target1.CidToGid)
            {
                [0x41] = (ushort)(target1.CidToGid[0x41] + 1),
            },
        };
        ReplaceProgramProposal badProposal = proposal with { Targets = [target0, corruptedTarget1] };

        using PdfDocumentEditor editor = doc.Edit();
        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
            () => editor.ReplaceCompositeProgram(badProposal));
        Assert.Contains(target0.Font.ObjectNumber.ToString(), ex.Message);
    }
}
