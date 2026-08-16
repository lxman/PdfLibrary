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
    public void Cid2_document_declines_on_a_pre_existing_cidtogidmap_defect()
    {
        // 0000_0000024.pdf was pinned "composite CID2 close" from the 2026-08-16/15f scans, but
        // diagnosis here (2026-08-16) shows it cannot close: BOTH fonts (obj 4 'AAAAAC+AlArabiya',
        // obj 7 'AAAAAF+AlArabiya,Italic') decline entirely, not for a width-genuine reason but for
        // "a patched glyph id lies beyond the program's glyph count" — SfntAdvancePatcher's own
        // safety guard (deliberately tested: SfntAdvancePatcherTests
        // .A_gid_at_or_beyond_numGlyphs_fails_rather_than_writes) correctly refusing to write an
        // advance for a glyph id that does not exist in the embedded program (maxp.numGlyphs=949).
        //
        // Root cause, confirmed by direct inspection: both fonts carry a real (non-Identity)
        // /CIDToGIDMap STREAM of 65536 entries — so it fully covers the used CIDs (which run up to
        // 8221, well inside the stream's range) — but PdfLibrary.Fonts.CidFont.LoadCidToGidMap
        // (pre-existing; last touched at `eb963f2`/`b1c9e90`, issue-24 work, well before this
        // branch's Task 1 `b88349a`) only stores NON-ZERO entries in its lookup dictionary
        // ("// Only store non-zero mappings"). CidFont.MapCidToGid then falls through to
        // "// Default: assume identity mapping" (`return cid;`) whenever a CID is absent from that
        // dictionary — which is true both for a CID truly outside the map's range AND for a CID the
        // map explicitly assigns to GID 0 (a legitimate .notdef declaration). For the punctuation
        // CIDs this document actually draws (8211/8216/8217/8220/8221 — em-dash/curly-quote code
        // points used directly as CIDs), the map's real answer is GID 0, but MapCidToGid instead
        // returns the CID itself (8217, 8221, ...) as a bogus "identity" GID — far beyond the
        // program's real 949 glyphs. ProgramWidthResolver.Composite has its own `gid == 0` skip
        // for exactly this "no meaningful width to compare" case, but it never gets the chance: the
        // gid it receives is already wrong by the time it gets there.
        //
        // This is a genuine defect, but NOT in Tasks 1-4 code — CidFont.MapCidToGid predates F-4a
        // entirely and Task 1 only extracted ProgramWidthResolver verbatim from FontProgramRule (no
        // behavior change), so the same miscomputed gid would already have produced this document's
        // ORIGINAL 6.2.11.5 finding before this branch existed. Filed as issue 34 in
        // Pellucid/docs/ISSUE-TRACKER.md rather than fixed here (out of this task's scope, and the
        // brief forbids engine changes beyond this test file). SfntAdvancePatcher's bounds check is
        // exactly why this surfaces as an honest decline instead of a corrupted hmtx write — the
        // safety net worked as designed.
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
        Assert.True(declines.All(d => d.Reason.Contains("beyond the program's glyph count")),
            $"{file}: expected every decline to cite the glyph-count guard; got: " +
            string.Join(" | ", declines.Select(d => d.Reason)));
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
}
