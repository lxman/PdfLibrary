using System;
using System.Collections.Generic;
using System.Linq;
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
        string contentHex = "0042 0041",
        string baseFont = "ABCDEF+DeadFace",
        int flags = 4,
        int? italicAngle = null,
        ushort macStyle = 0,
        ushort[]? cidToGid = null) =>
        ReplaceProgramFixtures.DeadCid2Doc(
            toUnicodeEntries, includeToUnicode, contentHex, baseFont, flags, italicAngle, macStyle, cidToGid);

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
        Assert.Single(proposal.Targets);
        Assert.Equal(4, proposal.Font.ObjectNumber);                      // descendant holder
        Assert.Equal(1, proposal.Targets[0].CompositeFont.ObjectNumber);  // Type0 wrapper
        Assert.Equal(FontProgramFormat.TrueType, proposal.Format);
        Assert.True(proposal.Targets[0].CidToGid.TryGetValue(0x0042, out ushort gid42) && gid42 != 0);
        Assert.True(proposal.Targets[0].CidToGid.TryGetValue(0x0041, out ushort gid41) && gid41 != 0);
        Assert.Equal(1, proposal.RestoredCodeCount); // only CID 0x42 was .notdef in the OLD program
        Assert.DoesNotContain('+', proposal.NewBaseFont);
        Assert.Contains("Liberation Sans", proposal.SourceDescription);
        Assert.True(proposal.Descriptor.Ascent > 0);
        Assert.NotEqual(0, proposal.DescriptorFlags & 32); // Nonsymbolic, always
    }

    [Fact]
    public void A_singleton_replacement_proposal_always_closes_its_own_target()
    {
        // Task 3 amendment (controller ruling, post-Task-2 review — spec §6): ClosesFinding is false
        // only for a group member that draws CID 0 while a sibling does not, and ProposeProgramReplace
        // declines every CID-0-drawing font BEFORE it ever reaches proposal construction (the gate
        // above the holder resolution). In this task every proposal is a singleton, so the one target
        // a singleton proposal ever constructs necessarily closes its own 6.2.11.8 finding.
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        ReplaceTarget target = Assert.Single(proposal.Targets);
        Assert.True(target.ClosesFinding);
    }

    [Fact]
    public void The_replacement_program_is_advance_patched_to_the_declared_widths()
    {
        PdfDocument doc = DeadCid2Doc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);
        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));

        var m = new EmbeddedFontMetrics(proposal.Program);
        foreach ((int cid, ushort gid) in proposal.Targets[0].CidToGid)
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
        // <E000> is Private Use Area — Liberation Sans has no glyph for it. Uses the migrated dead
        // code (0x42), not CID 0 — CID 0 alone would now hit the issue-40 cid0-only honesty decline
        // before ever reaching the coverage-gap check, which is not what this test exercises.
        PdfDocument doc = DeadCid2Doc(toUnicodeEntries: [(0x0042, "E000")], contentHex: "0042");
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
        Assert.True(proposal.Targets[0].CidToGid.TryGetValue(0x0042, out ushort gid42) && gid42 != 0);
        Assert.True(proposal.Targets[0].CidToGid.TryGetValue(0x0041, out ushort gid41) && gid41 != 0);
        // CID 0x42 is .notdef in the OLD (charset-bearing) program — absent from customCharsetSids;
        // CID 0x41 already has a real glyph there (gid 1, via the charset) — only the former is a
        // restored code.
        Assert.Equal(1, proposal.RestoredCodeCount);
    }

    // ── issue 40 honesty: a used CID 0 can never be fixed by a replacement ────────────────────────

    /// <summary>Verbatim per the controller brief — a later sweep taxonomy keys on this exact
    /// string. Pinning it with one full-string Assert.Equal (not fragment Contains checks) is what
    /// actually proves the wording a later task depends on, not just that SOME substring survived.</summary>
    private const string Cid0OnlyDeclineReason =
        "This font draws character code 0, which ISO 32000 defines as .notdef regardless of what "
        + "glyph the font maps it to — no font-side fix can make that draw conformant.";

    [Fact]
    public void A_cid0_only_font_declines_rather_than_proposing_a_fix_that_closes_nothing()
    {
        // CID 0 is the font's ONLY dead code (DeadCid2DocDrawingCidZero's default: /CIDToGIDMap
        // /Identity, content "0000 0041" — CID 0x41 identity-maps to a nonzero "live" GID). Since
        // FontProgramRule now flags a USED CID 0 regardless of what any replacement's map assigns
        // it, proposing a swap here would close ZERO rule-visible findings — the false-fix shape
        // the resave-harness convention exists to catch.
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2DocDrawingCidZero(onlyCidZeroDead: true);
        FontRemediationProposal result = Planner(new RecordingFontProvider(LiberationSansBytes()))
            .Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Equal(Cid0OnlyDeclineReason, decline.Reason);
    }

    [Fact]
    public void A_cid0_plus_other_dead_codes_still_declines_because_the_finding_is_per_font()
    {
        // Controller ruling (2026-08-17 review, supersedes this task's original "only when CID 0 is
        // the SOLE dead code" gate shape): CID 0 (forever .notdef) drawn ALONGSIDE a genuinely
        // different dead code (0x42, via an explicit map). FontProgramRule.CheckType0 emits at most
        // ONE 6.2.11.8 finding PER FONT (a single OR'd notdefHit bool across every drawn code, not
        // one finding per dead code) — so this font's single finding survives ANY replacement
        // unconditionally, because CID 0 is still drawn (the content stream is untouched by a
        // program swap) and still .notdef afterward regardless of the substitute's map. A proposal
        // here would therefore close ZERO rule-visible findings on this font too — the same
        // false-fix shape the cid0-only case catches — so the gate declines on CID 0's mere
        // presence, with no other-dead-codes carve-out.
        using PdfDocument doc = ReplaceProgramFixtures.DeadCid2DocDrawingCidZero(onlyCidZeroDead: false);
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        DeclineProposal decline = Assert.IsType<DeclineProposal>(Assert.Single(result.Fonts));
        Assert.Equal(Cid0OnlyDeclineReason, decline.Reason);
    }

    [Fact]
    public void Two_type0_wrappers_sharing_one_descendant_merge_even_when_only_one_findings_is_proposed()
    {
        // GUARD-ERA TEST, promoted (F-4b Task 4, controller ruling tracker issue 38), then RE-PROMOTED
        // (Task 4 review finding C1, spec §4 2026-08-18 clarification, commit 16d7585): the last-write-
        // wins-per-PROGRAM-HOLDER guard (FontRemediationPlanner.SharedHolderReason) that used to
        // decline this shape is DELETED — Propose() now groups font-program findings sharing one
        // holder BEFORE dispatch and routes a multi-entry group to the merged builder instead.
        //
        // Group MEMBERSHIP is INVENTORY-scoped, not findings-scoped: even though only object 1's
        // finding is in THIS call's input, wrapper 7 (which shares the SAME descendant, and — in this
        // fixture's default shape — independently draws the shared dead code 0x42 too) is pulled in by
        // Propose()'s inventory-scoped expansion regardless. Skipping it here is exactly the silent-
        // .notdef corruption the review caught: the merged replacement rewrites the shared /FontFile2
        // either way, and an uncovered sibling's CIDToGIDMap would then point at the DEPARTED
        // program's glyph ids.
        //
        // What stays call-scoped is CLOSING credit: wrapper 7's own finding was never named in this
        // call's `findings` argument, so it does not get credit for closing — ClosesFinding is false
        // for its target even though it is a full, correctly-mapped target. See
        // MergedReplacementTests for the merged proposal's full shape assertions (identical UNION map,
        // findingless-sibling expansion, width subsumption).
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result = Planner(provider).Propose(doc, [("font-program", 1)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
        ReplaceTarget wrapper1Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 1);
        ReplaceTarget wrapper7Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        Assert.True(wrapper1Target.ClosesFinding, "wrapper 1's finding was named in this call");
        Assert.False(wrapper7Target.ClosesFinding, "wrapper 7's finding was NOT named in this call");
        // Direct sharing: both targets still carry the identical UNION map, exactly as when both
        // findings are passed together.
        Assert.Equal(wrapper1Target.CidToGid, wrapper7Target.CidToGid);
    }

    [Fact]
    public void Two_type0_wrappers_with_distinct_descendants_sharing_one_descriptor_merge_together()
    {
        // GUARD-ERA TEST, promoted (F-4b Task 4): descriptor-level sharing case — the two logical
        // fonts have DISTINCT, separately-numbered ProgramHolderIds, but both descendants' /FontFile2
        // resolves through the SAME /FontDescriptor. Both findings are in THIS call's input, so
        // HolderGroupKey (descriptor object number) groups them and the merged builder runs instead
        // of declining. See MergedReplacementTests for the per-target-map assertions.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescriptorDoc();
        var provider = new StubFontProvider(LiberationSansBytes());

        FontRemediationProposal result =
            Planner(provider).Propose(doc, [("font-program", 1), ("font-program", 7)]);

        var proposal = Assert.IsType<ReplaceProgramProposal>(Assert.Single(result.Fonts));
        Assert.Equal(2, proposal.Targets.Count);
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
        // catches that regression, not just "the decline is still a decline". Uses the migrated
        // dead code (0x42), not CID 0 — see A_coverage_gap_declines_with_no_partial_fix.
        PdfDocument doc = DeadCid2Doc(toUnicodeEntries: [(0x0042, "E000")], contentHex: "0042");
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

    // ── manual path merges (Task 7, tracker issue 38) ──────────────────────────────────────────

    [Fact]
    public void AssessReplacementCandidate_merges_a_shared_holder_group()
    {
        // Task 7: the manual path's own TEMPORARY guard (SharedHolderReason) is retired — assessing a
        // candidate against ANY member of a shared-holder group now returns the SAME merged proposal
        // the automatic path builds, naming a target per member.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        FontInventoryEntry entry = FontInventory.Find(FontInventory.Read(doc), 1)!;

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0, sourceDescription: "Test");

        var proposal = Assert.IsType<ReplaceProgramProposal>(result.Proposal);
        Assert.Equal(2, proposal.Targets.Count);
        ReplaceTarget wrapper1Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 1);
        ReplaceTarget wrapper7Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        // Ruling 2: seedIds for the manual path is ONLY the picked entry — wrapper 1 (picked) closes;
        // wrapper 7 (pulled in by holder expansion) was never asked to be fixed, so it does not.
        Assert.True(wrapper1Target.ClosesFinding);
        Assert.False(wrapper7Target.ClosesFinding);
    }

    [Fact]
    public void AssessReplacementCandidate_hard_blocks_a_merge_cid_conflict()
    {
        // The conflicting-ToUnicode fixture: wrapper 2 says 0x42 -> 'Z', wrapper 1 says 0x42 -> 'B' —
        // two fonts sharing one descendant map the same character code to different glyphs, which no
        // single replacement program can serve.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x0042, "005A")]);
        FontInventoryEntry entry = FontInventory.Find(FontInventory.Read(doc), 1)!;

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0, sourceDescription: "Test");

        Assert.NotNull(result.HardBlockReason);
        Assert.Null(result.Proposal);
        Assert.Contains("different characters", result.HardBlockReason);
    }

    [Fact]
    public void AssessReplacementCandidate_merge_does_not_gain_the_automatic_paths_group_wide_cid0_decline()
    {
        // Ruling 1 (controller brief, Task 7): the manual path routes through BuildMergedReplacement
        // DIRECTLY, never ProposeMergedReplace, so it never runs the automatic path's group-wide
        // Cid0OnlyDeclineReason gate. The picked entry (wrapper 7) draws ONLY CID 0 here — the exact
        // shape that makes the AUTOMATIC path (ProposeMergedReplace) decline the whole group outright,
        // since no seed would close — but the manual path still proposes, honestly reporting
        // ClosesFinding: false for the picked entry's own target instead of refusing outright.
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc(
            wrapper2ToUnicode: [(0x00, "0043")], wrapper2Codes: [0x00]);
        FontInventoryEntry entry = FontInventory.Find(FontInventory.Read(doc), 7)!;

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0, sourceDescription: "Test");

        var proposal = Assert.IsType<ReplaceProgramProposal>(result.Proposal);
        Assert.Equal(2, proposal.Targets.Count);
        ReplaceTarget wrapper2Target = proposal.Targets.Single(t => t.CompositeFont.ObjectNumber == 7);
        Assert.False(wrapper2Target.ClosesFinding, "wrapper 7 draws only CID 0, which never closes");
    }

    [Fact]
    public void AssessReplacementCandidate_merge_uses_the_callers_source_description()
    {
        // sourceDescription is a manual-path-only parameter — the merged path must thread it through
        // BuildMergedReplacement exactly as the singleton path already threads it through
        // BuildReplacement, rather than silently falling back to "from your system fonts".
        PdfDocument doc = ReplaceProgramFixtures.SharedDescendantDoc();
        FontInventoryEntry entry = FontInventory.Find(FontInventory.Read(doc), 1)!;

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0,
            sourceDescription: "User-picked Test Face");

        var proposal = Assert.IsType<ReplaceProgramProposal>(result.Proposal);
        Assert.Equal("User-picked Test Face", proposal.SourceDescription);
    }

    [Fact]
    public void AssessReplacementCandidate_merge_blocks_when_a_sibling_is_not_composite()
    {
        // Task 4 round-2 ruling, mirrored for the manual path: a non-composite sibling sharing the
        // picked entry's holder blocks the WHOLE assessment — writing a composite substitute over a
        // shared program a simple font depends on would corrupt it. This is a real corruption hazard,
        // not an over-decline.
        PdfDocument doc = ReplaceProgramFixtures.SimpleFontSharingDescriptorWithCompositeSeedDoc();
        FontInventoryEntry entry = FontInventory.Find(FontInventory.Read(doc), 1)!;

        CandidateAssessment result = Planner().AssessReplacementCandidate(
            doc, entry, "font-program", LiberationSansBytes(), faceIndex: 0, sourceDescription: "Test");

        Assert.NotNull(result.HardBlockReason);
        Assert.Null(result.Proposal);
        Assert.Contains("cannot be included in a merged replacement", result.HardBlockReason);
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
