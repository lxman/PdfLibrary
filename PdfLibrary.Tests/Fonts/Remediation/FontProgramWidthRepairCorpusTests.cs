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
        // name), 2026-08-17: 66 width findings before (was ~4 patchable + 1 CID0 decline pre-fix
        // — F-4a Task 8's "1 partial" bucket for this doc). Per-object dedup surfaces 52 of those
        // 66 as proposable and 14 as CFF-charstring declines (52 + 14 = 66, reconciles). But the
        // 52 proposals are NOT 52 independent physical patches: FontInventory groups them onto
        // only 4 distinct program HOLDERS (4 physical /FontFile2 CIDFontType2 programs, each
        // shared by many separate Type0 wrapper objects on different pages/text runs — 16, 16, 12,
        // and 8 sibling findings per holder). FontRemediationPlanner proposes one
        // PatchWidthsProposal PER LOGICAL FONT regardless of holder sharing (its `seen` dedup at
        // line 90 is keyed by (Id, ProgramHolderId, RuleId), not by ProgramHolderId alone — by
        // design, per that method's own doc comment, for an unrelated FontId(0)-collision reason).
        // PdfDocumentEditor.ReplaceProgramBytes performs a full-stream REPLACE of the holder's
        // /FontFile2 on every call (FontDescriptor.Set, not a merge) — so when ApplyAndRecheck
        // applies all 52 proposals in sequence, every proposal after the first one FOR THE SAME
        // HOLDER overwrites its predecessor's patched bytes wholesale. Only the last-applied
        // proposal per holder actually survives; the other 52 - 4 = 48 are silently discarded.
        // Measured result: 46 findings remain after applying all 52 patches — the 14 legitimate
        // CFF/CID0 declines (never touched) plus 32 of the 52 patch-targeted findings whose own
        // proposal was clobbered by a later sibling's proposal on the same shared holder (only 20
        // of the 52 patch-targeted findings actually close: some incidentally, because the
        // surviving per-holder patch happens to also fix the same glyphs a sibling finding needed).
        // 14 + 32 = 46 reconciles; 66 - 46 = 20 genuinely closed. This is a real defect in the F-4a
        // remediation pipeline (the planner does not merge multiple proposals that target one
        // physical program holder before apply) — invisible before this task because the pre-fix
        // by-base-font-name dedup almost always collapsed this doc's many sibling Type0 wrappers
        // down to ~1 finding per holder, so the collision never arose. Out of scope for the
        // Task 2 rule-level dedup fix (FontProgramRule.DedupKey) to correct; flagged for the
        // planner/apply layer. The assertions below stay relative (before/after, not hardcoded
        // totals) so they do not need re-pinning every time the corpus or detection surface
        // shifts; this comment records the measured shape and the reconciled mechanism.
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
            "— at minimum the CID0 font's finding should legitimately remain while some CID2 " +
            "findings close; MOST remaining findings here are actually CID2 findings clobbered by " +
            "a same-holder sibling's patch, not legitimate CID0 holdouts (see the comment above).");
        Assert.True(afterWidthCount > 0,
            $"{file}: expected at least the CID0 font's width finding to legitimately remain, but all cleared.");

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
        // walk instead raises the honest 6.2.11.8 (.notdef) finding for both fonts.
        //
        // F-4b Task 5 re-pin: a 6.2.11.8 finding now dispatches to ProposeProgramReplace (whole-face
        // swap), not the old "missing glyph, not a width mismatch" width-patch decline — this document
        // still declines, since ProposeFor's StubFontProvider(null) never resolves a substitute for
        // either AlArabiya font, but now for THAT reason ("no font matching '...' is installed").
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
        Assert.True(declines.All(d => d.Reason.Contains("is installed on this computer")),
            $"{file}: expected every decline to cite the whole-program-replace no-substitute-installed " +
            "reason; got: " + string.Join(" | ", declines.Select(d => d.Reason)));
    }

    // F-4b Task 9 re-pin (2026-08-17, corrected in the F-4b final whole-branch review — the PRIOR
    // version of this comment named a since-renamed test and asserted a decline reason that no longer
    // holds post-retry): 0000_0000769.pdf's composite .notdef finding (object 1424, AGaramond-Semibold)
    // now ALSO routes through the planner (FontRemediationPlanner.Propose -> ProposeProgramReplace,
    // landed this program) and itself DECLINES here. THIS test's ProposeFor (above) constructs its
    // planner with `new FontRemediationPlanner(new StubFontProvider(null))` — StubFontProvider(null)
    // resolves NO face for ANY request — so object 1424's decline here is the plain "no font matching
    // '...' is installed on this computer" branch (fonts.Resolve returns null before any format
    // classification happens). This is NOT what happens for THIS SAME document's THIS SAME object under
    // a REAL font provider: `FontProgramReplaceCorpusTests.Mixed_document_closes_its_notdef_finding_
    // and_keeps_its_unrelated_width_decline` (renamed from the earlier "declines for a different
    // reason" name once the fix-round-1 synthetic-retry mitigation landed) uses a REAL provider
    // (EmbedProgramRoundTripTests.DeterministicFonts) against this machine's actual installed fonts,
    // and measures object 1424 CLOSING post-retry (resolving to Liberation Serif Bold), not declining
    // at all — a different planner construction produces a genuinely different outcome for the same
    // finding, not just a different decline reason. Both proposals here (in THIS test) are still
    // DeclineProposal, so Assert.Empty(patches) and the total-count formula below already admit the
    // new proposal kind without any assertion change — this comment documents the measured fact for a
    // future reader, per the F-4b Task 9 brief's own "re-pin" instruction.
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
