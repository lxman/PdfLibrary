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
    /// Review round 2, finding 2: REBUILT on <see cref="ReplaceProgramFixtures.SharedDescriptorDoc"/>
    /// (descriptor-level sharing), not <see cref="ReplaceProgramFixtures.SharedDescendantDoc"/> (direct
    /// sharing) — the original version of this test shared ONE descendant (4) between both wrappers,
    /// and <c>FontProgramFindingsFor</c> unions findings reported against the SHARED PROGRAM HOLDER
    /// (the descendant, per <c>FontProgramRule</c>'s own reporting convention), so wrapper 2 ended up
    /// seeing wrapper 1's OWN notdef finding too — a SECOND, ACCIDENTAL seed — and the test never
    /// actually exercised the width arm or the subsumption skip at all.
    ///
    /// <para>Under descriptor-level sharing, wrapper 1 (descendant 4) and wrapper 7 (descendant 14)
    /// have DISTINCT program holders, so their finding sets are genuinely independent: wrapper 1
    /// seeds via descendant 4's real notdef finding (it draws dead code 0x42); wrapper 7 draws ONLY
    /// the live code 0x43 on descendant 14 (dead-code-free — no notdef finding of its own) while
    /// descendant 14 declares width 500 for 0x43 against the shared program's actual 450 advance — a
    /// genuine, INDEPENDENT 6.2.11.5 finding. The merged replacement's advance patch already covers
    /// every target's declared widths (spec §4 step 4), so wrapper 7's width finding closes by
    /// construction — Propose() must not ALSO emit a separate <see cref="PatchWidthsProposal"/>
    /// against the same program stream (a last-write-wins corruption, not merely redundant).</para>
    /// </summary>
    [Fact]
    public void A_width_only_sibling_is_subsumed_by_the_merge_not_independently_patched()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc(wrapper2Codes: [0x43]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        // A single ReplaceProgramProposal — asserting Single here IS asserting no separate
        // PatchWidthsProposal was also emitted for wrapper 7's holder.
        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
        ReplaceTarget wrapper7Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        Assert.False(wrapper7Target.ClosesFinding); // non-seed: no notdef finding of its own
    }

    /// <summary>Review finding C1: wrapper 2 carries no finding in THIS call's input at all (only
    /// object 1's finding is passed) AND has no <c>/ToUnicode</c> — it is still pulled in by
    /// inventory-scoped expansion, and its own gate failure blocks the WHOLE group exactly like any
    /// other sibling's shape failure would.
    ///
    /// <para>Task 6 update: wrapper 1's own 6.2.11.5 width finding (declared 500 vs the shared
    /// program's actual 450) is no longer swallowed by the notdef group's now-conditional subsumption
    /// skip — the group DECLINED, so it frees wrapper 1's width finding for the width arm. Wrapper 2
    /// never entered <c>resolved</c> (its finding was never named in this call), so it does not join a
    /// width-family merge; wrapper 1 gets its own singleton width patch alongside the group's 2
    /// declines.</para>
    /// </summary>
    [Fact]
    public void A_gate_failing_findingless_sibling_blocks_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(wrapper2HasToUnicode: false);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1)]);

        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.All(result.Fonts.OfType<DeclineProposal>(), p =>
            Assert.Contains("cannot be included", p.Reason));

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(new HashSet<int> { 1 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
    }

    /// <summary>I5: merge-width-conflict at DESCRIPTOR level (distinct descendants). Wrapper 2's CID
    /// 0x43 is remapped to resolve to the SAME substitute glyph as wrapper 1's CID 0x41 ('A'), but
    /// descendant 2 declares a DIFFERENT width for it (700 vs descendant 1's 500) — one patched
    /// program advance cannot satisfy both.
    ///
    /// <para>Task 6 update: both wrappers draw their own dead code by default (0x42 / 0x44), so BOTH
    /// independently seed the notdef group AND both carry their own 6.2.11.5 finding — the DECLINED
    /// group now frees both for the width-family arm too, and since the SAME 500-vs-700 disagreement
    /// (genuinely beyond WidthTolerance) applies there as well, the width-family merge ALSO declines
    /// both, with the SAME (shared-constant) reason text as the notdef-merge's own width conflict.
    /// Review round 1, finding I2: that means each font would otherwise get TWO byte-identical
    /// DeclineProposal rows (one per arm) — Propose() now dedups on (Font, RuleId, Reason) right
    /// before returning, so this test measures 2 declines, not 4.</para>
    /// </summary>
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
    /// glyph for.
    ///
    /// <para>Task 6 update: wrapper 1's own width finding (declared 500 vs the shared program's actual
    /// 450, gid 1 — <c>FontProgramRule</c> attributes it to wrapper 1's own object, since wrapper 1
    /// itself draws the mismatched live code) is freed by the now-conditional subsumption skip. Wrapper
    /// 2 draws only its dead code (default wrapper2Codes [0x42]), which resolves to .notdef and is
    /// skipped by <c>ProgramWidthResolver.Composite</c>, so wrapper 2 has no width finding of its own
    /// to free — the freed patch is a SINGLETON covering only wrapper 1.</para>
    /// </summary>
    [Fact]
    public void A_coverage_gap_on_any_member_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x42, "E000")]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.All(result.Fonts.OfType<DeclineProposal>(), p =>
            Assert.Contains("cannot honestly render", p.Reason));

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(new HashSet<int> { 1 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
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

    /// <summary>Task 6 update: wrapper 1's own width finding (declared 500 vs the shared program's
    /// actual 450) is freed by the now-conditional subsumption skip once the notdef merge declines;
    /// wrapper 2 draws only the dead code (default wrapper2Codes [0x42], resolving to .notdef, skipped
    /// by the width resolver), so it contributes nothing — a freed singleton patch alongside the
    /// group's 2 declines.</summary>
    [Fact]
    public void Conflicting_tounicode_maps_decline_the_whole_group_per_member()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x0042, "005A")]);   // wrapper 2 says 0x42 -> 'Z'; wrapper 1 says 'B'
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.All(result.Fonts.OfType<DeclineProposal>(), p =>
            Assert.Contains("different characters", p.Reason));

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(new HashSet<int> { 1 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
    }

    /// <summary>Task 6 update: FontProgramRule attributes each wrapper's own finding to ITS OWN
    /// object number (<c>FontProgramRule.Make</c> reads <c>font.FontDictionary</c>, the WRAPPER being
    /// checked — never the shared descendant), scoped to codes THAT WRAPPER itself draws. Wrapper 1
    /// draws the live code (0x41) and gets its own 6.2.11.5 finding; wrapper 2 (default codes,
    /// dead-code-only) resolves 0x42 to .notdef, which <c>ProgramWidthResolver.Composite</c> skips, so
    /// wrapper 2 never has a width finding of its own to free — the width-family arm never seeds it,
    /// regardless of the notdef group's decline. Only wrapper 1's finding is freed, as a singleton
    /// patch, alongside the group's 2 declines.</summary>
    [Fact]
    public void A_sibling_without_tounicode_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(wrapper2HasToUnicode: false);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.OfType<DeclineProposal>().Count());
        Assert.All(result.Fonts.OfType<DeclineProposal>(), p =>
            Assert.Contains("cannot be included", p.Reason));

        PatchWidthsProposal patch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(new HashSet<int> { 1 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
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

    /// <summary>
    /// Review round 2, finding 1 (controller ruling): a non-composite entry sharing a group key
    /// still BLOCKS the replace group (writing a composite substitute over a shared program a simple
    /// font depends on would corrupt it) — Step 1's own gate declines the whole group on the simple
    /// font's account, exactly as any other shape failure would. But that member must NOT be
    /// swallowed by the pass-2 group-dispatch skip: its own, independently servable 6.2.11.5 width
    /// finding still routes through the ordinary per-entry dispatch (the width arm). This is safe even
    /// though the group (which the simple font is also a member of) declines: a DECLINED group writes
    /// nothing, so there is no double-writer — only the width patch ever touches the shared program.
    ///
    /// <para>Task 6 update: the composite wrapper (object 1) ALSO carries its own independently
    /// servable 6.2.11.5 finding (descendant 4 declares CID 0x41 -> 500 against the shared program's
    /// actual 450) — pre-Task-6 this was swallowed entirely by the unconditional subsumption skip;
    /// now the group's decline frees it too. Its declared 500 and the simple font's declared 507
    /// (this fixture's own default) are within <c>FontProgramRule.WidthTolerance</c> (10) of each
    /// other, so the width-family arm treats them as agreeing rather than conflicting and MERGES them
    /// into ONE cross-kind <see cref="PatchWidthsProposal"/> (holder = descendant 4, not the simple
    /// font's own object 30) — see <see cref="MergedWidthPatchTests"/> for the width-family merge's
    /// own dedicated coverage, including a wider-than-tolerance conflict variant.</para>
    /// </summary>
    [Fact]
    public void A_simple_font_sharing_the_descriptor_declines_the_group_but_keeps_its_own_width_fix()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SimpleFontSharingDescriptorWithCompositeSeedDoc();
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1), ("font-program", 30)]);

        // The composite wrapper (object 1) declines — the group cannot proceed with a simple sibling
        // sharing its program.
        DeclineProposal wrapperDecline =
            Assert.Single(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 1);
        Assert.Contains("cannot be included", wrapperDecline.Reason);

        // The simple font (object 30) also gets a group decline (it is the member the gate actually
        // fires on) — but that does not swallow its OWN, independently servable width fix, which now
        // merges with the composite's own (within-tolerance) width finding into one proposal.
        Assert.Single(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 30);
        PatchWidthsProposal mergedPatch = Assert.Single(result.Fonts.OfType<PatchWidthsProposal>());
        Assert.Equal(4, mergedPatch.Font.ObjectNumber);
        Assert.Equal(new HashSet<int> { 1, 30 }, mergedPatch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
        Assert.Equal(1, mergedPatch.GlyphsPatched);
    }

    /// <summary>
    /// Review round 2, finding 3: a NON-SEED member (pulled in only by inventory-scoped expansion,
    /// drawing no CID 0 itself) must not be told "This font draws character code 0" — that would be
    /// factually FALSE about it. Its row wraps the group's actual reason in the merge-blocked-sibling
    /// template instead, naming it as a fact about a SIBLING; the SEED (which genuinely draws only
    /// CID 0) still gets the raw, accurate reason.
    /// </summary>
    [Fact]
    public void A_non_seed_member_gets_the_wrapped_reason_not_the_raw_group_fact()
    {
        const string cid0OnlyDeclineReason =
            "This font draws character code 0, which ISO 32000 defines as .notdef regardless of what "
            + "glyph the font maps it to — no font-side fix can make that draw conformant.";

        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper1Codes: [0x00],
            wrapper2ToUnicode: [(0x41, "0041")], wrapper2Codes: [0x41]);
        FontRemediationProposal result = Planner(new StubFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1)]); // only wrapper 1's (seed) finding

        Assert.Equal(2, result.Fonts.Count);
        DeclineProposal wrapper1Decline =
            Assert.Single(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 1);
        DeclineProposal wrapper2Decline =
            Assert.Single(result.Fonts.OfType<DeclineProposal>(), p => p.Font.ObjectNumber == 7);

        Assert.Equal(cid0OnlyDeclineReason, wrapper1Decline.Reason); // raw: true about the seed itself
        Assert.NotEqual(cid0OnlyDeclineReason, wrapper2Decline.Reason); // wrapped, not raw
        Assert.Contains("cannot be included in a merged replacement", wrapper2Decline.Reason);
        Assert.Contains(cid0OnlyDeclineReason, wrapper2Decline.Reason);
    }
}
