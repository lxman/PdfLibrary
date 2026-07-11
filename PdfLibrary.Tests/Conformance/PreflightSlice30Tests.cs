using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 30 — simple-CFF advance-width metrics (<c>font-program</c>, 6.2.11.5 / 7.21.5). Extends the simple-font
/// width check from TrueType-only to simple CFF (Type1C / FontFile3) fonts, the last non-Type3 simple embedding
/// the clause reaches. The advance is resolved the CFF way — code → glyph name (via the PDF <c>/Encoding</c>,
/// including <c>/Differences</c>) → GID (via the CFF charset, <c>GetGlyphIdByName</c>) → CharString advance
/// (<c>GetAdvanceWidth(gid)</c>, the same defaultWidthX-aware path slice 29 validated for CIDFontType0). It must
/// NOT go through <c>GetAdvanceWidthByName</c>, which resolves only real Type1 programs (<c>_type1Parser</c>) and
/// hard-codes 500 for CFF — feeding that into the check would itself be the false positive. A code whose glyph
/// name does not resolve to a charset GID is skipped, never guessed at.
/// <para>
/// Two real-file oracles pin the two directions, LocalOnly (neither corpus nor reference set is vendored):
/// </para>
/// <list type="bullet">
///   <item><b>detection</b> — the corpus's two simple-CFF width-fail fixtures, 6-2-11-5-t01-fail-a
///     (WinAnsi, standard names) and -fail-b (custom <c>/Differences</c> names), are now flagged at 6.2.11.5 by
///     <c>font-program</c> itself. fail-c is Type3 (out of scope), fail-d TrueType, fail-e/-f Type0 — already
///     covered by earlier slices;</item>
///   <item><b>no false positive</b> — PDFUA-Ref-2-04 (a conformant SourceSansPro simple-CFF/WinAnsi
///     presentation) stays silent under both PDF/UA-1 and PDF/A-2b.</item>
/// </list>
/// The clause's only PDF/UA-1 fail fixture (7.21.5-t01-fail-a) is TrueType, so this slice adds detection to
/// PDF/A-2b only (fail-a + fail-b); the PdfUA1 floor is unchanged. Profile-aware clause mapping and the
/// TrueType/Type0 paths are pinned by <see cref="PreflightSlice19Tests"/> and <see cref="PreflightSlice29Tests"/>.
/// </summary>
[Trait("Category", "LocalOnly")]
public class PreflightSlice30Tests(ITestOutputHelper output)
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

    [Theory]
    [InlineData("6-2-11-5-t01-fail-a")] // simple CFF, WinAnsi (standard glyph names)
    [InlineData("6-2-11-5-t01-fail-b")] // simple CFF, custom /Differences glyph names
    public void SimpleCff_inconsistent_width_is_detected(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = FontProgram(path!, ConformanceProfile.PdfA2b);
        Dump($"{needle} (simple CFF)", findings);

        Finding f = Assert.Single(findings);
        Assert.Equal("6.2.11.5", ParitySnapshot.ClauseKey(f.Clause));
        Assert.Contains("advance width", f.Message);
    }

    [Fact]
    public void Conformant_simple_cff_round_trips_with_no_false_positive()
    {
        string? path = ReferenceFile("2-04");
        Assert.SkipUnless(path is not null, "PDFUA-Reference-Files not present (set PDFUA_REFERENCE_FILES)");

        Finding[] ua = FontProgram(path!, ConformanceProfile.PdfUA1);
        Finding[] a2b = FontProgram(path!, ConformanceProfile.PdfA2b);
        Dump("PDFUA-Ref-2-04 / UA1", ua);
        Dump("PDFUA-Ref-2-04 / A2b", a2b);

        Assert.Empty(ua);
        Assert.Empty(a2b);
    }
}
