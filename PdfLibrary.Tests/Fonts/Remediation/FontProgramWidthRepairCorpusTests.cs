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
/// Task 5 (F-4a width remediation, spec 2026-08-16-f4a-width-remediation): the REALITY GATE that
/// runs the Tasks 1-4 patcher pipeline end to end against seven real-world documents pinned by the
/// 2026-08-16/15f scans and the FontKind probe. Corpus files are READ-ONLY — every apply happens
/// against a temp copy, never the corpus file itself.
///
/// <para>LocalOnly: the corpus exists only on the dev box (and, mounted, on self-hosted runners —
/// the trait is the only thing keeping this out of CI; see .github/workflows filters), mirroring
/// <see cref="Conformance.WidthFalsePositiveCorpusTests"/>'s own discipline.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class FontProgramWidthRepairCorpusTests
{
    private const string CorpusVariable = "PDFLIBRARY_LOCAL708_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\local-708";

    private const string CcMainCorpusVariable = "PDFLIBRARY_CCMAIN_CORPUS";
    private const string CcMainDefaultCorpus = @"D:\PdfCorpora\real-world\cc-main-2021-31-sample";

    // Corpus resolution copied verbatim from WidthFalsePositiveCorpusTests' Corpus()/CcMainCorpus().
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

    private static (List<PatchWidthsProposal> Patches, List<DeclineProposal> Declines, PreflightResult Before,
        int TotalProposals) ProposeFor(string path)
    {
        PreflightResult before = Preflighter.Check(path, ConformanceProfile.PdfA2b);
        using PdfDocument doc = PdfDocument.Load(path);
        var planner = new FontRemediationPlanner(new StubFontProvider(null));
        FontRemediationProposal proposed = planner.Propose(doc,
            before.Findings.Where(f => f.RuleId == "font-program" && f.ObjectNumber is not null)
                .Select(f => (f.RuleId, f.ObjectNumber!.Value)));
        return (proposed.Fonts.OfType<PatchWidthsProposal>().ToList(),
                proposed.Fonts.OfType<DeclineProposal>().ToList(), before, proposed.Fonts.Count);
    }

    // ProposeFor and ApplyAndRecheck each load the document independently, but PatchWidthsProposal
    // carries only object numbers + patched bytes (no in-memory object handle), so a proposal
    // produced against one load transfers cleanly onto a second load of the SAME bytes — the corpus
    // files are read-only, so nothing changes between the two loads.
    private static PreflightResult ApplyAndRecheck(string path, IEnumerable<PatchWidthsProposal> patches)
    {
        using PdfDocument doc = PdfDocument.Load(path);
        using PdfDocumentEditor editor = doc.Edit();
        foreach (PatchWidthsProposal patch in patches)
            editor.ReplaceProgramBytes(patch.Font, patch.PatchedProgram);
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

    [Theory]
    [InlineData("local", "PowerBASIC Compiler for Windows v10.0.pdf")]
    [InlineData("local", "PowerBASIC Console Compiler v6.0.pdf")]
    [InlineData("ccmain", "0000_0000027.pdf")]
    public void Close_documents_lose_every_width_finding_and_keep_every_other_count(string corpus, string file)
    {
        string? root = corpus == "local" ? Corpus() : CcMainCorpus();
        string defaultPath = corpus == "local" ? DefaultCorpus : CcMainDefaultCorpus;
        Assert.SkipWhen(root is null, $"corpus not present at {defaultPath} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<PatchWidthsProposal> patches, List<DeclineProposal> declines, PreflightResult before, int _) =
            ProposeFor(path);

        Assert.True(patches.Count > 0, $"{file}: expected at least one PatchWidthsProposal, got none " +
            (declines.Count > 0
                ? $"(declines: {string.Join(" | ", declines.Select(d => d.Reason))})"
                : "(no proposals at all)"));

        int beforeWidthCount = before.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        int beforeOtherFontProgramCount = before.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) != "6.2.11.5");
        Dictionary<string, int> beforeByRule = CountByRule(before);

        PreflightResult after = ApplyAndRecheck(path, patches);

        int afterWidthCount = after.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Assert.True(afterWidthCount == 0,
            $"{file}: {afterWidthCount} width finding(s) remain after patching (had {beforeWidthCount} before).");

        int afterOtherFontProgramCount = after.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) != "6.2.11.5");
        Assert.Equal(beforeOtherFontProgramCount, afterOtherFontProgramCount);

        Dictionary<string, int> afterByRule = CountByRule(after);
        foreach ((string ruleId, int beforeCount) in beforeByRule)
        {
            if (ruleId == "font-program") continue; // covered by the width/other split above
            afterByRule.TryGetValue(ruleId, out int afterCount);
            Assert.True(beforeCount == afterCount,
                $"{file}: rule '{ruleId}' moved from {beforeCount} to {afterCount} after a width patch " +
                "that should only have touched font-program 6.2.11.5 findings.");
        }
        foreach ((string ruleId, int afterCount) in afterByRule)
        {
            if (ruleId == "font-program" || beforeByRule.ContainsKey(ruleId)) continue;
            Assert.True(0 == afterCount,
                $"{file}: rule '{ruleId}' appeared ({afterCount}) after a width patch that should only " +
                "have touched font-program 6.2.11.5 findings.");
        }
    }

    [Fact]
    public void Mixed_document_patches_cid2_and_declines_cid0()
    {
        // Re-measured post-issue-35 (FontProgramRule now dedups per font OBJECT, not base-font
        // name): 66 width findings before, 52 patch proposals / 14 charstring declines, 46
        // remaining after applying every patch (2026-08-17). The pre-fix run would have
        // undercounted here — sibling indirect objects sharing a /BaseFont previously collapsed
        // onto one finding/proposal apiece — so a rise from whatever this test saw before issue
        // 35 is the fix working as intended, not a regression. The assertions below stay
        // relative (before/after, not hardcoded totals) specifically so they do not need
        // re-pinning every time the corpus or the rule's detection surface shifts; this comment
        // records the measured shape for the record.
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "0000_0000522.pdf";
        string path = Path.Combine(root!, file);
        (List<PatchWidthsProposal> patches, List<DeclineProposal> declines, PreflightResult before, int _) =
            ProposeFor(path);

        Assert.True(patches.Count > 0, $"{file}: expected at least one patch proposal (its CID2 fonts).");
        Assert.True(declines.Any(d => d.Reason.Contains("charstrings")),
            $"{file}: expected at least one decline citing charstrings (its CID0 font); got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));

        int beforeWidthCount = before.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Dictionary<string, int> beforeByRule = CountByRule(before);

        PreflightResult after = ApplyAndRecheck(path, patches);

        int afterWidthCount = after.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Assert.True(afterWidthCount < beforeWidthCount,
            $"{file}: width findings did not drop (before {beforeWidthCount}, after {afterWidthCount}) " +
            "— the CID0 font's finding should legitimately remain, but the CID2 fonts' should not.");
        Assert.True(afterWidthCount > 0,
            $"{file}: expected the CID0 font's width finding to legitimately remain, but all cleared.");

        Dictionary<string, int> afterByRule = CountByRule(after);
        foreach ((string ruleId, int beforeCount) in beforeByRule)
        {
            if (ruleId == "font-program") continue;
            afterByRule.TryGetValue(ruleId, out int afterCount);
            Assert.True(beforeCount == afterCount,
                $"{file}: rule '{ruleId}' moved from {beforeCount} to {afterCount} after a width patch.");
        }
        // Same new-ruleId guard the close-doc test has: without this, a width patch that introduced
        // a brand-new RuleId not present before would slip through silently, since the loop above
        // only walks beforeByRule's keys.
        foreach ((string ruleId, int afterCount) in afterByRule)
        {
            if (ruleId == "font-program" || beforeByRule.ContainsKey(ruleId)) continue;
            Assert.True(0 == afterCount,
                $"{file}: rule '{ruleId}' appeared ({afterCount}) after a width patch that should only " +
                "have touched font-program 6.2.11.5 findings.");
        }
    }

    [Fact]
    public void Cid2_document_with_explicit_gid0_map_reports_notdef_not_width()
    {
        // 0000_0000024.pdf was pinned "composite CID2 close" from the 2026-08-16/15f scans, then
        // re-pinned (2026-08-16) to "declines on the SfntAdvancePatcher glyph-count guard" once
        // diagnosis found the real cause: PdfLibrary.Fonts.CidFont.LoadCidToGidMap only stored
        // NON-ZERO entries, so CidFont.MapCidToGid could not tell "CID outside the map's range"
        // (identity fallback, correct) from "CID the map explicitly sends to GID 0" (a legitimate
        // .notdef declaration) — both fell through to `return cid;`, producing a bogus non-zero
        // "identity" GID for the punctuation CIDs this document actually draws (8211/8216/8217/
        // 8220/8221 — em-dash/curly-quote code points used directly as CIDs). Filed as issue 34.
        //
        // Issue 34 fixed (this task): MapCidToGid now returns the honest 0 for a CID inside the
        // map stream's covered range with no stored entry. Both fonts' punctuation CIDs now
        // resolve to gid 0, so ProgramWidthResolver.Composite's own `gid == 0` skip removes them
        // from the width comparison (the spurious "far beyond the program's glyph count" width
        // patch/decline this test used to pin is gone), and FontProgramRule.CheckType0's `gid == 0`
        // walk instead raises the honest 6.2.11.8 (.notdef) finding for both fonts. The planner has
        // no remediation for a missing-glyph finding, so it still declines — but now for the true
        // reason ("missing glyph, not a width mismatch"), not a corrupted-gid side effect.
        //
        // veraPDF oracle (`verapdf.bat --format json -f 2b 0000_0000024.pdf`, 2026-08-17): 4 failed
        // rules total — 6.6.4 (missing PDF/A Identification), 6.2.11.4.1 x2 checks (Helvetica-Bold
        // / Helvetica-Oblique NOT embedded — unrelated simple fonts, not the AlArabiya composites),
        // and 6.2.4.3 x2 test numbers (DeviceGray/DeviceRGB without an OutputIntent). Neither
        // 6.2.11.5 nor 6.2.11.8 appears in veraPDF's report for the AlArabiya composite fonts —
        // veraPDF's own PDF/A-2b profile does not reach a composite-font glyph-existence check this
        // deep for this document, so it is not an independent corroborating oracle for THIS specific
        // finding; the fix is verified directly against the corrected CidFont.MapCidToGid behavior
        // and CidToGidMapExplicitZeroTests instead.
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        const string file = "0000_0000024.pdf";
        string path = Path.Combine(root!, file);
        (List<PatchWidthsProposal> patches, List<DeclineProposal> declines, PreflightResult _, int total) =
            ProposeFor(path);

        Assert.Empty(patches);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        // Direct check that every proposal is one of the two kinds above — "patches empty" alone only
        // proves nothing patched; it says nothing about a third proposal kind slipping through unseen.
        Assert.Equal(total, patches.Count + declines.Count);
        Assert.True(declines.All(d => d.Reason.Contains("missing glyph, not a width mismatch")),
            $"{file}: expected every decline to cite the missing-glyph (.notdef) reason, not a width " +
            "mismatch; got: " + string.Join(" | ", declines.Select(d => d.Reason)));
    }

    [Theory]
    [InlineData("0000_0000769.pdf")]
    [InlineData("0000_0000293.pdf")]
    public void Cff_documents_decline_with_the_charstring_reason(string file)
    {
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<PatchWidthsProposal> patches, List<DeclineProposal> declines, PreflightResult _, int total) =
            ProposeFor(path);

        Assert.Empty(patches);
        Assert.True(declines.Count > 0, $"{file}: expected at least one DeclineProposal, got none.");
        // Direct check that every proposal is one of the two kinds above — "patches empty" alone only
        // proves nothing patched; it says nothing about a third proposal kind slipping through unseen.
        Assert.Equal(total, patches.Count + declines.Count);
        Assert.True(declines.Any(d => d.Reason.Contains("charstrings")),
            $"{file}: expected at least one decline citing charstrings; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
    }

    [Fact]
    public void Two_sibling_objects_each_get_their_own_patch_and_close()
    {
        // Issue 35's cc-main reproducer: F-4a Task 8 bucketed this document "unchanged" because
        // the per-base-font-name dedup reported only ONE 6.2.11.5 finding for a pair of sibling
        // font objects (holders 39 and 43) sharing a /BaseFont — so the planner only ever
        // proposed one patch, and re-preflighting after applying it still saw the sibling's own
        // (never-patched) finding. Post-fix (measured 2026-08-17): TWO findings, TWO
        // PatchWidthsProposals, one per holder — applying both clears every 6.2.11.5 finding.
        const string file = "2000_2000807.pdf";
        string? root = CcMainCorpus();
        Assert.SkipWhen(root is null, $"corpus not present at {CcMainDefaultCorpus} (LocalOnly)");

        string path = Path.Combine(root!, file);
        (List<PatchWidthsProposal> patches, List<DeclineProposal> declines, PreflightResult before, int total) =
            ProposeFor(path);

        int beforeWidthCount = before.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Assert.Equal(2, beforeWidthCount);
        Assert.Empty(declines);
        Assert.Equal(2, total);
        Assert.Equal(2, patches.Count);
        Assert.Equal([39, 43], patches.Select(p => p.Font.ObjectNumber).OrderBy(n => n).ToArray());

        PreflightResult after = ApplyAndRecheck(path, patches);
        int afterWidthCount = after.Findings.Count(
            f => f.RuleId == "font-program" && ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5");
        Assert.Equal(0, afterWidthCount);
    }
}
