using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Phase-3 reporting: renders the parity report (<see cref="ParityReport"/>) and guards a coarse,
/// ratcheting whole-file agreement floor. Both need the corpus (they run the preflighter via
/// <see cref="ParityComparison"/>), so they are <c>[Trait("Category","Parity")]</c>. The report is a
/// non-gating artifact; the agreement floor is the report's soft companion gate — it catches a broad
/// regression that the clause-exact gate in <see cref="ParityOracleTests"/> might not.
/// </summary>
[Trait("Category", "Parity")]
public class ParityReportTests(ITestOutputHelper output)
{
    private const string Skip = "veraPDF corpus not present at ../veraPDF-corpus (Category=Parity)";

    /// <summary>Whole-file verdict-agreement floor per profile — a ratchet; raise as coverage grows.</summary>
    private static readonly IReadOnlyDictionary<ConformanceProfile, int> AgreementFloor =
        new Dictionary<ConformanceProfile, int>
        {
            [ConformanceProfile.PdfA2b] = 875,   // + slice 18 font-dict rules (6.2.11.6 full, .3.2 full, .3.1/.3.3 partial)
            [ConformanceProfile.PdfA2u] = 19,    // + 6.2.11.3.1 (embedded-CMap supplement) catches 6-2-11-7-2-t01-fail-f
            [ConformanceProfile.PdfA3b] = 12,
            [ConformanceProfile.PdfUA1] = 234,   // + slice 18 font-dict rules (7.21.3.2 full; 7.21.6/.3.1/.3.3 partial)
        };

    [Fact]
    public void Generate_parity_report()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        string markdown = ParityReport.Render();
        output.WriteLine(markdown);

        // Written to disk only when a destination is supplied (CI artifact path, or a manual refresh
        // of the committed report) — so a normal test run never dirties a tracked file.
        string? dest = Environment.GetEnvironmentVariable("PARITY_REPORT");
        if (!string.IsNullOrWhiteSpace(dest))
        {
            File.WriteAllText(dest, markdown);
            output.WriteLine($"\n(wrote report to {dest})");
        }

        Assert.Contains("# veraPDF parity report", markdown);
    }

    [Fact]
    public void Whole_file_agreement_does_not_regress()
    {
        Assert.SkipUnless(CorpusHarness.IsAvailable && ParitySnapshot.IsAvailable, Skip);

        var regressions = new List<string>();
        foreach (ParityComparison.ProfileComparison pc in ParityComparison.All)
        {
            int agree = pc.Files.Count(f => f.VeraCompliant == f.FocalConforms);
            int floor = AgreementFloor.GetValueOrDefault(pc.Profile);
            output.WriteLine($"{pc.Profile}: agreement {agree}/{pc.Files.Count} (floor {floor})");
            if (agree < floor)
                regressions.Add($"{pc.Profile}: {agree} < floor {floor}");
        }

        Assert.True(regressions.Count == 0,
            "whole-file agreement regressed vs the reference: " + string.Join(", ", regressions)
            + ". If a rule was intentionally changed, lower the floor; otherwise this is a regression.");
    }
}
