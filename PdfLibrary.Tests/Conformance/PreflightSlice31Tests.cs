using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Slice 31 — extended graphics state (<c>graphics-state</c>, ISO 19005-2 6.2.5). An ExtGState dictionary in a
/// PDF/A-2/3 file is constrained: no <c>/TR</c> (transfer function) and no <c>/HTP</c> (a legacy halftone-phase
/// key); <c>/TR2</c> only with the value <c>/Default</c>; <c>/RI</c> only one of the four standard rendering
/// intents; and any halftone reached through <c>/HT</c> (including the per-colourant components of a Type 5
/// composite) must have <c>HalftoneType</c> 1 or 5, must not carry a <c>HalftoneName</c>, and must supply a
/// <c>TransferFunction</c> for every non-primary colourant.
/// <para>
/// Driven by the veraPDF corpus (LocalOnly). Detection: the seven 6.2.5 fail fixtures. No-false-positive: the
/// three 6.2.5 pass fixtures, which exercise the exact valid boundaries the checks must accept
/// (<c>/TR2 /Default</c>, <c>HalftoneType 1</c>, and all four standard <c>/RI</c> values).
/// </para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class PreflightSlice31Tests(ITestOutputHelper output)
{
    private static Finding[] GraphicsState(string path, ConformanceProfile profile) =>
        Preflighter.Check(path, profile).Findings.Where(f => f.RuleId == "graphics-state").ToArray();

    private static string? CorpusFixture(ConformanceProfile profile, string needle) =>
        !CorpusHarness.IsAvailable
            ? null
            : CorpusHarness.AllPdfPaths(profile).FirstOrDefault(p => Path.GetFileName(p).Contains(needle));

    private void Dump(string label, Finding[] findings)
    {
        output.WriteLine($"{label}: {findings.Length} graphics-state finding(s)");
        foreach (Finding f in findings)
            output.WriteLine($"  [{ParitySnapshot.ClauseKey(f.Clause)}] {f.Message}");
    }

    // t03-fail-b (Type 5 halftone: TransferFunction absent for a "non-primary" colourant) is intentionally
    // NOT covered yet: its fixture treats Red/Green/Blue as non-primary, contradicting ISO 32000-1's list of
    // DeviceRGB primaries, so pinning it needs the veraPDF profile's exact primary-colourant set. Deferred to
    // avoid encoding a guess that could false-positive on a conformant Type 5 halftone.
    [Theory]
    [InlineData("6-2-5-t01-fail-a")] // /TR transfer function
    [InlineData("6-2-5-t01-fail-b")] // /HTP legacy halftone-phase key
    [InlineData("6-2-5-t02-fail-a")] // /TR2 with a value other than /Default
    [InlineData("6-2-5-t03-fail-a")] // HalftoneType other than 1 or 5
    [InlineData("6-2-5-t04-fail-a")] // halftone carries a HalftoneName
    [InlineData("6-2-5-t05-fail-a")] // /RI with a non-standard value ("Custom")
    public void ExtGState_violation_is_detected(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = GraphicsState(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal("6.2.5", ParitySnapshot.ClauseKey(f.Clause)));
    }

    [Theory]
    [InlineData("6-2-5-t02-pass-a")] // /TR2 /Default
    [InlineData("6-2-5-t03-pass-a")] // HalftoneType 1
    [InlineData("6-2-5-t05-pass-a")] // all four standard /RI values
    public void Conformant_extgstate_is_not_flagged(string needle)
    {
        string? path = CorpusFixture(ConformanceProfile.PdfA2b, needle);
        Assert.SkipUnless(path is not null, "veraPDF corpus not present at ../veraPDF-corpus");

        Finding[] findings = GraphicsState(path!, ConformanceProfile.PdfA2b);
        Dump(needle, findings);

        Assert.Empty(findings);
    }
}
