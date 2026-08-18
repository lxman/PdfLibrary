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

    /// <summary>
    /// Answers DIFFERENTLY depending on the requested <c>/BaseFont</c> — unlike
    /// <see cref="StubFontProvider"/>, which returns the same bytes regardless of what the request
    /// asks for (by design, to prove the planner reports what it actually got). Tracker issue 39's
    /// scenario needs exactly this: a RAW family name resolving to one face, and the synthetic
    /// Standard-14 fallback name <c>SubstituteFontResolver.Classify</c> +
    /// <c>SyntheticStd14Name</c> derive for it resolving to a DIFFERENT one — the shape
    /// <c>SystemFontLocator</c>'s own step-3 ladder produces on a real machine (a non-base-35
    /// family resolving through a CFF system face, e.g. Nimbus) that
    /// <see cref="BundledStandard14Provider"/> never gets to intercept, because it only recognises
    /// a request whose ORIGINAL family is itself a base-35 alias.
    /// </summary>
    private sealed class RawVsSyntheticFontProvider(
        string rawFamily, byte[]? rawBytes, string syntheticName, byte[]? syntheticBytes) : ISystemFontProvider
    {
        public FontMatch? Resolve(FontRequest request) => request.BaseFont switch
        {
            _ when request.BaseFont == rawFamily => rawBytes is null ? null : new FontMatch(rawBytes, 0),
            _ when request.BaseFont == syntheticName => syntheticBytes is null ? null : new FontMatch(syntheticBytes, 0),
            _ => null,
        };

        public IReadOnlyCollection<string> GetAvailableFontFamilies() =>
            throw new NotSupportedException("The planner does not call GetAvailableFontFamilies.");
        public bool IsFontAvailable(string familyName) =>
            throw new NotSupportedException("The planner does not call IsFontAvailable.");
        public string? FindFirstAvailable(IEnumerable<string> candidates) =>
            throw new NotSupportedException("The planner does not call FindFirstAvailable.");
        public void RefreshCache() =>
            throw new NotSupportedException("The planner does not call RefreshCache.");
    }

    /// <summary>See <see cref="ReplaceProgramFixtures.DeadCid2Doc"/> — shared with Task 6's apply
    /// tests so the two suites cannot silently diverge on the document shape the gate relies on.</summary>
    private static PdfDocument DeadCid2Doc(
        IReadOnlyList<(int Code, string Hex)>? toUnicodeEntries = null,
        bool includeToUnicode = true,
        string contentHex = "0000 0041",
        string baseFont = "ABCDEF+DeadFace",
        int flags = 4,
        int? italicAngle = null,
        ushort macStyle = 0) =>
        ReplaceProgramFixtures.DeadCid2Doc(
            toUnicodeEntries, includeToUnicode, contentHex, baseFont, flags, italicAngle, macStyle);

    /// <summary>See <see cref="ReplaceProgramFixtures.DeadCid0Doc"/>.</summary>
    private static PdfDocument DeadCid0Doc() => ReplaceProgramFixtures.DeadCid0Doc();

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
        // LOGICAL font — see FontRemediationPlanner.SharedHolderReason's doc comment. Both wrappers
        // draw in ReplaceProgramFixtures.SharedDescendantDoc (unlike the retired F-4b-era
        // TwoWrappersSharedHolderDoc, where only wrapper 1 drew) — verified not to change this guard's
        // outcome: SharedHolderReason fires on ProgramHolderId identity alone, before either wrapper's
        // drawn content is examined.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("shares this font's embedded program", decline.Reason);
    }

    [Fact]
    public void Two_type0_wrappers_with_distinct_descendants_sharing_one_descriptor_both_decline()
    {
        // Descriptor-level sharing case: unlike SharedDescendantDoc, the two logical fonts here have
        // DISTINCT, separately-numbered ProgramHolderIds — the object-number-only comparison in the
        // ORIGINAL SharedHolderReason would have missed this and let both through as independent
        // ReplaceProgramProposals, each with its own CidToGid map, targeting the same /FontDescriptor's
        // /FontFile2 — last write wins, the loser's CIDToGIDMap indexes the winner's face.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result =
            Planner(provider).Propose(doc, [("font-program", 1), ("font-program", 7)]);

        Assert.Equal(2, result.Fonts.Count);
        foreach (FontProposal proposal in result.Fonts)
        {
            DeclineProposal decline = Assert.IsType<DeclineProposal>(proposal);
            Assert.Contains("shares this font's embedded program", decline.Reason);
        }
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

    [Fact]
    public void A_cff_faced_raw_family_retries_the_synthetic_std14_name_and_closes()
    {
        // Tracker issue 39, fix round 1 (controller ruling, spec §3 step 1 "Liberation precedence"):
        // this fixture's /BaseFont is "ABCDEF+DeadFace" -> FamilyName "DeadFace", which matches no
        // serif/mono/bold/italic keyword, so Classify+SyntheticStd14Name derive "Helvetica" for it —
        // exactly what SystemFontLocator's own step 3 would try. The raw name resolves to a CFF
        // face (declines "not a TrueType program" on its own); the SAME synthetic name a real
        // BundledStandard14Provider answers for "helvetica" resolves TrueType — the retry must find
        // it and close, with SourceDescription naming what was ACTUALLY written (Liberation), not
        // the raw family the document named.
        PdfDocument doc = DeadCid2Doc();
        byte[] cff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        var provider = new RawVsSyntheticFontProvider("DeadFace", cff, "Helvetica", LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
        Assert.Contains("Liberation Sans", proposal.SourceDescription);
    }

    [Fact]
    public void No_face_at_all_for_the_raw_family_also_retries_the_synthetic_std14_name_and_closes()
    {
        // The OTHER retry trigger (match is null, not just a wrong format): no face at all for the
        // raw family, but the synthetic name resolves a usable TrueType face.
        PdfDocument doc = DeadCid2Doc();
        var provider = new RawVsSyntheticFontProvider("DeadFace", null, "Helvetica", LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
    }

    [Fact]
    public void A_coverage_gap_decline_does_not_retry_the_synthetic_name()
    {
        // Scope check: the retry fires ONLY when the primary attempt found no face at all, or one
        // this operation cannot use (non-TrueType) — a genuine coverage gap is a fact about the
        // substitute FOUND (already TrueType), not about which name was requested, so retrying
        // could not fix it and must not be attempted. Proven by making the SYNTHETIC name resolve
        // to a CFF face: if the retry incorrectly fired anyway, BuildReplacement would run against
        // it and the decline reason would flip to "not a TrueType program" — the assertion below
        // catches that regression, not just "the decline is still a decline".
        PdfDocument doc = DeadCid2Doc(toUnicodeEntries: [(0x0000, "E000")], contentHex: "0000");
        byte[] cff = MinimalCff.Build(charsetOperand: null, numGlyphs: 4);
        var provider = new RawVsSyntheticFontProvider("DeadFace", LiberationSansBytes(), "Helvetica", cff);

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Contains("no partial", decline.Reason);
    }

    /// <summary>Answers every request with the same bytes (like <see cref="StubFontProvider"/>) but
    /// RECORDS each <see cref="FontRequest"/> it sees, so a test can assert what style the planner
    /// actually asked for — tracker issue 43's scenario turns on exactly that.</summary>
    private sealed class RecordingFontProvider(byte[]? bytes) : ISystemFontProvider
    {
        public readonly List<FontRequest> Requests = [];

        public FontMatch? Resolve(FontRequest request)
        {
            Requests.Add(request);
            return bytes is null ? null : new FontMatch(bytes, 0);
        }

        public IReadOnlyCollection<string> GetAvailableFontFamilies() =>
            throw new NotSupportedException("The planner does not call GetAvailableFontFamilies.");
        public bool IsFontAvailable(string familyName) =>
            throw new NotSupportedException("The planner does not call IsFontAvailable.");
        public string? FindFirstAvailable(IEnumerable<string> candidates) =>
            throw new NotSupportedException("The planner does not call FindFirstAvailable.");
        public void RefreshCache() =>
            throw new NotSupportedException("The planner does not call RefreshCache.");
    }

    [Fact]
    public void A_faux_italic_declaration_defers_to_the_upright_embedded_program()
    {
        // Tracker issue 43: 0000_0000024.pdf declares three faces (Regular/Bold/Italic) that all
        // share ONE upright embedded program — the ",Italic" name suffix, descriptor flag 0x40, and
        // /ItalicAngle -11 are declarations the glyphs contradict. Every reference renderer
        // (poppler/MuPDF/Ghostscript, oracle-verified 2026-08-17) draws the embedded program and
        // ignores the declared style, so a replacement styled from the DECLARATION visibly restyles
        // the page. When the program being replaced parses, its own head.macStyle (regular in this
        // fixture) is the ground truth for the replacement's style — not the name, not the flags.
        PdfDocument doc = DeadCid2Doc(
            baseFont: "ABCDEF+DeadFace,Italic", flags: 4 | 0x40, italicAngle: -11);
        var provider = new RecordingFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        FontRequest request = Assert.Single(provider.Requests);
        Assert.False(request.Italic,
            "the embedded program's head.macStyle is regular, so the replacement request must not "
            + "ask for an italic face no matter what the name/descriptor declare");
        Assert.False(request.Bold);
    }

    [Fact]
    public void The_synthetic_retry_derives_its_name_from_the_program_style_not_the_declaration()
    {
        // The same style-lie, but through the issue-39 synthetic retry — the path that actually
        // produced the italic pick on 0000_0000024.pdf: Classify reads the ",Italic" name token and
        // derives "Helvetica-Oblique", which a bundled provider answers with an italic face. With an
        // upright program the retry must ask for plain "Helvetica" instead; this provider answers
        // ONLY that name, so the proposal closes exactly when the retry name is upright.
        PdfDocument doc = DeadCid2Doc(
            baseFont: "ABCDEF+DeadFace,Italic", flags: 4 | 0x40, italicAngle: -11);
        var provider = new RawVsSyntheticFontProvider(
            "DeadFace,Italic", null, "Helvetica", LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
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

    [Fact]
    public void A_genuinely_italic_program_is_replaced_with_an_italic_face_even_when_nothing_declares_it()
    {
        // The inverse pin for issue 43's program-first rule: head.macStyle italic (bit 1) with a
        // style-silent name and descriptor must yield an ITALIC replacement request. Under the old
        // declaration-only derivation this request came out regular — the same fidelity loss as the
        // faux-italic case, mirrored.
        PdfDocument doc = DeadCid2Doc(macStyle: 0x2);
        var provider = new RecordingFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        FontRequest request = Assert.Single(provider.Requests);
        Assert.True(request.Italic,
            "the embedded program's head.macStyle declares italic, and the program outranks the "
            + "style-silent name/descriptor");
        Assert.False(request.Bold);
    }
}
