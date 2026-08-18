using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using PdfLibrary.Tests.Fonts.Embedded;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 4: the planner's merged whole-face-replacement builder
/// (<see cref="FontRemediationPlanner.Propose(PdfLibrary.Structure.PdfDocument,System.Collections.Generic.IEnumerable{System.ValueTuple{string,int}})"/>
/// grouping font-program findings by <c>HolderGroupKey</c> and routing a multi-entry group to the
/// merged builder) — the controller-ruled replacement for the guard-era
/// <c>SharedHolderReason</c>/<c>Two_type0_wrappers_...</c> decline tests
/// (<see cref="ReplaceProgramProposalTests"/>), which now propose a MERGED
/// <see cref="ReplaceProgramProposal"/> instead of declining.
///
/// <para>Fixtures: <see cref="ReplaceProgramFixtures.SharedDescendantDoc"/> (direct sharing — one
/// descendant CIDFont) and <see cref="ReplaceProgramFixtures.SharedDescriptorDoc"/> (descriptor-level
/// sharing — distinct descendants, one shared <c>/FontDescriptor</c>), both landed by Task 1.</para>
/// </summary>
public sealed class MergedReplacementTests
{
    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        ReplaceProgramFixtures.Planner(provider);

    private static byte[] LiberationSansBytes() => ReplaceProgramFixtures.LiberationSansBytes();

    [Fact]
    public void Two_wrappers_sharing_one_descendant_merge_into_one_proposal()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
        Assert.Single(proposal.Targets.Select(t => t.Font.ObjectNumber).Distinct());
        Assert.Equal(2, proposal.Targets.Select(t => t.CompositeFont.ObjectNumber).Distinct().Count());
        // Direct sharing: both targets carry the identical UNION map — asserted against an
        // INDEPENDENT expected key set (review finding M2), not just cross-target equality, which
        // would pass even if a bug always assigned the same (wrong) map to both targets: the fixture
        // draws 0x41 and 0x42 between the two wrappers, and both are the union's keys.
        Assert.Equal(new HashSet<int> { 0x41, 0x42 }, proposal.Targets[0].CidToGid.Keys.ToHashSet());
        Assert.Equal(proposal.Targets[0].CidToGid, proposal.Targets[1].CidToGid);
        Assert.All(proposal.Targets, t => Assert.True(t.ClosesFinding));
    }

    /// <summary>
    /// Review finding C1 (spec §4, "Group membership is INVENTORY-scoped, not findings-scoped",
    /// 2026-08-18 clarification, commit 16d7585): wrapper 2 draws ONLY the LIVE code 0x41 here — no
    /// dead code, no notdef finding of its own, genuinely findingless — yet Propose() still pulls it
    /// into the group because it shares wrapper 1's descendant. Its own code must join the coverage
    /// union the merged replacement resolves (skipping it would leave its CIDToGIDMap entry
    /// unresolved, rendering .notdef with no error anywhere), and it becomes a full target with
    /// ClosesFinding false — nothing of its was asked to be fixed.
    /// </summary>
    [Fact]
    public void A_findingless_sibling_is_pulled_into_the_group_and_its_codes_join_the_coverage_union()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x41, "0041")], wrapper2Codes: [0x41]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1)]); // only wrapper 1's finding — wrapper 2 has none

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
        ReplaceTarget wrapper2Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        Assert.False(wrapper2Target.ClosesFinding);
        Assert.True(wrapper2Target.CidToGid.ContainsKey(0x41));
    }

    /// <summary>
    /// Review finding C1 (width subsumption): wrapper 2 draws ONLY the live code 0x41 — no notdef
    /// finding — but the descendant's declared width for 0x41 (500) does not match the embedded
    /// program's actual advance (450, <see cref="ZeroAdvanceSfntFixture"/>'s <c>gid1Advance</c>), so
    /// wrapper 2 independently carries its OWN 6.2.11.5 (width) finding, passed alongside wrapper 1's
    /// notdef finding. The merged replacement's advance patch already covers the declared widths for
    /// every target (spec §4 step 4), so wrapper 2's width finding closes by construction — Propose()
    /// must not ALSO emit a separate <see cref="PatchWidthsProposal"/> against the same program
    /// stream (a last-write-wins corruption, not merely redundant).
    /// </summary>
    [Fact]
    public void A_width_only_sibling_is_subsumed_by_the_merge_not_independently_patched()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x41, "0041")], wrapper2Codes: [0x41]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        // A single ReplaceProgramProposal — asserting Single here IS asserting no separate
        // PatchWidthsProposal was also emitted for wrapper 2's holder.
        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
    }

    /// <summary>Review finding C1: wrapper 2 carries no finding in THIS call's input at all (only
    /// object 1's finding is passed) AND has no <c>/ToUnicode</c> — it is still pulled in by
    /// inventory-scoped expansion, and its own gate failure blocks the WHOLE group exactly like any
    /// other sibling's shape failure would.</summary>
    [Fact]
    public void A_gate_failing_findingless_sibling_blocks_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(wrapper2HasToUnicode: false);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("cannot be included", Assert.IsType<DeclineProposal>(p).Reason));
    }

    /// <summary>I5: merge-width-conflict at DESCRIPTOR level (distinct descendants). Wrapper 2's CID
    /// 0x43 is remapped to resolve to the SAME substitute glyph as wrapper 1's CID 0x41 ('A'), but
    /// descendant 2 declares a DIFFERENT width for it (700 vs descendant 1's 500) — one patched
    /// program advance cannot satisfy both.</summary>
    [Fact]
    public void A_merge_width_conflict_at_descriptor_level_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc(
            wrapper2ToUnicode: [(0x43, "0041"), (0x44, "0044")], descendant2Width: 700);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("declare different widths", Assert.IsType<DeclineProposal>(p).Reason));
    }

    /// <summary>I5: a coverage gap on ANY member declines the WHOLE group — wrapper 2's own
    /// <c>/ToUnicode</c> maps its dead code to a Private Use Area codepoint the substitute has no
    /// glyph for.</summary>
    [Fact]
    public void A_coverage_gap_on_any_member_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x42, "E000")]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("cannot honestly render", Assert.IsType<DeclineProposal>(p).Reason));
    }

    /// <summary>I5: every member draws CID 0 (and nothing else) — none can ever close (issue 40), so
    /// the group declines <c>Cid0OnlyDeclineReason</c> for every member.</summary>
    [Fact]
    public void All_members_drawing_cid_zero_decline_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper1Codes: [0x00], wrapper2Codes: [0x00],
            wrapper2ToUnicode: [(0x00, "0043")]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("character code 0", Assert.IsType<DeclineProposal>(p).Reason));
    }

    [Fact]
    public void Two_descendants_sharing_one_descriptor_merge_with_per_target_maps()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc();
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Select(t => t.Font.ObjectNumber).Distinct().Count());
        Assert.NotEqual(proposal.Targets[0].CidToGid.Keys.Order(),
                        proposal.Targets[1].CidToGid.Keys.Order());
        Assert.All(proposal.Targets, t => Assert.True(t.ClosesFinding));
    }

    [Fact]
    public void Conflicting_tounicode_maps_decline_the_whole_group_per_member()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x0042, "005A")]);   // wrapper 2 says 0x42 -> 'Z'; wrapper 1 says 'B'
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("different characters", Assert.IsType<DeclineProposal>(p).Reason));
    }

    [Fact]
    public void A_sibling_without_tounicode_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(wrapper2HasToUnicode: false);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p =>
            Assert.Contains("cannot be included", Assert.IsType<DeclineProposal>(p).Reason));
    }

    /// <summary>
    /// Task 3 amendment (spec §6, controller ruling, commit 905bae1): a group MEMBER that draws CID 0
    /// no longer blocks the whole group — only that member's own 6.2.11.8 finding survives
    /// (<see cref="ReplaceTarget.ClosesFinding"/> false). Wrapper 2 draws CID 0 alongside its own
    /// dead code (0x42); wrapper 1 draws neither. A mixed group where at least one member closes
    /// still PROPOSES.
    /// </summary>
    [Fact]
    public void A_member_drawing_cid_zero_does_not_close_but_the_group_still_proposes()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x0000, "0043"), (0x0042, "0042")],
            wrapper2Codes: [0x0000, 0x0042]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);

        ReplaceTarget wrapper1Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 1);
        ReplaceTarget wrapper2Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        Assert.True(wrapper1Target.ClosesFinding, "wrapper 1 draws no CID 0, so its finding closes");
        Assert.False(wrapper2Target.ClosesFinding, "wrapper 2 draws CID 0, so its finding never closes");
    }
}
