using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// PDF/UA-1 reference-file pass-oracle. Every file in the PDF Association's reference set
/// (<see cref="PdfUaReferenceHarness"/>) is a real conformant PDF/UA-1 document, so the preflight must
/// return <b>zero</b> findings for each: the rule set is a strict subset of the standard, so any finding on
/// a conformant file is a false positive — the exact invariant the Matterhorn reframe gates on. This is the
/// companion to the veraPDF corpus oracle (<see cref="CorpusOracleTests"/>): the corpus proves detection
/// breadth against deliberately-broken fixtures, this proves 0 FP against documents accessibility tooling
/// accepts. LocalOnly (the reference set is not vendored).
/// </summary>
[Trait("Category", "LocalOnly")]
public class PdfUaReferenceOracleTests(ITestOutputHelper output)
{
    /// <summary>
    /// Reference files the current rule set still flags, each mapped to the reason it is tolerated. Empty:
    /// the two former false positives — the slice-19 metrics check on a CIDFontType0/CFF font
    /// (PDFUA-Ref-2-08, since narrowed out of the width check) and the missing /StructTreeRoot on the
    /// hybrid-reference file (PDFUA-Ref-2-09, since the engine follows /XRefStm) — are both fixed. Kept as
    /// the extension point: a genuinely-tolerated finding goes here with its justification, never silently.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnownBaseline =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [Fact]
    public void Reference_files_produce_no_findings()
    {
        Assert.SkipUnless(PdfUaReferenceHarness.IsAvailable,
            "PDFUA-Reference-Files not present (set PDFUA_REFERENCE_FILES)");

        var unexpected = new List<string>();
        var staleBaseline = new HashSet<string>(KnownBaseline.Keys);
        int total = 0;

        foreach (string path in PdfUaReferenceHarness.Files())
        {
            total++;
            string name = Path.GetFileName(path);

            List<string> findings;
            try
            {
                PreflightResult result = Preflighter.Check(path, ConformanceProfile.PdfUA1);
                findings = result.Findings
                    .Select(f => $"[{ParitySnapshot.ClauseKey(f.Clause)}] {f.RuleId}")
                    .ToList();
            }
            catch (Exception ex)
            {
                findings = [$"load-error:{ex.GetType().Name}"];
            }

            if (findings.Count == 0)
                continue;

            staleBaseline.Remove(name);
            if (!KnownBaseline.ContainsKey(name))
                unexpected.Add($"{name} => {string.Join(", ", findings)}");
        }

        output.WriteLine($"checked {total} PDF/UA-1 reference file(s); {unexpected.Count} unexpected, "
                         + $"{staleBaseline.Count} stale baseline");

        Assert.True(unexpected.Count == 0,
            $"{unexpected.Count} conformant PDF/UA-1 reference file(s) flagged (false positives): "
            + string.Join("; ", unexpected));
        Assert.True(staleBaseline.Count == 0,
            $"{staleBaseline.Count} baseline entr(y/ies) now pass — remove: {string.Join(", ", staleBaseline)}");
    }
}
