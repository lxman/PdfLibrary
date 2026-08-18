using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Editing;
using PdfLibrary.Fonts.Remediation;
using PdfLibrary.Structure;
using PdfLibrary.Tests.Conformance;
using PdfLibrary.Tests.Fonts;
using Xunit;

namespace PdfLibrary.Tests.Fonts.Remediation;

/// <summary>
/// F-4b Task 9 (spec 2026-08-17-f4b-notdef-program-replacement, §3 gate 4): the REALITY GATE that
/// runs the whole engine stack — planner dispatch (<see cref="FontRemediationPlanner.Propose(PdfLibrary.Structure.PdfDocument,System.Collections.Generic.IEnumerable{System.ValueTuple{string,int}})"/>
/// routing a composite <c>.notdef</c> finding to <see cref="FontRemediationPlanner.ProposeProgramReplace"/>,
/// and <see cref="PdfDocumentEditor.ReplaceCompositeProgram"/> applying it — against real-world
/// documents pinned by the 2026-08-17 groundwork re-measure (Task 3's population: 341 composite
/// <c>.notdef</c> findings across 94 docs, 279 CID0 + 62 CID2, 340/341 carry <c>/ToUnicode</c>).
///
/// <para>The AUTHORITATIVE doc list is <c>docs/superpowers/notes/2026-08-17-f4b-groundwork-remeasure.md</c>
/// §6 (Pellucid repo, "Task 9 pinned-doc list") — NOT the design spec's prose, which predates that
/// re-measure. Every Theory below cites its row and states what is MEASURED, not assumed.</para>
///
/// <para><b>Fix round 1 (tracker issue 39, reviewer-verified reproducible engine defect, NOT machine
/// variance):</b> the first version of this gate measured every "expect: Close" cc-main candidate
/// as a decline, and attributed it to font-availability noise. The real cause was a REPRODUCIBLE
/// planner bug: <see cref="Fonts.SystemFontLocator"/>'s own ladder resolves a non-base-35 family
/// (e.g. 'AlArabiya', 'HelveticaNeue-Medium') through its INTERNAL synthetic Standard-14 fallback
/// into whatever <see cref="Fonts.Base35Aliases"/> ranks first for that synthetic name — Nimbus
/// Sans/Roman (CFF) on this machine, ahead of Liberation — and <see cref="Fonts.BundledStandard14Provider"/>
/// (built to beat exactly that precedence with Liberation TTF) never got a chance to intercept,
/// because it only recognises a REQUEST whose ORIGINAL family is itself a base-35 alias, not a name
/// synthesised deep inside the locator's own ladder. <see cref="FontRemediationPlanner.ProposeProgramReplace"/>
/// now retries once with that same synthetic name when the primary attempt finds no face, or a
/// non-TrueType one — giving the bundled provider the intercept chance it needs, honouring spec §3
/// step 1's "Liberation precedence". See tracker issue 39 for the full writeup and the render/F-2
/// scope this fix does NOT touch.</para>
///
/// <para>Post-fix measured reality: four of the six original "expect: Close" candidates
/// (both SCV docs, <c>0000_0000024.pdf</c>, <c>6000_6000827.pdf</c>) now genuinely close in full.
/// The remaining two (<c>0000_0000714.pdf</c>, <c>4000_4000802.pdf</c>) still decline — their
/// resolved substitute (DejaVu Sans / DejaVu Sans Light) is ALREADY TrueType, missing exactly one
/// used glyph; a coverage gap is a fact about the substitute found, not about which name was
/// requested, so the retry correctly does not fire for these and cannot change the outcome. Two
/// simple-font declines (out of the retry's scope entirely — composite-kind gates before any font
/// resolution) needed their doc comments corrected to the reason ACTUALLY measured, not assumed.</para>
///
/// <para>Corpus files are READ-ONLY — every apply happens against a temp copy (<see cref="ApplyAndRecheck"/>),
/// never the corpus file itself, cloning <see cref="FontProgramWidthRepairCorpusTests.ApplyAndRecheck"/>'s
/// own discipline. Font provider: <see cref="EmbedProgramRoundTripTests.DeterministicFonts"/> — the
/// SAME <c>BundledStandard14Provider(LoadLiberationFace, SystemFontLocator.Default)</c> composition
/// F-2's round-trip gate uses — a real resolution against this machine's installed fonts (plus the
/// vendored Liberation faces for the Standard 14 names).</para>
///
/// <para>LocalOnly: mirrors <see cref="FontProgramWidthRepairCorpusTests"/> and
/// <see cref="WidthFalsePositiveCorpusTests"/> — the trait is the only thing keeping this out of CI
/// (see .github/workflows filters).</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class FontProgramReplaceCorpusTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private const string CcMainCorpusVariable = "PDFLIBRARY_CCMAIN_CORPUS";
    private const string CcMainDefaultCorpus = @"D:\PdfCorpora\real-world\cc-main-2021-31-sample";

    // Corpus resolution copied verbatim from FontProgramWidthRepairCorpusTests' Corpus()/CcMainCorpus().
    private static string? Corpus()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static string? CcMainCorpus()
    {
        string root = Environment.GetEnvironmentVariable(CcMainCorpusVariable) ?? CcMainDefaultCorpus;
        return Directory.Exists(root) ? root : null;
    }

    private static (List<ReplaceProgramProposal> Replacements, List<DeclineProposal> Declines,
        List<PatchWidthsProposal> Patches, PreflightResult Before, int TotalProposals) ProposeFor(string path)
    {
        PreflightResult before = Preflighter.Check(path, ConformanceProfile.PdfA2b);
        using PdfDocument doc = PdfDocument.Load(path);
        var planner = new FontRemediationPlanner(EmbedProgramRoundTripTests.DeterministicFonts);
        // Feed EVERY font-program finding, not just 6.2.11.8 — the planner's own dispatch
        // (ProposeWidthPatch -> ProposeProgramReplace when hasNotdef && composite) needs the full
        // per-object finding set to route correctly, and a mixed doc's width findings must still
        // surface their own (unrelated) declines in the same call.
        FontRemediationProposal proposed = planner.Propose(doc,
            before.Findings.Where(f => f.RuleId == "font-program" && f.ObjectNumber is not null)
                .Select(f => (f.RuleId, f.ObjectNumber!.Value)));
        return (proposed.Fonts.OfType<ReplaceProgramProposal>().ToList(),
                proposed.Fonts.OfType<DeclineProposal>().ToList(),
                proposed.Fonts.OfType<PatchWidthsProposal>().ToList(),
                before, proposed.Fonts.Count);
    }

    // Mirrors FontProgramWidthRepairCorpusTests.ApplyAndRecheck: ReplaceProgramProposal carries only
    // object numbers + resolved bytes (no in-memory object handle), so a proposal produced against
    // one load transfers cleanly onto a second load of the SAME bytes — the corpus files are
    // read-only, so nothing changes between the two loads.
    private static PreflightResult ApplyAndRecheck(string path, IEnumerable<ReplaceProgramProposal> replacements)
    {
        using PdfDocument doc = PdfDocument.Load(path);
        using PdfDocumentEditor editor = doc.Edit();
        foreach (ReplaceProgramProposal replacement in replacements)
            editor.ReplaceCompositeProgram(replacement);
        string temp = Path.GetTempFileName();
        try
        {
            using (FileStream fs = File.Create(temp)) editor.Save(fs);
            return Preflighter.Check(temp, ConformanceProfile.PdfA2b);
        }
        finally { File.Delete(temp); }
    }

    private static Dictionary<string, int> CountByRule(PreflightResult result) =>
        result.Findings.GroupBy(f => f.RuleId).ToDictionary(g => g.Key, g => g.Count());

    private static int NotdefCount(PreflightResult result) => result.Findings.Count(
        f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.8");

    private static int WidthCount(PreflightResult result) => result.Findings.Count(
        f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");

    /// <summary>
    /// Fix round 1 narrowing (reviewer-verified Important finding): the FIRST version of this helper
    /// tolerated a decrease on EVERY rule id as a blanket "Save() side effect" allowance — too broad,
    /// because it would have silently absorbed a genuine regression on any OTHER rule the exact same
    /// way it (correctly) absorbs the one rule actually observed to move.
    ///
    /// <para>Measured root cause (2026-08-17): <c>PdfDocumentEditor.Save()</c> rewrites indirect
    /// objects with its own canonical framing, which incidentally fixes any PRE-EXISTING
    /// <c>indirect-object-spacing</c> (ISO 19005 §6.1.9, byte-level whitespace) violation regardless
    /// of what the edit itself targeted — a general property of any edit-and-save round trip through
    /// this engine, unrelated to font remediation. That is the ONLY rule id this tolerance applies
    /// to; every other rule id returns to strict equality.</para>
    /// </summary>
    private static void AssertRuleCountsHold(
        string file, Dictionary<string, int> beforeByRule, Dictionary<string, int> afterByRule)
    {
        foreach ((string ruleId, int beforeCount) in beforeByRule)
        {
            // font-program's own count is EXPECTED to move — that is the entire point of a
            // replacement (or, for the coverage/decline docs, the caller doesn't reach this helper
            // with any replacements at all) — and is covered by the caller's own NotdefCount/
            // WidthCount assertions instead. Excluded here, not tolerance-relaxed like the spacing
            // rule below: those are TWO DIFFERENT KINDS of "expected to move" (an intended effect
            // of THIS operation vs. an incidental Save() side effect of ANY operation), and
            // conflating them would hide a real regression in the one this helper exists to catch.
            if (ruleId == "font-program") continue;
            afterByRule.TryGetValue(ruleId, out int afterCount);
            if (ruleId == "indirect-object-spacing")
            {
                Assert.True(afterCount <= beforeCount,
                    $"{file}: rule 'indirect-object-spacing' ROSE from {beforeCount} to {afterCount}.");
                continue;
            }
            Assert.True(beforeCount == afterCount,
                $"{file}: rule '{ruleId}' moved from {beforeCount} to {afterCount} after applying the " +
                "measured proposals — this operation should only ever touch font-program findings " +
                "(and, incidentally, indirect-object-spacing via Save()'s own canonical framing).");
        }
        foreach ((string ruleId, int afterCount) in afterByRule)
        {
            if (ruleId == "font-program" || beforeByRule.ContainsKey(ruleId)) continue;
            Assert.True(0 == afterCount,
                $"{file}: rule '{ruleId}' appeared ({afterCount}) after applying the measured proposals.");
        }
    }

    /// <summary>
    /// note §6 rows 3, 6 (post fix round 1): the issue-34 reproducer (<c>0000_0000024.pdf</c>) and
    /// <c>6000_6000827.pdf</c> — expected "Close" in §6, and MEASURED as genuinely, fully closing:
    /// every composite <c>.notdef</c> finding on each doc becomes a <see cref="ReplaceProgramProposal"/>
    /// (zero declines), and applying all of them clears 6.2.11.8 to zero. Before fix round 1's
    /// tracker-issue-39 retry, these all declined — 'AlArabiya' each resolved, on this machine's
    /// step-3 synthetic fallback, to a CFF-flavoured Nimbus face that
    /// <see cref="Fonts.BundledStandard14Provider"/> never got to intercept; the retry now gives it
    /// that chance and every <see cref="ReplaceProgramProposal.SourceDescription"/> below confirms
    /// it actually wrote Liberation Sans/Serif, honestly naming what it wrote (not the raw family
    /// the document named).
    ///
    /// <para>note §6 rows 1-2 (the two SCV CID0 docs) MOVED to
    /// <see cref="Cid0_only_documents_decline_entirely_under_the_issue_40_honesty_gate"/> — issue 40
    /// (this task, 2026-08-17 re-measure): both SCV composite fonts draw CID 0 as their SOLE dead
    /// code, and <c>FontProgramRule</c> now flags a USED CID 0 regardless of what any replacement's
    /// map assigns it, so a "fix" here would close zero rule-visible findings. The planner's own
    /// cid0-only honesty gate declines both rather than proposing that false fix.</para>
    /// </summary>
    [Theory]
    [InlineData("ccmain", "0000_0000024.pdf")]
    [InlineData("ccmain", "6000_6000827.pdf")]
    public void Fully_closing_documents_lose_every_notdef_finding_without_raising_width(string corpus, string file)
    {
        string? root = corpus == "local" ? Corpus() : CcMainCorpus();
        string defaultPath = corpus == "local" ? DefaultCorpus : CcMainDefaultCorpus;
        Assert.SkipWhen(root is null, $"corpus not present at {defaultPath} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult before, int total) = ProposeFor(path);

        int beforeNotdef = NotdefCount(before);
        Assert.True(beforeNotdef > 0, $"{file}: expected at least one pre-existing .notdef finding.");
        Assert.Empty(patches);
        Assert.Empty(declines);
        Assert.Equal(total, replacements.Count);
        Assert.Equal(beforeNotdef, replacements.Count);
        Assert.All(replacements, r => Assert.Contains("Liberation", r.SourceDescription));

        int beforeWidth = WidthCount(before);
        Dictionary<string, int> beforeByRule = CountByRule(before);

        PreflightResult after = ApplyAndRecheck(path, replacements);

        Assert.Equal(0, NotdefCount(after));

        int afterWidth = WidthCount(after);
        Assert.True(afterWidth <= beforeWidth,
            $"{file}: width finding count ROSE after a replacement (before {beforeWidth}, after " +
            $"{afterWidth}) — the replacement program must already satisfy declared widths (spec §3).");

        AssertRuleCountsHold(file, beforeByRule, CountByRule(after));
    }

    /// <summary>
    /// Issue 40 (this task, 2026-08-17 re-measure): note §6 rows 1-2, the two SCV docs — expected
    /// "Close" in §6 and measured as such through fix round 1, but MEASURED HERE (post issue-40
    /// predicate fix) as declining ENTIRELY: both composite fonts on each doc draw CID 0 as their
    /// SOLE dead code (all their other used codes already resolve to a real glyph in the OLD
    /// program). <c>FontProgramRule.CheckType0</c> now flags a used CID 0 regardless of what glyph
    /// any replacement's map assigns it (ISO 32000 §9.7.4.2), so a replacement here would close ZERO
    /// rule-visible findings — <c>FontRemediationPlanner.ProposeProgramReplace</c>'s cid0-only
    /// honesty gate declines instead of proposing that false fix. Moved out of
    /// <see cref="Fully_closing_documents_lose_every_notdef_finding_without_raising_width"/>, which
    /// these two rows no longer satisfy (zero replacements, not "every finding closes").
    /// </summary>
    [Theory]
    [InlineData("SCV~us~en~file=N0088673.pdf~gen~ref.pdf")]
    [InlineData("SCV~us~en~file=SCVTORQUEWRENCH.PDF~gen~ref.PDF")]
    public void Cid0_only_documents_decline_entirely_under_the_issue_40_honesty_gate(string file)
    {
        string? root = Corpus();
        Assert.SkipWhen(root is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult before, int total) = ProposeFor(path);

        Assert.True(NotdefCount(before) > 0, $"{file}: expected at least one pre-existing .notdef finding.");
        Assert.Empty(patches);
        Assert.Empty(replacements);
        Assert.Equal(total, declines.Count);
        Assert.True(declines.All(d => d.Reason.Contains("character code 0")),
            $"{file}: expected every decline to cite the issue-40 cid0-only reason; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
    }

    /// <summary>
    /// note §6 rows 4-5: <c>0000_0000714.pdf</c> and <c>4000_4000802.pdf</c>, both expected "Close".
    /// UNAFFECTED by the fix-round-1 retry: their resolved substitute (DejaVu Sans / DejaVu Sans
    /// Light) is ALREADY TrueType-classified — the retry's trigger condition
    /// (<c>match is null || primary.Format is not TrueType</c>) is false, by design, because a
    /// genuine glyph-coverage gap is a fact about the substitute FOUND, not about which name was
    /// requested; retrying under a different name could not fix a face that is already the "right"
    /// format but missing one glyph. Confirmed still declining post-fix, for the identical reason.
    /// </summary>
    [Theory]
    [InlineData("0000_0000714.pdf")]
    [InlineData("4000_4000802.pdf")]
    public void Coverage_gap_documents_still_decline_after_the_synthetic_retry(string file)
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.Empty(patches);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        Assert.Equal(total, declines.Count);
        Assert.True(declines.All(d => d.Reason.Contains("cannot honestly render")),
            $"{file}: expected every decline to cite the coverage gap; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
    }

    /// <summary>
    /// note §6 row 8: the population's sole no-<c>/ToUnicode</c> doc (object 1424,
    /// <c>Type0CidType0</c>) — <see cref="FontRemediationPlanner.ProposeProgramReplace"/> declines
    /// before ever consulting a font provider (the <c>type0.ToUnicode is null</c> gate runs ahead of
    /// <c>fonts.Resolve</c>), so this decline is deterministic regardless of installed fonts and
    /// entirely outside the fix-round-1 retry's reach (the retry only runs after a font WAS
    /// requested). Unaffected, re-confirmed post-fix.
    /// </summary>
    [Fact]
    public void No_tounicode_document_declines_for_that_reason()
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "4000_4000103.pdf";
        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> _,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        Assert.Equal(total, declines.Count);
        Assert.True(declines.Any(d => d.Reason.Contains("ToUnicode")),
            $"{file}: expected a decline citing the missing /ToUnicode mapping; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
    }

    /// <summary>
    /// note §6 row 10: <c>4000_4000993.pdf</c>, 26+ sibling simple-CFF objects — entirely OUTSIDE
    /// the fix-round-1 retry's reach: these are SIMPLE (non-Type0) fonts, so
    /// <c>ProposeWidthPatch</c>'s composite-kind gate never dispatches to
    /// <c>ProposeProgramReplace</c> at all, regardless of font availability — every decline is the
    /// deterministic v1-scope "missing glyph" reason. Unaffected, re-confirmed post-fix.
    /// </summary>
    [Fact]
    public void Simple_cff_document_declines_naming_v1_scope()
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "4000_4000993.pdf";
        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> _,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        Assert.Equal(total, declines.Count);
        Assert.True(declines.All(d => d.Reason.Contains("missing glyph")),
            $"{file}: expected every decline to cite the v1-scope missing-glyph reason; got a sample: " +
            declines[0].Reason);
    }

    /// <summary>
    /// note §6 row 9: <c>0000_0000450.pdf</c>, object 23 (the pinned simple-TT <c>.notdef</c>
    /// finding). MEASURED (2026-08-17, corrected — the ORIGINAL doc comment here assumed the
    /// "missing glyph" v1-scope reason without ever checking): this document's font-program findings
    /// total FOUR objects (8, 10, 23, 40), and object 23 carries BOTH the pinned <c>.notdef</c>
    /// finding AND a width (6.2.11.5) finding — <c>ProposeWidthPatch</c> only reaches the
    /// v1-scope "missing glyph" decline when <c>!hasWidth</c>; because object 23 ALSO has a width
    /// finding, it instead runs the ordinary width-conflict check and declines "two character codes
    /// share one glyph but declare different widths" — a genuine, PRE-EXISTING condition entirely
    /// outside the fix-round-1 retry's reach (composite-kind gate, same as the CFF doc above).
    /// Still entirely a DECLINE for every object; still zero replacements.
    /// </summary>
    [Fact]
    public void Simple_tt_document_declines_via_a_preexisting_width_conflict()
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "0000_0000450.pdf";
        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> _,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.Equal(total, declines.Count);
        DeclineProposal decline23 = Assert.Single(declines, d => d.Font.ObjectNumber == 23);
        Assert.Contains("share one glyph but declare different widths", decline23.Reason);
    }

    /// <summary>
    /// note §6 row 7: <c>0000_0000769.pdf</c> — a CFF width finding (object 2032) keeps its
    /// PRE-EXISTING charstring decline (unrelated to this task; the same finding
    /// <see cref="FontProgramWidthRepairCorpusTests.Cff_documents_decline_with_the_charstring_reason"/>
    /// pins), alongside object 1424's composite <c>.notdef</c> finding (CID0, family
    /// 'AGaramond-Semibold'). Post fix-round-1 (tracker issue 39), object 1424 used to CLOSE:
    /// 'AGaramond' matches the serif keyword and 'Semibold' matches the bold-substring check, so
    /// Classify derives 'Times-Bold', BundledStandard14Provider intercepts it, and the retry wrote
    /// Liberation Serif Bold.
    ///
    /// <para>Issue 40 (this task, 2026-08-17 re-measure) MOVED this: object 1424 draws CID 20 (live)
    /// and CID 0 — CID 0 is the font's SOLE dead code (CID 20 already resolves in the OLD program),
    /// so <c>FontProgramRule</c> now flags it regardless of what any replacement's map assigns, and
    /// the planner's cid0-only honesty gate declines rather than proposing a fix that would close
    /// zero rule-visible findings. Object 1424 is now MEASURED as a second decline, alongside object
    /// 2032's untouched, unrelated CFF-charstring decline — both objects decline, zero replacements.
    /// This doc's pre-existing <c>indirect-object-spacing</c> count is ZERO, so this run's own
    /// <see cref="AssertRuleCountsHold"/> call proves absence-of-regression only, not that the
    /// tolerance branch is exercised.</para>
    /// </summary>
    [Fact]
    public void Mixed_document_declines_both_its_notdef_and_its_unrelated_width_finding()
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "0000_0000769.pdf";
        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult before, int total) = ProposeFor(path);

        Assert.Empty(patches);
        Assert.Empty(replacements);
        Assert.Equal(total, declines.Count);

        DeclineProposal notdefDecline = Assert.Single(declines,
            d => d.Reason.Contains("character code 0"));
        Assert.Equal(1424, notdefDecline.Font.ObjectNumber);

        DeclineProposal widthDecline = Assert.Single(declines,
            d => d.Reason.Contains("stores its advances in CFF charstrings"));
        Assert.Equal(2032, widthDecline.Font.ObjectNumber);

        // Nothing to apply — both findings decline (mirrors the shape of the coverage-gap and
        // no-tounicode decline-only tests above): unlike the pre-issue-40 measurement, this doc no
        // longer has a replacement to round-trip through ApplyAndRecheck.
        Assert.True(NotdefCount(before) > 0, $"{file}: expected at least one pre-existing .notdef finding.");
    }
}
