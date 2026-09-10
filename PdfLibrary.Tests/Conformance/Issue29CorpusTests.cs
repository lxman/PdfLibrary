using System;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// Issue 29: CC-MAIN 4000_4000080.pdf is non-conforming under ISO 19005-2 6.2.11.5 test 1.
/// veraPDF identifies used codes 77 (M), 109 (m), and 13 (unencoded) in the embedded Arial faces.
/// The engine used to report the document only because an unrelated Central-European caron mapping
/// took the raw-code fallback; correcting that mapping exposed this pre-existing width-resolution gap.
/// </summary>
[Trait("Category", "LocalOnly")]
public class Issue29CorpusTests
{
    private const string CorpusVariable = "PDFLIBRARY_CCMAIN_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\cc-main-2021-31-sample";

    [Fact]
    public void Arial_width_divergence_is_detected_for_the_codes_verapdf_identifies()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        string path = Path.Combine(root, "4000_4000080.pdf");
        Assert.SkipUnless(File.Exists(path), $"CC-MAIN fixture not present at {path} (LocalOnly)");

        using PdfDocument doc = PdfDocument.Load(path);
        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        Finding[] findings = new FontProgramRule().Check(context)
            .Where(f => ParitySnapshot.ClauseKey(f.Clause) == "6.2.11.5")
            .ToArray();

        Finding finding = Assert.Single(findings);
        Assert.Equal(3, finding.ObjectNumber);
        Assert.Contains("278 units", finding.Message);
    }
}
