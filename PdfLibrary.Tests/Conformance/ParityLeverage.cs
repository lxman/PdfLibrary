using System;
using System.Collections.Generic;
using System.Linq;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Verdict leverage over the whole-file misses: how many verdicts each clause would move.
///
/// A miss is a VERDICT disagreement — veraPDF rejects the file and PdfLibrary conforms, which means
/// PdfLibrary emitted no error finding at all. Flagging ANY ONE of that file's clauses therefore
/// flips it. Clause coverage is a different and stricter measure: matching veraPDF on every clause
/// it flags. Plan verdict work from this analysis and coverage work from the coverage table; do not
/// read one as the other.
///
/// Corrected 2026-08-20. The previous model counted a clause as flipping a miss only when it was the
/// SOLE missed clause, which reported zero leverage for the whole PDF/A-2b font cluster and read as
/// "partial closure moves nothing". The corpus disproves it directly: 6-2-11-8-t01-fail-d is flagged
/// by veraPDF on both 6.2.11.5 and 6.2.11.8, PdfLibrary flags only 6.2.11.8, and the file is not a miss.
/// </summary>
internal static class ParityLeverage
{
    /// <summary>One whole-file miss and the clauses standing between it and agreement.</summary>
    internal sealed record Miss(string FileName, IReadOnlyList<string> MissedClauses);

    /// <summary>What closing one clause is worth, alone and in its cheapest paying combination.</summary>
    /// <param name="AppearsInMisses">Whole-file misses whose blocking set contains this clause.</param>
    /// <param name="FlipsAlone">Misses this clause closes by itself — equal to AppearsInMisses, since any one clause flips a miss.</param>
    /// <param name="MinimumPayingSet">Always the clause itself; retained for report-format stability.</param>
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

        var clauses = new List<ClauseLeverage>();
        foreach (string clause in misses.SelectMany(m => m.MissedClauses).Distinct(StringComparer.Ordinal))
        {
            int appears = misses.Count(m => m.MissedClauses.Contains(clause, StringComparer.Ordinal));

            // A miss is !VeraCompliant && PdfLibraryConforms — PdfLibrary flagged NOTHING on that
            // file. Flagging ANY ONE of its clauses makes the file non-conforming, so the verdict
            // agrees. Every clause in a blocking set therefore flips every miss it appears in, and
            // the cheapest paying set is always the clause itself. (This is verdict leverage; clause
            // -level parity is a different, stricter goal measured by the coverage table.)
            int flipsAlone = appears;
            IReadOnlyList<string> minimum = [clause];
            int minimumFlips = appears;

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
