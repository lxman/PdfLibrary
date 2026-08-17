using System;
using System.Collections.Generic;
using System.Text;
using CffTestFixtures;
using PdfLibrary.Conformance.Rules;
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
/// F-4b Task 5: <see cref="ReplaceProgramProposal"/>, the planner's whole-face-swap arm
/// (<see cref="FontRemediationPlanner.ProposeProgramReplace"/>, dispatched from
/// <see cref="FontRemediationPlanner.ProposeWidthPatch"/> whenever a font-program finding's clause is
/// 6.2.11.8), and its manual counterpart <see cref="FontRemediationPlanner.AssessReplacementCandidate"/>.
///
/// <para>Fixtures are Type0/CIDFontType2 (or Type0/CIDFontType0) documents assembled the
/// <c>ResolveGlyphIdCid2OttoTests.BuildCid2OttoFont</c> way — hand-built objects, no
/// <c>TestFixtures.Path(...)</c> helper. The shared CID2 fixture's program is
/// <see cref="ZeroAdvanceSfntFixture"/>'s TrueType builder (2 glyphs only), an Identity
/// /CIDToGIDMap, and a /ToUnicode CMap that turns the dead CID 0 into a real character —
/// the mechanism spec §3 relies on for a dead code with no other honest source of truth.</para>
/// </summary>
public sealed class ReplaceProgramProposalTests
{
    private static PdfName N(string s) => new(s);
    private static PdfIndirectReference Ref(int n) => new(n, 0);

    private static FontRemediationPlanner Planner(ISystemFontProvider? provider = null) =>
        ReplaceProgramFixtures.Planner(provider);

    private static byte[] LiberationSansBytes() => ReplaceProgramFixtures.LiberationSansBytes();

    private static byte[] BfCharBytes(IReadOnlyList<(int Code, string Hex)> entries) =>
        ReplaceProgramFixtures.BfCharBytes(entries);

    /// <summary>See <see cref="ReplaceProgramFixtures.DeadCid2Doc"/> — shared with Task 6's apply
    /// tests so the two suites cannot silently diverge on the document shape the gate relies on.</summary>
    private static PdfDocument DeadCid2Doc(
        IReadOnlyList<(int Code, string Hex)>? toUnicodeEntries = null,
        bool includeToUnicode = true,
        string contentHex = "0000 0041") =>
        ReplaceProgramFixtures.DeadCid2Doc(toUnicodeEntries, includeToUnicode, contentHex);

    /// <summary>See <see cref="ReplaceProgramFixtures.DeadCid0Doc"/>.</summary>
    private static PdfDocument DeadCid0Doc() => ReplaceProgramFixtures.DeadCid0Doc();

    /// <summary>Local to <see cref="TwoWrappersSharedHolderDoc"/> only — the two shared-fixture
    /// documents build their own <c>/CIDSystemInfo</c> internally now.</summary>
    private static void AddCidSystemInfo(PdfDictionary descendant)
    {
        descendant[N("CIDSystemInfo")] = new PdfDictionary
        {
            [N("Registry")] = new PdfString(Encoding.ASCII.GetBytes("Adobe")),
            [N("Ordering")] = new PdfString(Encoding.ASCII.GetBytes("Identity")),
            [N("Supplement")] = new PdfInteger(0),
        };
    }

    /// <summary>Two Type0 wrappers (objects 1 and 7) sharing ONE descendant CIDFont (object 4) — the
    /// <c>ProgramHolderId != Id</c> composite fixture where two logical fonts share a program holder,
    /// making the controller's issue-38 guard reachable through <c>Propose</c> for real (previously
    /// untested per program memory). Only wrapper 1 draws anything; wrapper 2 exists purely as a
    /// sibling in the SAME page's font resources, which is all <c>FontInventory.Read</c> needs to see
    /// it (a resource-presence walk, not a usage walk).</summary>
    private static PdfDocument TwoWrappersSharedHolderDoc()
    {
        byte[] font = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);
        var doc = new PdfDocument();
        doc.AddObject(3, 0, new PdfStream(
            new PdfDictionary { [N("Length1")] = new PdfInteger(font.Length) }, font));
        doc.AddObject(2, 0, new PdfDictionary
        {
            [N("Type")] = N("FontDescriptor"),
            [N("FontName")] = N("ABCDEF+Shared"),
            [N("Flags")] = new PdfInteger(4),
            [N("FontFile2")] = Ref(3),
        });
        var descendant = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("CIDFontType2"),
            [N("BaseFont")] = N("ABCDEF+Shared"),
            [N("FontDescriptor")] = Ref(2),
            [N("CIDToGIDMap")] = N("Identity"),
            [N("DW")] = new PdfInteger(1000),
        };
        AddCidSystemInfo(descendant);
        doc.AddObject(4, 0, descendant);

        doc.AddObject(6, 0, new PdfStream(new PdfDictionary(), BfCharBytes([(0x0000, "0041")])));

        var wrapper1 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+Shared"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
            [N("ToUnicode")] = Ref(6),
        };
        doc.AddObject(1, 0, wrapper1);

        var wrapper2 = new PdfDictionary
        {
            [N("Type")] = N("Font"),
            [N("Subtype")] = N("Type0"),
            [N("BaseFont")] = N("ABCDEF+Shared"),
            [N("Encoding")] = N("Identity-H"),
            [N("DescendantFonts")] = new PdfArray(Ref(4)),
        };
        doc.AddObject(7, 0, wrapper2);

        doc.AddObject(11, 0, new PdfStream(new PdfDictionary(),
            Encoding.ASCII.GetBytes("BT /F0 12 Tf <0000> Tj ET")));
        doc.AddObject(22, 0, new PdfDictionary
        {
            [N("Type")] = N("Page"),
            [N("Parent")] = Ref(21),
            [N("Contents")] = Ref(11),
            [N("Resources")] = new PdfDictionary
            {
                [N("Font")] = new PdfDictionary { [N("F0")] = Ref(1), [N("F1")] = Ref(7) },
            },
        });
        doc.AddObject(21, 0, new PdfDictionary
        {
            [N("Type")] = N("Pages"),
            [N("Kids")] = new PdfArray(Ref(22)),
            [N("Count")] = new PdfInteger(1),
        });
        doc.AddObject(20, 0, new PdfDictionary { [N("Type")] = N("Catalog"), [N("Pages")] = Ref(21) });
        doc.Trailer.Dictionary[N("Root")] = Ref(20);
        return doc;
    }

    // ── automatic path (Propose) ────────────────────────────────────────────────────────────────

    [Fact]
    public void A_dead_cid_type0_font_gets_a_replacement_proposal()
    {
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(4, proposal.Font.ObjectNumber);           // descendant holder
        Assert.Equal(1, proposal.CompositeFont.ObjectNumber);  // Type0 wrapper
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
        Assert.True(proposal.CidToGid.TryGetValue(0x0000, out ushort gid0) && gid0 != 0);
        Assert.True(proposal.CidToGid.TryGetValue(0x0041, out ushort gid41) && gid41 != 0);
        Assert.Equal(1, proposal.RestoredCodeCount); // only CID 0 was .notdef in the OLD program
        Assert.DoesNotContain('+', proposal.NewBaseFont);
        Assert.Contains("Liberation Sans", proposal.SourceDescription);
        Assert.True(proposal.Descriptor.Ascent > 0);
        Assert.NotEqual(0, proposal.DescriptorFlags & 32); // Nonsymbolic, always
    }

    [Fact]
    public void The_replacement_program_is_advance_patched_to_the_declared_widths()
    {
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);
        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));

        var m = new EmbeddedFontMetrics(proposal.Program);
        foreach ((int cid, ushort gid) in proposal.CidToGid)
        {
            double declared = cid == 0x41 ? 500 : 1000; // /W [65 [500]] else /DW 1000
            double programWidth = ProgramWidthResolver.Scale(m, m.GetAdvanceWidth(gid));
            Assert.True(Math.Abs(programWidth - declared) <= 0.5 + 1,
                $"cid {cid:X4} gid {gid}: program {programWidth} vs declared {declared}");
        }
    }

    [Fact]
    public void A_font_without_tounicode_declines_naming_the_identity_gap()
    {
        PdfDocument doc = DeadCid2Doc(includeToUnicode: false);

        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("ToUnicode", decline.Reason);
    }

    [Fact]
    public void A_coverage_gap_declines_with_no_partial_fix()
    {
        // <E000> is Private Use Area — Liberation Sans has no glyph for it.
        PdfDocument doc = DeadCid2Doc(toUnicodeEntries: [(0x0000, "E000")], contentHex: "0000");
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("no partial", decline.Reason);
    }

    [Fact]
    public void An_embedding_restricted_substitute_declines_absolutely()
    {
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(EmbedFixtures.RestrictedEmbeddingFont());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("licensed by its vendor", decline.Reason);
    }

    [Fact]
    public void A_non_truetype_substitute_declines_naming_the_mechanism()
    {
        PdfDocument doc = DeadCid2Doc();
        byte[] cff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        var provider = new StubFontProvider(cff);

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("TrueType", decline.Reason);
    }

    [Fact]
    public void A_simple_font_notdef_finding_declines_naming_v1_scope()
    {
        PdfDocument doc = WidthPatchFixtures.NotdefOnlyDoc();

        FontRemediationProposal result = Planner().Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("simple font", decline.Reason);
    }

    [Fact]
    public void A_cid0_descendant_converts_to_cid2()
    {
        PdfDocument doc = DeadCid0Doc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
        Assert.True(proposal.CidToGid.TryGetValue(0x0000, out ushort gid0) && gid0 != 0);
        Assert.True(proposal.CidToGid.TryGetValue(0x0041, out ushort gid41) && gid41 != 0);
        // CID 0 is .notdef in the OLD (charset-bearing) program; CID 0x41 already has a real glyph
        // there (gid 1, via the charset) — only the former is a restored code.
        Assert.Equal(1, proposal.RestoredCodeCount);
    }

    [Fact]
    public void Two_type0_wrappers_sharing_one_descendant_decline_the_shared_program_holder()
    {
        // Controller ruling, tracker issue 38: last-write-wins per PROGRAM HOLDER vs. one proposal per
        // LOGICAL font — see FontRemediationPlanner.SharedHolderReason's doc comment.
        PdfDocument doc = TwoWrappersSharedHolderDoc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("shares this font's embedded program", decline.Reason);
    }

    [Fact]
    public void A_cid_keyed_cff_substitute_declines_with_the_truetype_mechanism_not_table_124()
    {
        // Task-5-review Important: RunByteGates used to run the SIMPLE-font Table-124 gate
        // (SimpleFontProgramSubtype.Resolve) against a COMPOSITE font's substitute here, so a
        // genuinely CID-keyed CFF/OTF candidate produced a factually inverted decline ("...permits
        // only for a composite (Type0) font, never for a simple one" about a font that IS composite).
        // BuildReplacement now calls RunByteGates(simpleFont: false), which skips that gate — this
        // candidate must reach the TrueType-mechanism decline instead.
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(MinimalCff.BuildCid(numGlyphs: 2));

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("not a TrueType program", decline.Reason);
        Assert.DoesNotContain("Table 124", decline.Reason);
    }

    // ── manual path (AssessReplacementCandidate) ───────────────────────────────────────────────

    [Fact]
    public void AssessReplacementCandidate_hard_blocks_a_coverage_gap()
    {
        PdfDocument doc = DeadCid2Doc();
        IReadOnlyList<FontInventoryEntry> inventory = FontInventory.Read(doc);
        FontInventoryEntry entry = FontInventory.Find(inventory, 1)!;

        // Lacks any Unicode-cmap coverage of 'A'/'B' — its only mapped code is Mac-Roman code 10.
        byte[] candidate = ZeroAdvanceSfntFixture.FontBytes(gid1Advance: 450);

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", candidate, faceIndex: 0, sourceDescription: "Test");

        Assert.NotNull(result.HardBlockReason);
        Assert.Null(result.Proposal);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void AssessReplacementCandidate_declines_when_the_font_draws_no_characters()
    {
        // Task-5-review Important: an empty CidToGid (entry.UsedCodes empty — reachable only through
        // the manual path, since Propose() never attributes a font-program finding to a font with no
        // used codes) must not silently produce a proposal that maps EVERY CID to .notdef.
        PdfDocument doc = DeadCid2Doc();
        FontInventoryEntry baseEntry = FontInventory.Find(FontInventory.Read(doc), 1)!;
        FontInventoryEntry entry = baseEntry with { UsedCodes = [] };

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0, sourceDescription: "Test");

        Assert.NotNull(result.HardBlockReason);
        Assert.Contains("no characters", result.HardBlockReason);
        Assert.Null(result.Proposal);
    }
}
