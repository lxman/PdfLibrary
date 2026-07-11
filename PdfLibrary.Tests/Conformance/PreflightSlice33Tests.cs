using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 33 — Separation and DeviceN colour spaces for PDF/A (ISO 19005-2 6.2.4.4). Two spot-colour rules that
/// already exist for PDF/X-4 carry the same requirement in PDF/A-2/3, so they are widened to both profiles
/// (veraPDF reuses the identical <c>PDSeparation</c>/<c>PDDeviceN</c> tests across the two):
/// <list type="bullet">
///   <item><b>separation consistency</b> (t03) — all Separation arrays that name the same colorant must agree
///     on alternate space and tint transform;</item>
///   <item><b>DeviceN colorants</b> (t02) — every spot colorant used in a DeviceN/NChannel space must have an
///     entry in the <c>/Colorants</c> dictionary. PDF/A applies this to <b>every</b> DeviceN space, wider than
///     PDF/X-4's NChannel-only scope.</item>
/// </list>
/// The 6.2.4.4 t01 fixtures (a device alternate space used without a matching output intent) are already caught
/// by <c>device-colour</c>. Corpus-driven, LocalOnly.
/// </summary>
[Trait("Category", "LocalOnly")]
public class PreflightSlice33Tests(ITestOutputHelper output)
{
    private static readonly string[] SpotRuleIds = ["pdfx-separation-consistency", "pdfx-nchannel-colorants"];

    private static Finding[] SpotColour(string path, ConformanceProfile profile) =>
        Preflighter.Check(path, profile).Findings.Where(f => SpotRuleIds.Contains(f.RuleId)).ToArray();

    private static string? CorpusFixture(ConformanceProfile profile, string needle) =>
        !CorpusHarness.IsAvailable
            ? null
            : CorpusHarness.AllPdfPaths(profile).FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private void Dump(string label, Finding[] findings)
    {
        output.WriteLine($"{label}: {findings.Length} spot-colour finding(s)");
        foreach (Finding f in findings)
            output.WriteLine($"  [{ParitySnapshot.ClauseKey(f.Clause)}] ({f.RuleId}) {f.Message}");
    }

    [Theory]
    [InlineData("6-2-4-4-t02-fail-a")] // DeviceN spot colorant not in /Colorants
    [InlineData("6-2-4-4-t02-fail-b")] // /Colorants present but empty
    [InlineData("6-2-4-4-t02-fail-c")] // no /Colorants dictionary at all
    [InlineData("6-2-4-4-t03-fail-a")] // same-name Separation, different tintTransform
    [InlineData("6-2-4-4-t03-fail-b")] // same-name Separation, different alternateSpace
    [InlineData("6-2-4-4-t03-fail-c")] // same-name Separation (in NChannel /Colorants), different tintTransform
    [InlineData("6-2-4-4-t03-fail-d")] // same-name Separation (in NChannel /Colorants), different alternateSpace
    public void SpotColour_violation_is_detected(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = SpotColour(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("6.2.4.4", ParitySnapshot.ClauseKey(f.Clause)));
    }

    [Theory]
    [InlineData("6-2-4-4-t02-pass-a")] // conformant DeviceN with a complete /Colorants dictionary
    [InlineData("6-2-4-4-t03-pass-a")] // consistent same-name Separations
    public void Conformant_spot_colour_is_not_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = SpotColour(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.Empty(findings);
    }
}
