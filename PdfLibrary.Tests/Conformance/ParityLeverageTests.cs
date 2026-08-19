using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Unit tests for the sole-cause analysis behind the report's verdict-leverage ranking. They use
/// synthetic <see cref="ParityComparison.FileComparison"/> values, so unlike the rest of the parity
/// harness they need no corpus and run everywhere.
///
/// The behaviour under test is the distinction that the older "biggest parity gaps" ranking blurred:
/// a clause that veraPDF flags on many files still moves NO whole-file verdict unless it is the only
/// clause we miss on some file.
/// </summary>
public class ParityLeverageTests
{
    private static ParityComparison.FileComparison Miss(string name, params string[] veraClauses) =>
        new(ConformanceProfile.PdfA2b, name,
            VeraCompliant: false, veraClauses.ToHashSet(StringComparer.Ordinal),
            PdfLibraryConforms: true, new HashSet<string>(StringComparer.Ordinal));

    private static ParityLeverage.ClauseLeverage For(ParityLeverage.Analysis a, string clause) =>
        a.Clauses.Single(c => c.Clause == clause);

    [Fact]
    public void A_clause_that_always_co_occurs_flips_no_verdict_alone()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5"),
        ]);

        Assert.Equal(0, For(analysis, "6.2.11.5").FlipsAlone);
        Assert.Equal(0, For(analysis, "6.2.11.4.1").FlipsAlone);
        Assert.Equal(2, For(analysis, "6.2.11.5").AppearsInMisses);
    }

    [Fact]
    public void A_clause_that_is_the_only_miss_on_a_file_flips_that_file()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.1.13"),
            Miss("b.pdf", "6.1.13"),
            Miss("c.pdf", "6.1.13", "6.2.11.8"),
        ]);

        Assert.Equal(3, For(analysis, "6.1.13").AppearsInMisses);
        Assert.Equal(2, For(analysis, "6.1.13").FlipsAlone);
        Assert.Equal(0, For(analysis, "6.2.11.8").FlipsAlone);
    }

    [Fact]
    public void A_co_occurring_clause_reports_the_smallest_set_that_pays_and_what_it_pays()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5", "6.2.11.8"),
        ]);

        ParityLeverage.ClauseLeverage five = For(analysis, "6.2.11.5");
        Assert.Equal(["6.2.11.4.1", "6.2.11.5"], five.MinimumPayingSet);
        Assert.Equal(1, five.MinimumPayingSetFlips);

        // The three-clause set subsumes the two-clause one, so closing it pays for both files.
        ParityLeverage.ClauseLeverage eight = For(analysis, "6.2.11.8");
        Assert.Equal(["6.2.11.4.1", "6.2.11.5", "6.2.11.8"], eight.MinimumPayingSet);
        Assert.Equal(2, eight.MinimumPayingSetFlips);
    }

    [Fact]
    public void The_ranking_puts_a_clause_that_flips_a_verdict_above_a_more_frequent_one_that_flips_none()
    {
        // 6.2.11.5 blocks three files but never alone; 6.6.4 blocks one and closes it. The old
        // frequency-ranked "highest-leverage work" list inverted exactly this pair.
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("c.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("d.pdf", "6.6.4"),
        ]);

        string markdown = ParityReport.RenderLeverage("PDF/A-2b", analysis);

        Assert.True(
            markdown.IndexOf("6.6.4", StringComparison.Ordinal)
            < markdown.IndexOf("6.2.11.5", StringComparison.Ordinal),
            "a clause that flips a verdict must rank above a more frequent one that flips none:\n" + markdown);
    }

    [Fact]
    public void The_ranking_names_the_combination_a_zero_leverage_clause_needs_before_it_pays()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.6.4"),
        ]);

        string markdown = ParityReport.RenderLeverage("PDF/A-2b", analysis);

        // Without the partner clause named, a reader cannot tell what closing 6.2.11.5 would buy.
        Assert.Contains("6.2.11.4.1 + 6.2.11.5", markdown);
    }

    [Fact]
    public void Files_the_two_validators_agree_on_are_not_misses()
    {
        var bothFail = new ParityComparison.FileComparison(
            ConformanceProfile.PdfA2b, "bothfail.pdf",
            VeraCompliant: false, new HashSet<string>(StringComparer.Ordinal) { "6.1.13", "6.2.2" },
            PdfLibraryConforms: false, new HashSet<string>(StringComparer.Ordinal) { "6.1.13" });
        var bothPass = new ParityComparison.FileComparison(
            ConformanceProfile.PdfA2b, "bothpass.pdf",
            VeraCompliant: true, new HashSet<string>(StringComparer.Ordinal),
            PdfLibraryConforms: true, new HashSet<string>(StringComparer.Ordinal));

        ParityLeverage.Analysis analysis = ParityLeverage.Analyse([bothFail, bothPass, Miss("gap.pdf", "6.2.2")]);

        Assert.Equal(["gap.pdf"], analysis.Misses.Select(m => m.FileName));
        // 6.2.2 is missed clause-wise on bothfail.pdf too, but that file's verdict already agrees,
        // so it contributes nothing to verdict leverage.
        Assert.Equal(1, For(analysis, "6.2.2").AppearsInMisses);
        Assert.Equal(1, For(analysis, "6.2.2").FlipsAlone);
    }
}
