using System;
using System.Collections.Generic;
using Xunit;

namespace PdfLibrary.Tests.Fonts;

/// <summary>
/// The canary's diff, tested without a corpus. Pure in, pure out — no I/O, no skip semantics. This is
/// the part that decides whether a run is green, so it gets tested directly rather than only through a
/// LocalOnly gate that CI never runs.
/// </summary>
public class FontFaultCompareTests
{
    private static Dictionary<string, string> Map(params (string Key, string Value)[] rows)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in rows) d[key] = value;
        return d;
    }

    [Fact]
    public void IdenticalMaps_ProduceNoProblems()
    {
        var m = Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException"));

        Assert.Empty(FontFaultCanary.Compare(m, m));
    }

    [Fact]
    public void BothEmpty_ProduceNoProblems()
    {
        // The expected steady state once the corpus is clean: no faults, no baseline rows, green.
        Assert.Empty(FontFaultCanary.Compare(Map(), Map()));
    }

    [Fact]
    public void AFaultNotInTheBaseline_IsReportedAsNew()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")),
            expected: Map());

        string problem = Assert.Single(problems);
        Assert.StartsWith("  NEW", problem);
        Assert.Contains("a.pdf\tFontA", problem);
        Assert.Contains("CffTable:IndexOutOfRangeException", problem);
    }

    [Fact]
    public void ADifferentFaultForTheSameFont_IsReportedAsChanged()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("a.pdf\tFontA", "CffTable:EndOfStreamException")),
            expected: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")));

        string problem = Assert.Single(problems);
        Assert.StartsWith("  CHANGED", problem);
        // Both sides carry their stage: a baseline row is only meaningful as Stage:ExceptionType.
        Assert.Contains("CffTable:IndexOutOfRangeException -> CffTable:EndOfStreamException", problem);
    }

    [Fact]
    public void ABaselinedFaultThatStoppedHappening_IsReportedAsMissing()
    {
        // A fixed parser is still a baseline change and must be reviewed, not silently absorbed.
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(),
            expected: Map(("a.pdf\tFontA", "CffTable:IndexOutOfRangeException")));

        string problem = Assert.Single(problems);
        Assert.StartsWith("  MISSING", problem);
        Assert.Contains("a.pdf\tFontA", problem);
    }

    [Fact]
    public void AllThreeKinds_AreReportedTogether()
    {
        List<string> problems = FontFaultCanary.Compare(
            actual: Map(("new.pdf\tF", "Head:EndOfStreamException"), ("same.pdf\tF", "Cmap:X")),
            expected: Map(("gone.pdf\tF", "Head:X"), ("same.pdf\tF", "Cmap:X")));

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.StartsWith("  NEW") && p.Contains("new.pdf"));
        Assert.Contains(problems, p => p.StartsWith("  MISSING") && p.Contains("gone.pdf"));
    }
}
