using System.Collections.Generic;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Unit tests for the verdict-leverage analysis behind the report's leverage ranking. They use
/// synthetic <see cref="ParityComparison.FileComparison"/> values, so unlike the rest of the parity
/// harness they need no corpus and run everywhere.
///
/// The behaviour under test: a miss means PdfLibrary flagged NOTHING on that file, so flagging ANY ONE
/// of its clauses flips the verdict — co-occurrence with other missed clauses does not blunt a clause's
/// leverage. This is a stricter goal than clause coverage (matching veraPDF on every clause it flags),
/// which the report's separate coverage table measures.
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
    public void Any_single_missed_clause_flips_a_miss()
    {
        // A miss means PdfLibrary flagged NOTHING, so flagging any ONE of the file's clauses
        // makes it non-conforming and the verdict agrees. Co-occurrence does not require
        // closing every clause.
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("two-clause-miss.pdf", "6.2.11.5", "6.2.11.8"),
        ]);

        ParityLeverage.ClauseLeverage five = For(analysis, "6.2.11.5");
        Assert.Equal(1, five.AppearsInMisses);
        Assert.Equal(1, five.FlipsAlone);
        Assert.Equal(["6.2.11.5"], five.MinimumPayingSet);
        Assert.Equal(1, five.MinimumPayingSetFlips);
    }

    [Fact]
    public void A_clause_that_always_co_occurs_still_flips_every_verdict_alone()
    {
        // Corrected 2026-08-20: co-occurrence used to zero out a clause's leverage. It doesn't --
        // flagging either clause alone already flips both misses, since a miss means PdfLibrary
        // flagged nothing at all.
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5"),
        ]);

        Assert.Equal(2, For(analysis, "6.2.11.5").FlipsAlone);
        Assert.Equal(2, For(analysis, "6.2.11.4.1").FlipsAlone);
        Assert.Equal(2, For(analysis, "6.2.11.5").AppearsInMisses);
    }

    [Fact]
    public void A_missed_clause_flips_every_file_it_appears_in()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.1.13"),
            Miss("b.pdf", "6.1.13"),
            Miss("c.pdf", "6.1.13", "6.2.11.8"),
        ]);

        Assert.Equal(3, For(analysis, "6.1.13").AppearsInMisses);
        Assert.Equal(3, For(analysis, "6.1.13").FlipsAlone);
        Assert.Equal(1, For(analysis, "6.2.11.8").FlipsAlone);
    }

    [Fact]
    public void A_co_occurring_clause_reports_itself_as_the_paying_set()
    {
        // Corrected 2026-08-20: the "cheapest paying set" used to grow with the other clauses a miss
        // was blocked by. It doesn't -- it is always the clause itself, however many other clauses
        // co-occur, because flagging just this one already flips the file.
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5", "6.2.11.8"),
        ]);

        ParityLeverage.ClauseLeverage five = For(analysis, "6.2.11.5");
        Assert.Equal(["6.2.11.5"], five.MinimumPayingSet);
        Assert.Equal(2, five.MinimumPayingSetFlips);

        ParityLeverage.ClauseLeverage eight = For(analysis, "6.2.11.8");
        Assert.Equal(["6.2.11.8"], eight.MinimumPayingSet);
        Assert.Equal(1, eight.MinimumPayingSetFlips);
    }

    [Fact]
    public void The_ranking_puts_the_more_frequent_clause_first_now_that_every_clause_flips_alone()
    {
        // Corrected 2026-08-20: since every clause flips every miss it appears in, there is no more
        // "flips none" class to elevate above a frequent one. Ranking collapses to misses blocked --
        // 6.2.11.5 (three files) correctly outranks 6.6.4 (one file).
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("c.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("d.pdf", "6.6.4"),
        ]);

        string markdown = ParityReport.RenderLeverage("PDF/A-2b", analysis);

        Assert.True(
            markdown.IndexOf("6.2.11.5", StringComparison.Ordinal)
            < markdown.IndexOf("6.6.4", StringComparison.Ordinal),
            "a clause blocking more misses must rank above one blocking fewer, now that every clause "
            + "flips alone:\n" + markdown);
    }

    [Fact]
    public void The_per_file_breakdown_names_every_blocking_clause()
    {
        ParityLeverage.Analysis analysis = ParityLeverage.Analyse(
        [
            Miss("a.pdf", "6.2.11.4.1", "6.2.11.5"),
            Miss("b.pdf", "6.6.4"),
        ]);

        string markdown = ParityReport.RenderLeverage("PDF/A-2b", analysis);

        // The per-file breakdown table (not the per-clause leverage table) still lists every clause
        // blocking a given miss, so a reader can see the full blocking set for that file.
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
