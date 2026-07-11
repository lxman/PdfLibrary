using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 29 — CIDFontType0 advance-width metrics (<c>font-program</c>, 6.2.11.5 / 7.21.5). Extends the
/// width check from CIDFontType2-only to both Type0 descendant kinds, resting on the CFF defaultWidthX
/// parser fix (per the CFF spec an omitted CharString width is the FD's defaultWidthX, not nominalWidthX —
/// the old confusion made omitted-width glyphs diverge by hundreds of units, which is why CFF-keyed fonts
/// were excluded from the check). Two real-file oracles pin the two directions the fix must get right —
/// LocalOnly, since neither the veraPDF corpus nor the PDF Association reference set is vendored:
/// <list type="bullet">
///   <item><b>detection</b> — the corpus's only CIDFontType0 width-fail fixture (6-2-11-5-t01-fail-e) is
///     now flagged at 6.2.11.5 by <c>font-program</c> itself, not merely caught by some other rule;</item>
///   <item><b>no false positive</b> — PDFUA-Ref-2-08 (a conformant MinionPro CIDFontType0/CFF book chapter
///     that diverged 227 units under the old bug) now round-trips inside the 10-unit tolerance, so
///     <c>font-program</c> stays silent on it under both PDF/A-2b and PDF/UA-1.</item>
/// </list>
/// The synthetic TrueType/CIDFontType2 metrics logic and profile-aware clause mapping are pinned by
/// <see cref="PreflightSlice19Tests"/>; this slice adds the CFF-keyed real-font evidence.
/// </summary>
[Trait("Category", "LocalOnly")]
public class PreflightSlice29Tests(ITestOutputHelper output)
{
    private static Finding[] FontProgram(string path, ConformanceProfile profile) =>
        Preflighter.Check(path, profile).Findings.Where(f => f.RuleId == "font-program").ToArray();

    private static string? CorpusFixture(ConformanceProfile profile, string needle) =>
        !CorpusHarness.IsAvailable
            ? null
            : CorpusHarness.AllPdfPaths(profile).FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private static string? ReferenceFile(string needle) =>
        !PdfUaReferenceHarness.IsAvailable
            ? null
            : PdfUaReferenceHarness.Files().FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private void Dump(string label, Finding[] findings)
    {
        output.WriteLine($"{label}: {findings.Length} font-program finding(s)");
        foreach (Finding f in findings)
            output.WriteLine($"  [{ParitySnapshot.ClauseKey(f.Clause)}] {f.Message}");
    }

    [Fact]
    public void CidFontType0_inconsistent_width_is_detected()
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, "6-2-11-5-t01-fail-e");
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = FontProgram(path!, ConformanceProfile.PdfA2b);
        Dump("fail-e (CIDFontType0)", findings);

        Finding f = Assert.Single(findings);
        Assert.Equal("6.2.11.5", ParitySnapshot.ClauseKey(f.Clause));
        Assert.Contains("advance width", f.Message);
    }

    [Fact]
    public void CidFontType2_inconsistent_width_still_detected()
    {
        // Regression guard: the pre-existing CIDFontType2 path must keep firing after the widening.
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, "6-2-11-5-t01-fail-f");
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = FontProgram(path!, ConformanceProfile.PdfA2b);
        Dump("fail-f (CIDFontType2)", findings);

        Finding f = Assert.Single(findings);
        Assert.Equal("6.2.11.5", ParitySnapshot.ClauseKey(f.Clause));
        Assert.Contains("advance width", f.Message);
    }

    [Fact]
    public void Conformant_cidfonttype0_cff_round_trips_with_no_false_positive()
    {
        string? path = ReferenceFile("2-08");
        Assert.SkipUnless(path is not null, "PDFUA-Reference-Files not present (set PDFUA_REFERENCE_FILES)");

        Finding[] ua = FontProgram(path!, ConformanceProfile.PdfUA1);
        Finding[] a2b = FontProgram(path!, ConformanceProfile.PdfA2b);
        Dump("PDFUA-Ref-2-08 / UA1", ua);
        Dump("PDFUA-Ref-2-08 / A2b", a2b);

        Assert.Empty(ua);
        Assert.Empty(a2b);
    }
}
