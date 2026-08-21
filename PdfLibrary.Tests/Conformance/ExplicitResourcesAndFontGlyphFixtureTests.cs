using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// The corpus-oracle deliverable promised by the spec
/// (<c>Docs/superpowers/specs/2026-08-20-explicit-resources-and-font-glyph-parity-design.md</c>, "Test
/// strategy"): "Each closed file gets a <c>LocalOnly</c> assertion that we now flag it, and the three
/// <c>6-2-2-t04-pass-*</c> files get explicit no-false-positive assertions." No such assertion existed
/// anywhere before this file (whole-branch review finding I3) — the fixture names appeared only in
/// comments across <c>PdfLibrary.Tests/</c>.
///
/// <para>Clause 6.2.2 is protected today only by accident, via its <c>ParityFullClauses</c> entry in
/// <see cref="CorpusOracleTests"/>. Clauses 6.2.11.8 (7/8) and 6.2.11.4.1 (8/11) are NOT full-parity
/// clauses, so without this file the font cluster's 5 detections were protected only by the aggregate
/// 986/986 whole-file verdict count in <see cref="ParityReportTests"/> — a count that one lost
/// detection plus one unrelated gain elsewhere would leave unchanged. Each fact below asserts the
/// SPECIFIC clause this branch's own fix made fire, not merely "something fired", so a regression that
/// silently swaps one detection for an unrelated one on the same file still fails the test.</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class ExplicitResourcesAndFontGlyphFixtureTests(ITestOutputHelper output)
{
    private static string? CorpusFixture(ConformanceProfile profile, string needle) =>
        !CorpusHarness.IsAvailable
            ? null
            : CorpusHarness.AllPdfPaths(profile).FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private static Finding[] ErrorsOf(string path, ConformanceProfile profile, string ruleId) =>
        Preflighter.Check(path, profile).Errors.Where(f => f.RuleId == ruleId).ToArray();

    private void Dump(string label, Finding[] findings)
    {
        output.WriteLine($"{label}: {findings.Length} finding(s)");
        foreach (Finding f in findings)
            output.WriteLine($"  [{ParitySnapshot.ClauseKey(f.Clause)}] {f.Message}");
    }

    // ── Job A — ExplicitResourcesRule, clause 6.2.2 test 2 (Task 5) ───────────────────────────────

    [Theory]
    [InlineData("6-2-2-t04-fail-d")] // Type3 font, direct /Resources absent, charprocs reference /CS0
    [InlineData("6-2-2-t04-fail-e")] // Form XObject, direct /Resources absent, /CS0 cs
    [InlineData("6-2-2-t04-fail-f")] // Page /Resources absent; ancestor Pages node holds /X0; /X0 Do
    public void Explicit_resources_fail_fixture_is_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = ErrorsOf(path!, ConformanceProfile.PdfA2b, "explicit-resources");
        Dump(needle, findings);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("6.2.2", ParitySnapshot.ClauseKey(f.Clause)));
    }

    [Theory]
    [InlineData("6-2-2-t04-pass-a")] // Type3 font, absent /Resources, but no NAMED reference (d1 only)
    [InlineData("6-2-2-t04-pass-b")] // Form XObject, absent /Resources, device colour only (rg)
    [InlineData("6-2-2-t04-pass-c")] // page/form both carry their own resources; inner form absent but unused
    public void Explicit_resources_pass_fixture_produces_no_false_positive(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        PreflightResult result = Preflighter.Check(path!, ConformanceProfile.PdfA2b);
        Dump(needle, result.Errors.ToArray());

        // The strict, whole-file reading: a -pass- fixture must conform, not merely stay silent on
        // explicit-resources specifically — that is what "no false positive" means for this invariant.
        Assert.True(result.Conforms, $"{needle} is a conformant fixture but PdfLibrary rejected it.");
    }

    // ── Job B — font cluster (FontProgramRule), clauses 6.2.11.4.1 / 6.2.11.8 (Task 7) ────────────

    [Theory]
    [InlineData("6-2-11-4-1-t02-fail-a")] // B1: WinAnsi ASCII name now document-asserted -> glyph-present
    [InlineData("6-2-11-4-1-t02-fail-b")] // B1: custom /Differences name, same fix
    public void Font_cluster_glyph_present_fail_fixture_is_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = ErrorsOf(path!, ConformanceProfile.PdfA2b, "font-program");
        Dump(needle, findings);

        // Asserting the SPECIFIC clause this branch's B1 fix made fire — not merely "something fired" —
        // so a regression that drops this detection while gaining an unrelated one on the same file
        // (e.g. a coincidental metrics finding) still fails this test.
        Assert.Contains(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.4.1");
    }

    [Theory]
    [InlineData("6-2-11-8-t01-fail-a")] // B2 CFF arm: no glyph name, nonsymbolic, CFF built-in encoding miss
    [InlineData("6-2-11-8-t01-fail-b")] // B2 TrueType arm: no glyph name is sufficient on its own
    [InlineData("6-2-11-4-1-t02-fail-e")] // B3: an incomplete final Identity-H composite code is .notdef
    public void Font_cluster_notdef_fail_fixture_is_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = ErrorsOf(path!, ConformanceProfile.PdfA2b, "font-program");
        Dump(needle, findings);

        Assert.Contains(findings, f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.8");
    }
}
