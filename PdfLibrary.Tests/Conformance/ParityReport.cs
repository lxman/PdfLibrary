using System.Collections.Generic;
using System.Linq;
using System.Text;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Renders the veraPDF-parity report — the non-gating, human-readable face of the harness. It turns
/// <see cref="ParityComparison"/> into markdown: a per-profile verdict-agreement scoreboard (the
/// confusion matrix) and a per-clause coverage matrix that shows exactly where PdfLibrary and veraPDF
/// diverge. This is the artifact the "verdict parity with veraPDF" claim is quoted from.
/// </summary>
internal static class ParityReport
{
    private static string ProfileLabel(ConformanceProfile p) => p switch
    {
        ConformanceProfile.PdfA2b => "PDF/A-2b",
        ConformanceProfile.PdfA2u => "PDF/A-2u",
        ConformanceProfile.PdfA3b => "PDF/A-3b",
        ConformanceProfile.PdfUA1 => "PDF/UA-1",
        _ => p.ToString(),
    };

    private static int Percent(int num, int den) => den == 0 ? 100 : (int)System.Math.Round(100.0 * num / den);

    public static string Render()
    {
        IReadOnlyList<ParityComparison.ProfileComparison> all = ParityComparison.All;
        var sb = new StringBuilder();

        int totalFiles = all.Sum(p => p.Files.Count);
        int totalFp = all.Sum(p => p.Files.Count(f => f.IsFalsePositive));

        sb.AppendLine("# veraPDF parity report");
        sb.AppendLine();
        string versions = string.Join(", ", ParitySnapshot.VerapdfVersions.Select(kv => $"{kv.Key} {kv.Value}"));
        sb.AppendLine($"_PdfLibrary preflighter vs veraPDF ({versions}); corpus @ {ParitySnapshot.CorpusCommit}. "
            + "Generated — regenerate with the `Category=Parity` test `ParityReportTests.Generate_parity_report` "
            + "(set `PARITY_REPORT`), and re-run `tools/verapdf-parity/capture.sh` first if veraPDF or the corpus moved._");
        sb.AppendLine();
        sb.AppendLine($"Across all **{totalFiles}** files PdfLibrary produced **{totalFp} false positives** — it never "
            + "rejects a file veraPDF accepts. PdfLibrary is a strict subset of veraPDF, so every disagreement below is "
            + "a coverage gap (veraPDF flags a clause PdfLibrary does not yet implement), **not a PdfLibrary error**.");
        sb.AppendLine();

        // ---- Report B: verdict-agreement scoreboard (confusion matrix) ------------------------------
        sb.AppendLine("## Verdict agreement");
        sb.AppendLine();
        sb.AppendLine("| Profile | Files | Both pass | Both fail | PdfLibrary misses (gap) | PdfLibrary FP | Agreement |");
        sb.AppendLine("|---|--:|--:|--:|--:|--:|--:|");
        foreach (ParityComparison.ProfileComparison pc in all)
        {
            int bothPass = pc.Files.Count(f => f.VeraCompliant && f.PdfLibraryConforms);
            int bothFail = pc.Files.Count(f => !f.VeraCompliant && !f.PdfLibraryConforms);
            int gap = pc.Files.Count(f => !f.VeraCompliant && f.PdfLibraryConforms);
            int fp = pc.Files.Count(f => f.IsFalsePositive);
            int agree = bothPass + bothFail;
            sb.AppendLine($"| {ProfileLabel(pc.Profile)} | {pc.Files.Count} | {bothPass} | {bothFail} | "
                + $"{gap} | {fp} | {agree}/{pc.Files.Count} ({Percent(agree, pc.Files.Count)}%) |");
        }
        sb.AppendLine();

        // ---- Report A: per-clause coverage matrix ---------------------------------------------------
        sb.AppendLine("## Clause coverage");
        sb.AppendLine();
        sb.AppendLine("Of the files where veraPDF flags a clause, how many does PdfLibrary also flag on that clause.");
        sb.AppendLine();

        var gaps = new List<(ConformanceProfile Profile, string Clause, int Vera, int Matched)>();

        foreach (ParityComparison.ProfileComparison pc in all)
        {
            (Dictionary<string, int> vera, Dictionary<string, int> matched) = ClauseTallies(pc);
            if (vera.Count == 0) continue;

            int fullCount = vera.Count(kv => matched.GetValueOrDefault(kv.Key) == kv.Value);
            sb.AppendLine($"### {ProfileLabel(pc.Profile)} — {fullCount}/{vera.Count} clauses at full parity");
            sb.AppendLine();
            sb.AppendLine("| Clause | veraPDF flags | PdfLibrary matches | Coverage | |");
            sb.AppendLine("|---|--:|--:|--:|---|");
            foreach ((string clause, int total) in vera.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
            {
                int m = matched.GetValueOrDefault(clause);
                string status = m == total ? "✅ full" : m == 0 ? "— none" : "◐ partial";
                sb.AppendLine($"| {clause} | {total} | {m} | {Percent(m, total)}% | {status} |");
                if (m < total) gaps.Add((pc.Profile, clause, total, m));
            }
            sb.AppendLine();
        }

        // ---- verdict leverage (misses each clause would flip) ---------------------------------------
        sb.AppendLine("## Verdict leverage — what actually moves a whole-file verdict");
        sb.AppendLine();
        sb.AppendLine(
            "**Plan verdict work from this section and coverage work from the table below.** A miss is a "
            + "VERDICT disagreement: veraPDF rejects the file and PdfLibrary conforms, having flagged "
            + "nothing. Closing ANY ONE of a miss's clauses flips it, so a clause's leverage is simply the "
            + "number of misses it appears in. Matching veraPDF on every clause it flags is a separate, "
            + "stricter goal — that is what the clause-coverage table measures.");
        sb.AppendLine();
        foreach (ParityComparison.ProfileComparison pc in all)
            sb.Append(RenderLeverage(ProfileLabel(pc.Profile), ParityLeverage.Analyse(pc.Files)));

        // ---- clause-coverage gaps -------------------------------------------------------------------
        sb.AppendLine("## Biggest clause-coverage gaps");
        sb.AppendLine();
        sb.AppendLine("Ranked by number of files PdfLibrary misses on a clause it does not fully cover. This measures "
            + "detection coverage, **not** verdict movement — a clause high on this list may flip nothing at all "
            + "(see the leverage section above before treating any of it as prioritised work).");
        sb.AppendLine();
        int rank = 1;
        foreach ((ConformanceProfile profile, string clause, int vera, int matched) in
                 gaps.OrderByDescending(g => g.Vera - g.Matched).Take(10))
        {
            sb.AppendLine($"{rank++}. **{ProfileLabel(profile)} clause {clause}** — {vera - matched} of {vera} "
                + $"files missed (PdfLibrary matches {matched}).");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    /// <summary>
    /// Renders one profile's verdict-leverage ranking: per clause, how many whole-file misses it blocks
    /// and how many it flips. Since a miss means PdfLibrary flagged nothing on that file, closing any one
    /// clause flips it — so the two numbers are always equal, and ranking is simply by misses blocked.
    /// </summary>
    public static string RenderLeverage(string profileLabel, ParityLeverage.Analysis analysis)
    {
        var sb = new StringBuilder();
        string miss = analysis.Misses.Count == 1 ? "miss" : "misses";
        sb.AppendLine($"### {profileLabel} — {analysis.Misses.Count} whole-file {miss}");
        sb.AppendLine();

        if (analysis.Misses.Count == 0)
        {
            sb.AppendLine("PdfLibrary's verdict already agrees with veraPDF on every file of this profile, so "
                + "**no clause work here can move a verdict** — however many files a clause gap still spans.");
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine("| Clause | Misses it blocks | Flips alone |");
        sb.AppendLine("|---|--:|--:|");
        foreach (ParityLeverage.ClauseLeverage c in analysis.Clauses)
            sb.AppendLine($"| {c.Clause} | {c.AppearsInMisses} | {c.FlipsAlone} |");
        sb.AppendLine();

        sb.AppendLine("Each miss and the clauses standing between it and agreement:");
        sb.AppendLine();
        sb.AppendLine("| File | Blocked by |");
        sb.AppendLine("|---|---|");
        foreach (ParityLeverage.Miss m in analysis.Misses)
            sb.AppendLine($"| {m.FileName} | {string.Join(" + ", m.MissedClauses)} |");
        sb.AppendLine();

        return sb.ToString();
    }

    private static (Dictionary<string, int> Vera, Dictionary<string, int> Matched) ClauseTallies(
        ParityComparison.ProfileComparison pc)
    {
        var vera = new Dictionary<string, int>();
        var matched = new Dictionary<string, int>();
        foreach (ParityComparison.FileComparison f in pc.Files)
            foreach (string c in f.VeraClauses)
            {
                vera[c] = vera.GetValueOrDefault(c) + 1;
                if (f.PdfLibraryClauses.Contains(c))
                    matched[c] = matched.GetValueOrDefault(c) + 1;
            }
        return (vera, matched);
    }
}
