using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 32 — rendering intents (<c>rendering-intent</c>, ISO 19005-2 6.2.6). Every rendering intent named in a
/// PDF/A-2/3 file must be one of the four ISO 32000-1 Table 70 values. Mirrors veraPDF's <c>CosRenderingIntent</c>
/// rule, which reaches the ExtGState <c>/RI</c> entry, an image (XObject or inline) <c>/Intent</c> entry, and the
/// <c>ri</c> content operator. This corrects the previous slice, which mis-attributed the ExtGState <c>/RI</c>
/// value check to 6.2.5.
/// <para>
/// Corpus-driven (LocalOnly). Detection spans two sites the corpus exercises: <c>6-2-5-t05-fail-a</c> (an
/// ExtGState <c>/RI /Custom</c>) and <c>6-2-6-t01-fail-a</c> (an inline image <c>/Intent /Custom</c>).
/// No-false-positive: the matching pass fixtures, which use only standard intent values.
/// </para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class PreflightSlice32Tests(ITestOutputHelper output)
{
    private static Finding[] RenderingIntent(string path, ConformanceProfile profile) =>
        Preflighter.Check(path, profile).Findings.Where(f => f.RuleId == "rendering-intent").ToArray();

    private static string? CorpusFixture(ConformanceProfile profile, string needle) =>
        !CorpusHarness.IsAvailable
            ? null
            : CorpusHarness.AllPdfPaths(profile).FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private void Dump(string label, Finding[] findings)
    {
        output.WriteLine($"{label}: {findings.Length} rendering-intent finding(s)");
        foreach (Finding f in findings)
            output.WriteLine($"  [{ParitySnapshot.ClauseKey(f.Clause)}] {f.Message}");
    }

    [Theory]
    [InlineData("6-2-5-t05-fail-a")] // ExtGState /RI /Custom
    [InlineData("6-2-6-t01-fail-a")] // inline image /Intent /Custom
    public void NonStandard_rendering_intent_is_detected(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = RenderingIntent(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("6.2.6", ParitySnapshot.ClauseKey(f.Clause)));
    }

    [Theory]
    [InlineData("6-2-5-t05-pass-a")] // ExtGState /RI with all four standard values
    [InlineData("6-2-6-t01-pass-a")] // conformant inline-image intent
    [InlineData("6-2-6-t01-pass-b")] // conformant inline-image intent
    public void Standard_rendering_intent_is_not_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = RenderingIntent(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.Empty(findings);
    }
}
