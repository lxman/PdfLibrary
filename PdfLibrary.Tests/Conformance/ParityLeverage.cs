using System;
using System.Collections.Generic;
using System.Linq;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Sole-cause analysis over the whole-file misses: which clauses actually move a VERDICT, as opposed
/// to which clauses we most often fail to flag.
///
/// The two are very different, and conflating them misdirects planning. A clause veraPDF flags on
/// many files still moves nothing unless it is the ONLY clause we miss on some file — where a miss
/// co-occurs with other missed clauses, every one of them must close before that file's verdict
/// flips. Measured 2026-08-19, PDF/A-2b's three most frequently missed clauses (6.2.11.5, 6.2.11.4.1,
/// 6.2.11.8 — the font cluster) each moved ZERO verdicts alone and only paid all three together.
/// </summary>
internal static class ParityLeverage
{
    /// <summary>One whole-file miss and the clauses standing between it and agreement.</summary>
    internal sealed record Miss(string FileName, IReadOnlyList<string> MissedClauses);

    /// <summary>What closing one clause is worth, alone and in its cheapest paying combination.</summary>
    /// <param name="AppearsInMisses">Whole-file misses whose blocking set contains this clause.</param>
    /// <param name="FlipsAlone">Misses this clause closes by itself — its true verdict leverage.</param>
    /// <param name="MinimumPayingSet">Smallest blocking set containing it (itself, when it pays alone).</param>
    /// <param name="MinimumPayingSetFlips">Misses that set closes once every clause in it is covered.</param>
    internal sealed record ClauseLeverage(
        string Clause,
        int AppearsInMisses,
        int FlipsAlone,
        IReadOnlyList<string> MinimumPayingSet,
        int MinimumPayingSetFlips);

    /// <summary>Misses by file name, and clauses ranked by what they actually flip.</summary>
    internal sealed record Analysis(
        IReadOnlyList<Miss> Misses,
        IReadOnlyList<ClauseLeverage> Clauses);

    public static Analysis Analyse(IEnumerable<ParityComparison.FileComparison> files)
    {
        // Only whole-file misses count. A file both validators reject already agrees, so a clause we
        // miss on it carries no verdict leverage however often it appears.
        List<Miss> misses = files
            .Where(f => !f.VeraCompliant && f.PdfLibraryConforms)
            .Select(f => new Miss(
                f.FileName,
                f.VeraClauses.Except(f.PdfLibraryClauses).OrderBy(c => c, StringComparer.Ordinal).ToList()))
            .OrderBy(m => m.FileName, StringComparer.Ordinal)
            .ToList();

        List<IReadOnlyList<string>> blockingSets = misses.Select(m => m.MissedClauses).ToList();

        var clauses = new List<ClauseLeverage>();
        foreach (string clause in misses.SelectMany(m => m.MissedClauses).Distinct(StringComparer.Ordinal))
        {
            int appears = misses.Count(m => m.MissedClauses.Contains(clause, StringComparer.Ordinal));
            int flipsAlone = misses.Count(m =>
                m.MissedClauses.Count == 1 && StringComparer.Ordinal.Equals(m.MissedClauses[0], clause));

            // The cheapest combination that buys anything: the smallest blocking set the clause sits in.
            IReadOnlyList<string> minimum = blockingSets
                .Where(s => s.Contains(clause, StringComparer.Ordinal))
                .OrderBy(s => s.Count)
                .ThenBy(s => string.Join(",", s), StringComparer.Ordinal)
                .First();
            var covered = minimum.ToHashSet(StringComparer.Ordinal);
            int minimumFlips = misses.Count(m => m.MissedClauses.All(covered.Contains));

            clauses.Add(new ClauseLeverage(clause, appears, flipsAlone, minimum, minimumFlips));
        }

        return new Analysis(
            misses,
            clauses
                .OrderByDescending(c => c.FlipsAlone)
                .ThenByDescending(c => c.AppearsInMisses)
                .ThenBy(c => c.Clause, StringComparer.Ordinal)
                .ToList());
    }
}
