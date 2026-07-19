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
            [ConformanceProfile.PdfA2b] = 937,   // +5 prohibited-xobject (6.2.9 → 5/5) +3 image-dictionary (6.2.8 → 3/4; the 4th is an inline image needing content-stream parsing) +4 permissions (6.1.12 → 4/4: /Perms keys + signature-reference Digest keys under DocMDP) +2 name-utf8 (6.1.8 → 2/2: every name valid UTF-8 after #-escape), all 0 FP. Ratchets to the current verified agreement (the earlier 899 lagged the 921 baseline). Standing −1 note: the 6.2.11.5 width check stays dropped on CIDFontType0/CFF fonts (CFF advance extraction false-positives on conformant reference files, PDFUA-Ref-2-08 — FP-safety outweighs one corpus detection). +2 simple-font glyph-present (6.2.11.4.1 t2 → 8/11) via the tri-state code→GID resolver (font-program slice 1); simple-font .notdef (6.2.11.8) is also live but adds 0 corpus files — its 5 remaining fail files are out-of-scope font types (classic Type1 / predefined-charset CFF / symbolic / Type0-non-identity → Unknown, FP-safe); 0 FP corpus-wide
            [ConformanceProfile.PdfA2u] = 19,    // + 6.2.11.3.1 (embedded-CMap supplement) catches 6-2-11-7-2-t01-fail-f
            [ConformanceProfile.PdfA3b] = 12,
            [ConformanceProfile.PdfUA1] = 281,   // +6 from embedded-file widened to UA-1 7.11 (non-empty /F,/UF; filespecs from the catalog name tree AND FileAttachment annotation /FS → 6/6). Ratchets to the current verified agreement (the earlier 253 lagged the 275 baseline: slice-21 annotation rules + the incremental-update obj-stream resolution fix)
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
            int agree = pc.Files.Count(f => f.VeraCompliant == f.PdfLibraryConforms);
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
