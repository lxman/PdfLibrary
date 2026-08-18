using System.Linq;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
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
        // Direct sharing: both targets carry the identical UNION map.
        Assert.Equal(proposal.Targets[0].CidToGid, proposal.Targets[1].CidToGid);
        Assert.All(proposal.Targets, t => Assert.True(t.ClosesFinding));
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
