using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// Task 6 (tracker issue 38): the planner's per-holder merged WIDTH patch —
/// <see cref="FontRemediationPlanner.Propose(PdfDocument, System.Collections.Generic.IEnumerable{System.ValueTuple{string, int}})"/>'s
/// independent width-family grouping (parallel to <see cref="MergedReplacementTests"/>'s notdef-family
/// one), and the "proposed-only subsumption skip" that frees a DECLINED replace group's members' own
/// width findings for this arm instead of swallowing them unconditionally.
///
/// <para>Fixtures: <see cref="ReplaceProgramFixtures.SharedDescendantDoc"/> (direct sharing, same-kind
/// merge), <see cref="ReplaceProgramFixtures.SharedDescriptorDoc"/> (descriptor-level sharing, extended
/// this task with <c>wrapper1Codes</c> for a pure width-only conflict), and
/// <see cref="ReplaceProgramFixtures.SimpleFontSharingDescriptorWithCompositeSeedDoc"/> (extended this
/// task with <c>simpleFontWidth</c>/<c>wrapper1Codes</c> for the cross-kind + declined-frees-width
/// shape).</para>
/// </summary>
public sealed class MergedWidthPatchTests
{
    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        ReplaceProgramFixtures.Planner(provider);

    private static byte[] LiberationSansBytes() => ReplaceProgramFixtures.LiberationSansBytes();

    /// <summary>Two Type0 wrappers sharing ONE descendant (direct sharing), NEITHER drawing the dead
    /// code — no notdef finding exists anywhere, so this is a PURE width-only merge, independent of
    /// the notdef-family grouping entirely. Both wrappers' own 6.2.11.5 finding (attributed to the
    /// shared descendant) names the same glyph (gid 1, CID 0x41, declared 500 vs the program's actual
    /// 450) — pre-Task-6, each would independently produce its OWN PatchWidthsProposal against the
    /// SAME program stream (last-write-wins); Task 6 merges them into one.</summary>
    [Fact]
    public void Two_wrappers_sharing_one_descendant_merge_into_one_width_patch()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper1Codes: [0x41], wrapper2Codes: [0x41]);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1), ("font-program", 7)]);

        PatchWidthsProposal patch = Assert.IsType<PatchWidthsProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, patch.CoveredFonts.Count);
        Assert.Equal(new HashSet<int> { 1, 7 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
        Assert.Equal(1, patch.GlyphsPatched);
        Assert.False(patch.LeavesOtherFindings);

        var metrics = new EmbeddedFontMetrics(patch.PatchedProgram);
        Assert.Equal(500, metrics.GetAdvanceWidth(1)); // both siblings' shared glyph, now merged
    }

    /// <summary>Descriptor-level sharing (distinct descendants), NEITHER wrapper drawing its own dead
    /// code — a pure width-only conflict: descendant 1 declares CID 0x41 -> 500, descendant 2 declares
    /// CID 0x43 -> 700, and BOTH CIDs resolve to gid 1 in the shared program (the only non-.notdef
    /// glyph it has). One patched program cannot satisfy both — the merge-width-conflict decline
    /// (verbatim, per the controller brief) fires for BOTH members.</summary>
    [Fact]
    public void Conflicting_declared_widths_across_siblings_decline_both()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc(
            wrapper1Codes: [0x41], wrapper2Codes: [0x43], descendant2Width: 700);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p => Assert.Contains(
            "different widths for the same glyph", Assert.IsType<DeclineProposal>(p).Reason));
    }

    /// <summary>The "declined-replace-group-frees-width" shape combined with cross-kind merging: the
    /// composite wrapper (object 1) seeds a notdef group (it draws descendant 4's dead code 0x42); the
    /// simple TrueType font (object 30) sharing the same descriptor blocks that group from proposing
    /// (Task 4's own ruling — a simple sibling can never join a composite substitute). Pre-Task-6, the
    /// composite's OWN independently-servable width finding (CID 0x41 declared 500) was swallowed by
    /// the unconditional subsumption skip; Task 6 frees it, and — because the simple font's OWN width
    /// finding (declared <paramref name="simpleFontWidth"/>: 500, agreeing) shares the SAME glyph
    /// (gid 1) — the two merge into ONE cross-kind PatchWidthsProposal, alongside (not instead of) the
    /// group's own per-member notdef decline for each.</summary>
    [Fact]
    public void A_composite_and_simple_font_sharing_a_descriptor_merge_their_width_patch_after_the_replace_group_declines()
    {
        using PdfDocument doc =
            ReplaceProgramFixtures.SimpleFontSharingDescriptorWithCompositeSeedDoc(simpleFontWidth: 500);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1), ("font-program", 30)]);

        // The notdef group still declines for both members — Task 6 does not remove that decline, it
        // only ADDS the freed width fix alongside it.
        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.Contains(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 1);
        Assert.Contains(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 30);

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(2, patch.CoveredFonts.Count);
        Assert.Equal(new HashSet<int> { 1, 30 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
        Assert.True(patch.LeavesOtherFindings); // the composite's own notdef finding is not addressed here

        var metrics = new EmbeddedFontMetrics(patch.PatchedProgram);
        Assert.Equal(500, metrics.GetAdvanceWidth(1));
    }

    /// <summary>The "composite width-only member" shape from the ruling: wrapper 7 (descriptor-level
    /// sharing) draws ONLY its own live code (0x43, no dead code of its own) — genuinely no notdef
    /// finding — but is pulled into wrapper 1's notdef group by inventory-scoped expansion. The group
    /// declines on a DIFFERENT, unrelated ground (wrapper 7's own /ToUnicode maps its live code to a
    /// PUA codepoint the substitute cannot render — a coverage gap). Pre-Task-6, BOTH wrapper 1's and
    /// wrapper 7's own width findings were swallowed entirely by the unconditional subsumption skip;
    /// Task 6 frees BOTH (wrapper 1 keeps its own notdef finding too, but that does not exclude it —
    /// the "frees its members' width findings" ruling is unqualified by notdef status), and because
    /// they agree on the SAME shared glyph's declared width (500, the fixture's own default), they
    /// merge into ONE cross-holder patch alongside the group's own per-member decline.</summary>
    [Fact]
    public void A_width_only_composite_member_of_a_declined_group_gets_its_own_freed_patch()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc(
            wrapper2Codes: [0x43], wrapper2ToUnicode: [(0x43, "E000")]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        // The notdef group declines (coverage gap) for both members.
        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.All(result.Fonts.OfType<DeclineProposal>(), p =>
            Assert.Contains("cannot honestly render", p.Reason));

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(new HashSet<int> { 1, 7 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
    }

    /// <summary>Review round 1, finding I1 — the falsifying shape: a <c>/Subtype /Type1</c> font
    /// (object 40) sharing the SAME <c>/FontDescriptor</c> (and so the same <c>/FontFile2</c>) as two
    /// TrueType-family width-only seeds. HolderGroupKey keys on the resolved descriptor object number,
    /// never on which /FontFile* key the descriptor happens to carry, and FontKind is derived purely
    /// from /Subtype — so a Type1 entry (excluded from width-family MEMBERSHIP by the kind gate) still
    /// shares the EXACT stream a merged width patch would rewrite. Neither wrapper draws its own dead
    /// code (both would otherwise merge successfully — see
    /// <see cref="Two_wrappers_sharing_one_descendant_merge_into_one_width_patch"/>), so the blocking
    /// sibling is the ONLY thing standing between this test and a successful merge: it must decline the
    /// WHOLE group instead, and NO PatchWidthsProposal may be emitted for the holder.</summary>
    [Fact]
    public void A_mixed_kind_sibling_sharing_the_descriptor_blocks_the_width_merge()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper1Codes: [0x41], wrapper2Codes: [0x41], includeType1BlockingSibling: true);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Empty(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p => Assert.Contains(
            "cannot be included", Assert.IsType<DeclineProposal>(p).Reason));
    }
}
