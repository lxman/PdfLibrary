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
/// re-measure. Every Theory below cites its row and, where this run's category diverges from that
/// note's "expected", explains the divergence: §6's expectations were derived from
/// <c>FontInventory.Find</c> counting alone (no live font resolution — see Task 3's report, "Every
/// scan-run stat" section); THIS task is the first to run the real planner against real installed
/// fonts, and on THIS machine several "expect: Close" rows measure as "Decline" or "Mixed" instead —
/// every one of those declines cites a documented v1-scope reason
/// (<see cref="FontRemediationPlanner"/>'s own "Decision 2" comment: only a TrueType substitute can
/// replace without rewriting CFF charstrings; or a genuine glyph-coverage gap in the resolved
/// substitute face), never an exception, a wrong-category reason, or a width count rising — so this
/// is measured reality, not a defect, per the task's own "pin MEASURED truth" instruction. Two
/// surprising declines (0000_0000024.pdf, 6000_6000827.pdf — both expected Close) were independently
/// confirmed against veraPDF (<c>verapdf.bat --format text -f 2b</c>): both genuinely FAIL PDF/A-2b,
/// so the underlying finding is real; the decline is this machine's AlArabiya substitute resolving to
/// a CFF-flavoured face, not a TrueType one.</para>
///
/// <para>Corpus files are READ-ONLY — every apply happens against a temp copy (<see cref="ApplyAndRecheck"/>),
/// never the corpus file itself, cloning <see cref="FontProgramWidthRepairCorpusTests.ApplyAndRecheck"/>'s
/// own discipline. Font provider: <see cref="EmbedProgramRoundTripTests.DeterministicFonts"/> — the
/// SAME <c>BundledStandard14Provider(LoadLiberationFace, SystemFontLocator.Default)</c> composition
/// F-2's round-trip gate uses — a real resolution against this machine's installed fonts (plus the
/// vendored Liberation faces for the Standard 14 names), so a doc whose family resolves nothing, or
/// resolves to the wrong format, or resolves with a coverage gap, is a legitimate measured outcome —
/// not a test bug.</para>
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
    /// note §6 rows 1-2: the two SCV CID0 docs. §6 expected "Close" for both; MEASURED reality on
    /// this machine is a genuine per-finding MIX: each doc's two composite <c>.notdef</c> fonts
    /// request DIFFERENT families — one resolves LiberationSans-Bold (the vendored TrueType face,
    /// via <see cref="EmbedProgramRoundTripTests.DeterministicFonts"/>'s Standard-14 fallback) and
    /// CLOSES; the sibling requests 'HelveticaNeue-Medium', which resolves on this Windows box to a
    /// CFF-flavoured face and DECLINES under the documented v1 "TrueType substitute only" gate
    /// (<see cref="FontRemediationPlanner"/>'s "Decision 2" — CidToGid addresses glyph ids the way
    /// glyf/hmtx exposes them, not CFF charstrings). This is font-availability variance, not a
    /// defect: the SAME mechanism (<see cref="PdfDocumentEditor.ReplaceCompositeProgram"/>) both
    /// closes the resolvable finding and correctly declines the unresolvable one in the SAME apply
    /// pass, which is exactly what the reality gate exists to prove.
    /// </summary>
    [Theory]
    [InlineData("SCV~us~en~file=N0088673.pdf~gen~ref.pdf")]
    [InlineData("SCV~us~en~file=SCVTORQUEWRENCH.PDF~gen~ref.PDF")]
    public void Scv_documents_replace_the_resolvable_font_and_decline_its_TrueType_only_sibling(string file)
    {
        string? root = Corpus();
        Assert.SkipWhen(root is null, $"corpus not present at {DefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult before, int total) = ProposeFor(path);

        int beforeNotdef = NotdefCount(before);
        Assert.Equal(2, beforeNotdef);
        Assert.Empty(patches);
        Assert.Equal(total, replacements.Count + declines.Count);
        Assert.Single(replacements);
        DeclineProposal decline = Assert.Single(declines);
        Assert.Contains("is not a TrueType program", decline.Reason);

        int beforeWidth = WidthCount(before);
        Dictionary<string, int> beforeByRule = CountByRule(before);

        PreflightResult after = ApplyAndRecheck(path, replacements);

        int afterNotdef = NotdefCount(after);
        Assert.Equal(1, afterNotdef); // one closes, one declines — the measured shape, not zero.

        int afterWidth = WidthCount(after);
        Assert.True(afterWidth <= beforeWidth,
            $"{file}: width finding count ROSE after a replacement (before {beforeWidth}, after " +
            $"{afterWidth}) — the replacement program must already satisfy declared widths (spec §3).");

        // Every OTHER rule id must not get WORSE (rise, or newly appear) — but MAY improve, because
        // PdfDocumentEditor.Save() rewrites the whole file with its own canonical object framing,
        // which incidentally fixes any PRE-EXISTING indirect-object-spacing violations regardless of
        // what the edit itself targeted (measured: this doc's 32 indirect-object-spacing findings
        // drop to 0 purely as a Save() side effect — a general property of any edit-and-save round
        // trip through this engine, not something specific to program replacement). Equality would
        // be the wrong bar here; "never regresses" is the actual guarantee this gate can make.
        Dictionary<string, int> afterByRule = CountByRule(after);
        foreach ((string ruleId, int beforeCount) in beforeByRule)
        {
            if (ruleId == "font-program") continue; // covered by the notdef/width checks above
            afterByRule.TryGetValue(ruleId, out int afterCount);
            Assert.True(afterCount <= beforeCount,
                $"{file}: rule '{ruleId}' ROSE from {beforeCount} to {afterCount} after a program " +
                "replacement that should only have touched font-program 6.2.11.8 findings.");
        }
        foreach ((string ruleId, int afterCount) in afterByRule)
        {
            if (ruleId == "font-program" || beforeByRule.ContainsKey(ruleId)) continue;
            Assert.True(0 == afterCount,
                $"{file}: rule '{ruleId}' appeared ({afterCount}) after a program replacement that " +
                "should only have touched font-program 6.2.11.8 findings.");
        }
    }

    /// <summary>
    /// note §6 rows 3-6: the issue-34 reproducer (<c>0000_0000024.pdf</c>) and three "ordinary
    /// single-object CID2 swap" docs, all expected "Close". MEASURED reality on this machine: every
    /// one declines, for one of two documented, machine-specific reasons —
    /// <c>0000_0000024.pdf</c> (both fonts) and <c>6000_6000827.pdf</c> request 'AlArabiya', which
    /// resolves here to a CFF-flavoured face (the v1 "TrueType substitute only" gate, same as the
    /// SCV docs' sibling above); <c>0000_0000714.pdf</c> and <c>4000_4000802.pdf</c> resolve to
    /// DejaVu Sans / DejaVu Sans Light, each missing exactly one used glyph (a genuine coverage gap —
    /// "Pellucid makes no partial replacements"). <c>0000_0000024.pdf</c> and
    /// <c>6000_6000827.pdf</c> were independently checked against veraPDF
    /// (<c>verapdf.bat --format text -f 2b</c>, 2026-08-17): both report <c>FAIL ... 2b</c>, so the
    /// underlying PDF/A-2b violation is real — the decline is a font-availability fact about THIS
    /// machine, not a false finding.
    /// </summary>
    [Theory]
    [InlineData("0000_0000024.pdf")]
    [InlineData("0000_0000714.pdf")]
    [InlineData("4000_4000802.pdf")]
    [InlineData("6000_6000827.pdf")]
    public void Cc_main_close_candidates_decline_for_a_named_font_availability_reason(string file)
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.Empty(patches);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        Assert.Equal(total, replacements.Count + declines.Count);
        Assert.True(
            declines.All(d => d.Reason.Contains("is not a TrueType program")
                               || d.Reason.Contains("cannot honestly render")),
            $"{file}: expected every decline to cite either the TrueType-only gate or a coverage " +
            "gap; got: " + string.Join(" | ", declines.Select(d => d.Reason)));
    }

    /// <summary>
    /// note §6 row 8: the population's sole no-<c>/ToUnicode</c> doc (object 1424,
    /// <c>Type0CidType0</c>) — <see cref="FontRemediationPlanner.ProposeProgramReplace"/> declines
    /// before ever consulting a font provider (the <c>type0.ToUnicode is null</c> gate runs ahead of
    /// <c>fonts.Resolve</c>), so this decline is deterministic regardless of installed fonts.
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
        Assert.Equal(total, replacements.Count + declines.Count);
        Assert.True(declines.Any(d => d.Reason.Contains("ToUnicode")),
            $"{file}: expected a decline citing the missing /ToUnicode mapping; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
    }

    /// <summary>
    /// note §6 rows 9-10: simple-font (non-Type0) <c>.notdef</c> findings — one TrueType, one CFF —
    /// both out of v1 scope (<see cref="FontRemediationPlanner"/>'s <c>SimpleFontMissingGlyphReason</c>
    /// / charstrings-reason declines), deterministic regardless of installed fonts because the
    /// composite-kind gate runs before any font resolution.
    /// </summary>
    [Theory]
    [InlineData("0000_0000450.pdf")] // simple-TT notdef
    [InlineData("4000_4000993.pdf")] // 26 sibling simple-CFF objects, one base-name decline category
    public void Simple_font_documents_decline_as_out_of_v1_scope(string file)
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> _,
            PreflightResult _, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        Assert.Equal(total, replacements.Count + declines.Count);
    }

    /// <summary>
    /// note §6 row 7: <c>0000_0000769.pdf</c> — a CFF width finding (object 2032) keeps its
    /// PRE-EXISTING charstring decline (unrelated to this task; the same finding
    /// <see cref="FontProgramWidthRepairCorpusTests.Cff_documents_decline_with_the_charstring_reason"/>
    /// pins), alongside object 1424's composite <c>.notdef</c> finding (CID0, family
    /// 'AGaramond-Semibold'). MEASURED (2026-08-17): object 1424 ALSO declines — 'AGaramond-Semibold'
    /// resolves on this machine to a CFF-flavoured face, the same v1 "TrueType substitute only" gate
    /// the SCV/cc-main declines above cite — for a reason distinct from object 2032's charstring
    /// decline, so both categories are asserted explicitly (and asserted DIFFERENT) rather than one
    /// silently absorbing the other. This is the doc <see cref="FontProgramWidthRepairCorpusTests.Cff_documents_decline_with_the_charstring_reason"/>
    /// re-pins: that test's <c>Assert.Empty(patches)</c> / total-count shape already tolerates a
    /// second <see cref="DeclineProposal"/> (both proposals are the same record TYPE, just different
    /// reasons), so no assertion there needed to change — only its comment, documenting this measured
    /// fact for a future reader.
    /// </summary>
    [Fact]
    public void Mixed_document_declines_its_notdef_finding_for_a_different_reason_than_its_width_finding()
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "0000_0000769.pdf";
        string path = Path.Combine(root!, file);
        (List<ReplaceProgramProposal> replacements, List<DeclineProposal> declines, List<PatchWidthsProposal> patches,
            PreflightResult before, int total) = ProposeFor(path);

        Assert.Empty(replacements);
        Assert.Empty(patches);
        Assert.Equal(total, replacements.Count + declines.Count);
        Assert.Equal(2, declines.Count);

        // "charstrings" alone is NOT a safe discriminator: the notdef decline's own reason text also
        // says "...without rewriting CFF charstrings" — the fuller, width-decline-specific phrase
        // below is the one substring unique to ProposeWidthPatch's CFF/CID0 branch.
        DeclineProposal? widthDecline =
            declines.SingleOrDefault(d => d.Reason.Contains("stores its advances in CFF charstrings"));
        Assert.True(widthDecline is not null,
            $"{file}: expected the pre-existing CFF width finding's charstring decline; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));

        DeclineProposal? notdefDecline =
            declines.SingleOrDefault(d => d.Reason.Contains("is not a TrueType program"));
        Assert.True(notdefDecline is not null,
            $"{file}: expected the notdef finding's TrueType-only decline; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
        Assert.NotEqual(widthDecline!.Font.ObjectNumber, notdefDecline!.Font.ObjectNumber);

        // No replacements resulted, so font-program's own counts must hold exactly (nothing was
        // applied to touch them). Every OTHER rule id must not get WORSE — but, as the SCV theory's
        // comment documents, MAY improve as a Save() round-trip side effect (canonical object framing
        // incidentally fixing pre-existing indirect-object-spacing violations) even with an empty
        // replacement set, since Save() rewrites the whole file regardless of what changed.
        Dictionary<string, int> beforeByRule = CountByRule(before);
        PreflightResult after = ApplyAndRecheck(path, replacements);
        Dictionary<string, int> afterByRule = CountByRule(after);
        afterByRule.TryGetValue("font-program", out int afterFontProgram);
        beforeByRule.TryGetValue("font-program", out int beforeFontProgram);
        Assert.Equal(beforeFontProgram, afterFontProgram);
        foreach ((string ruleId, int beforeCount) in beforeByRule)
        {
            if (ruleId == "font-program") continue;
            afterByRule.TryGetValue(ruleId, out int afterCount);
            Assert.True(afterCount <= beforeCount,
                $"{file}: rule '{ruleId}' ROSE from {beforeCount} to {afterCount} despite no " +
                "replacements being applied.");
        }
        foreach ((string ruleId, int afterCount) in afterByRule)
        {
            if (ruleId == "font-program" || beforeByRule.ContainsKey(ruleId)) continue;
            Assert.True(0 == afterCount,
                $"{file}: rule '{ruleId}' appeared ({afterCount}) despite no replacements being applied.");
        }
    }
}
