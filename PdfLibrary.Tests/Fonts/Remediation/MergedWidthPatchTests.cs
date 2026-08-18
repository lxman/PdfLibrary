using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Fonts;
using PdfLibrary.Tests.Fonts.Embedded;
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

    /// <summary>
    /// Task 8b (review finding I3), defect A pin — "the width merge is dead in production." The
    /// production path (<c>RemediationRunner.StageDomainZeroDecision</c>, Pellucid) calls
    /// <c>Propose</c> with a SINGLE-element findings list, one finding at a time. Pre-fix, the
    /// width-family grouping loop only added entries carrying a finding THIS CALL named — a
    /// single-finding call could never form a group larger than one, the <c>members.Count &gt; 1</c>
    /// gate in pass 2 always failed, and the merge fell back to a singleton patch covering only the
    /// seed. This reuses <see cref="Two_wrappers_sharing_one_descendant_merge_into_one_width_patch"/>'s
    /// own fixture and expectation verbatim, changing only ONE thing — wrapper 7's finding is never
    /// named — to isolate exactly the defect the merge's inventory-scoped expansion (Task 4's own C1
    /// fix, now shared by the width family too) closes.
    /// </summary>
    [Fact]
    public void A_single_finding_still_merges_with_a_findingless_same_holder_sibling()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper1Codes: [0x41], wrapper2Codes: [0x41]);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);

        PatchWidthsProposal patch = Assert.IsType<PatchWidthsProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, patch.CoveredFonts.Count);
        Assert.Equal(new HashSet<int> { 1, 7 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());
    }

    /// <summary>
    /// Task 8b (review finding I3), defect B pin — the CORRUPTION shape: a same-kind, addressable
    /// sibling sharing the holder but carrying no finding IN THIS CALL must still have its OWN
    /// declared widths honoured by the merge, not just the seed's. Uses
    /// <see cref="TwoLiveGlyphsSharedDescendantDoc"/> — a program with TWO real glyphs, not one — so
    /// wrapper 1 (gid 1) and wrapper 7 (gid 2) each have their own mismatch on a glyph the OTHER font
    /// never touches: no possible cross-member conflict, and no coincidence can explain a passing
    /// assertion. Pre-fix, the singleton patch built from wrapper 1's own used codes alone never
    /// touches gid 2 at all — wrapper 7's declared width goes unhonoured with no error anywhere,
    /// exactly the corruption Task 4's C1 finding closed for the notdef family. Asserted on the
    /// PATCHED PROGRAM BYTES, not proposal metadata, per the brief.
    /// </summary>
    [Fact]
    public void A_single_finding_still_honours_a_findingless_siblings_own_declared_width()
    {
        using PdfDocument doc = TwoLiveGlyphsSharedDescendantDoc();
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);

        PatchWidthsProposal patch = Assert.IsType<PatchWidthsProposal>(Assert.Single(result.Fonts));
        Assert.Equal(new HashSet<int> { 1, 7 }, patch.CoveredFonts.Select(f => f.ObjectNumber).ToHashSet());

        var metrics = new EmbeddedFontMetrics(patch.PatchedProgram);
        Assert.Equal(500, metrics.GetAdvanceWidth(1)); // the seed's (wrapper 1) own glyph
        Assert.Equal(650, metrics.GetAdvanceWidth(2)); // the findingless sibling's (wrapper 7) own glyph
    }

    /// <summary>
    /// Task 8b (review finding I3), interaction gate: a genuine width CONFLICT between the seed and
    /// an EXPANSION-ONLY sibling (no finding of its own named this call) still declines the WHOLE
    /// group all-or-nothing — never a partial merge, never an arbitrary pick. Same fixture and
    /// expectation as <see cref="Conflicting_declared_widths_across_siblings_decline_both"/>, but with
    /// only wrapper 1's finding named — proving the conflict check still runs (and still wins over a
    /// silent write) even though wrapper 7 was pulled in purely by inventory-scoped expansion, not
    /// because this call asked about it.
    /// </summary>
    [Fact]
    public void An_expansion_only_sibling_in_genuine_conflict_declines_the_whole_group()
    {
        using PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc(
            wrapper1Codes: [0x41], wrapper2Codes: [0x43], descendant2Width: 700);
        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);

        Assert.Equal(2, result.Fonts.Count);
        Assert.All(result.Fonts, p => Assert.Contains(
            "different widths for the same glyph", Assert.IsType<DeclineProposal>(p).Reason));
    }

    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    /// <summary>Task 8b's own fixture: unlike every other document in this file (all built over
    /// <see cref="ZeroAdvanceSfntFixture.FontBytes"/>'s 2-glyph program — gid 0 .notdef, gid 1 the
    /// ONLY real glyph), the shared program here has TWO real glyphs. Every existing width fixture
    /// funnels any two siblings' width claims onto the SAME gid 1, so two members can only ever
    /// AGREE (indistinguishable from a singleton fix) or CONFLICT (correctly declined) — there is no
    /// way, over a 1-glyph program, to prove a merge actually INCORPORATED a sibling's own
    /// contribution rather than coincidentally producing the right bytes anyway. Direct sharing (one
    /// descendant, object 4): CID 0x41 -&gt; gid 1 (declared 500, program 450), CID 0x43 -&gt; gid 2
    /// (declared 650, program 600). Wrapper 1 (object 1) draws ONLY 0x41; wrapper 7 (object 7) draws
    /// ONLY 0x43 — each wrapper's mismatch lives on a glyph the OTHER wrapper never touches, so the
    /// two can never conflict, only merge.</summary>
    private static PdfDocument TwoLiveGlyphsSharedDescendantDoc()
    {
        byte[] Hmtx()
        {
            var b = new List<byte>();
            void U16(int v) { b.Add((byte)(v >> 8)); b.Add((byte)v); }
            U16(500); U16(0); // gid 0 (.notdef)
            U16(450); U16(0); // gid 1 — wrapper 1's own live glyph
            U16(600); U16(0); // gid 2 — wrapper 7's own live glyph
            return b.ToArray();
        }

        byte[] CidToGid(int max, params (int Cid, ushort Gid)[] entries)
        {
            var bytes = new byte[(max + 1) * 2];
            foreach ((int cid, ushort gid) in entries)
            {
                bytes[cid * 2] = (byte)(gid >> 8);
                bytes[cid * 2 + 1] = (byte)gid;
            }
            return bytes;
        }

        byte[] font = MinimalSfnt.Build(
            ("head", ZeroAdvanceSfntFixture.Head()),
            ("maxp", ZeroAdvanceSfntFixture.Maxp(3)),
            ("hhea", ZeroAdvanceSfntFixture.Hhea(3)),
            ("hmtx", Hmtx()),
            ("cmap", ZeroAdvanceSfntFixture.CmapMacFormat6()),
            ("glyf", new byte[4]));

        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+TwoLiveGlyphs"),
            [N("Flags")] = new PdfInteger(4), // symbolic
            [N("FontFile2")] = Ref(3),
        });

        doc.AddObject(6, 0, new PdfStream(new PdfDictionary(),
            CidToGid(0x43, (0x41, 1), (0x43, 2))));
        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+TwoLiveGlyphs"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = Ref(6),
            [N("DW")] = new PdfInteger(1000),
            [N("W")] = new PdfArray(
                new PdfInteger(0x41), new PdfArray(new PdfInteger(500)),
                new PdfInteger(0x43), new PdfArray(new PdfInteger(650))),
            [N("CIDSystemInfo")] = new PdfDictionary
            {
                [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
                [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
                [N("Supplement")] = new PdfInteger(0),
            },
        };
        doc.AddObject(4, 0, descendant);

        doc.AddObject(5, 0, new PdfStream(new PdfDictionary(),
            ReplaceProgramFixtures.BfCharBytes([(0x41, "0041")])));
        doc.AddObject(1, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+TwoLiveGlyphs"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
            [N("ToUnicode")] = Ref(5),
        });

        doc.AddObject(8, 0, new PdfStream(new PdfDictionary(),
            ReplaceProgramFixtures.BfCharBytes([(0x43, "0043")])));
        doc.AddObject(7, 0, new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+TwoLiveGlyphs"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
            [N("ToUnicode")] = Ref(8),
        });

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0041> Tj /F1 12 Tf <0043> Tj ET")));
        WidthPatchFixtures.AddSinglePageCatalog(doc, font1: 1, font2: 7);
        return doc;
    }
}
