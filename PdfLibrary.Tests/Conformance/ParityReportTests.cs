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
            [ConformanceProfile.PdfA2b] = 945,   // +3 content-stream operators (clause 6.2.2 t01: a page/Do-reached content stream must use only the 73 ISO 32000-1 operators, even inside BX/EX; ContentStreamOperatorRule, usage-sensitive so a dead form does not false-positive), 0 FP. t04 (run-together operators the lexer recovers, e.g. `ref`→`re`+`f`) deferred to a content-tokenisation change. +4 CMap WMode/UseCMap (6.2.11.3.3 t2/t3 → 5/5 full; font-program slice 3), 0 FP. +1 Type3 font metrics (6.2.11.5 via CharProc d0/d1 → 7/13; font-program slice 2), 0 FP. +5 prohibited-xobject (6.2.9 → 5/5) +3 image-dictionary (6.2.8 → 3/4; the 4th is an inline image needing content-stream parsing) +4 permissions (6.1.12 → 4/4: /Perms keys + signature-reference Digest keys under DocMDP) +2 name-utf8 (6.1.8 → 2/2: every name valid UTF-8 after #-escape), all 0 FP. Ratchets to the current verified agreement (the earlier 899 lagged the 921 baseline). Standing −1 note: the 6.2.11.5 width check stays dropped on CIDFontType0/CFF fonts (CFF advance extraction false-positives on conformant reference files, PDFUA-Ref-2-08 — FP-safety outweighs one corpus detection). +2 simple-font glyph-present (6.2.11.4.1 t2 → 8/11) via the tri-state code→GID resolver (font-program slice 1); simple-font .notdef (6.2.11.8) is also live but adds 0 corpus files — its 5 remaining fail files are out-of-scope font types (classic Type1 / predefined-charset CFF / symbolic / Type0-non-identity → Unknown, FP-safe); 0 FP corpus-wide
            [ConformanceProfile.PdfA2u] = 19,    // + 6.2.11.3.1 (embedded-CMap supplement) catches 6-2-11-7-2-t01-fail-f
            [ConformanceProfile.PdfA3b] = 12,
            [ConformanceProfile.PdfUA1] = 296,   // FULL machine-checkable UA-1 parity (296/296). +3 table-header (clause 7.5 t1/t2 → 2/2 full: in a regular table every TD must connect to a header via /Headers→TH /ID or an explicit-Scope TH heading its column/row — PDF/UA-1 has no default scope; UaTableHeaderRule), 0 FP. +3 media-clip (clause 7.18.6.2 t1/t2 → 2/2 full: a Rendition-action media clip data dictionary needs a /CT content-type string and a correct /Alt multi-language text array; UaMediaClipRule), 0 FP. +1 encryption /P (clause 7.16 → 1/1 full: an encrypted file's /Encrypt /P must set bit 512, the accessibility-extraction permission; UaEncryptionRule), 0 FP. +2 role-map (clause 7.1 t6/t7 → 7.1 clause 16/16 full: no circular /RoleMap, no remapped standard type; document-level UaRoleMapRule), 0 FP. +3 pdfuaid-prefix (clause 5 t3/t4/t5 → 5/5 full: part/amd/corr must use the "pdfuaid" prefix; read per-property via XmlReader since XLinq collapses multiple prefixes on one URI), 0 FP. +3 CMap WMode/UseCMap (7.21.3.3 t2/t3 → 4/4 full; font-program slice 3), 0 FP. +6 from embedded-file widened to UA-1 7.11 (non-empty /F,/UF; filespecs from the catalog name tree AND FileAttachment annotation /FS → 6/6). Ratchets to the current verified agreement (the earlier 253 lagged the 275 baseline: slice-21 annotation rules + the incremental-update obj-stream resolution fix)
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
