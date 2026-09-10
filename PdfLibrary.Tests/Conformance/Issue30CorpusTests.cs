using System;
using System.IO;
using System.Linq;
using PdfLibrary.Conformance;
using PdfLibrary.Conformance.Rules;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

[Trait("Category", "LocalOnly")]
public class Issue30CorpusTests
{
    private const string CorpusVariable = "PDFLIBRARY_CCMAIN_CORPUS";
    private const string DefaultCorpus = @"D:\PdfCorpora\real-world\cc-main-2021-31-sample";

    [Fact]
    public void Symbolic_truetype_object_23_uses_its_symbol_cmap()
    {
        string root = Environment.GetEnvironmentVariable(CorpusVariable) ?? DefaultCorpus;
        string path = Path.Combine(root, "0000_0000450.pdf");
        Assert.SkipUnless(File.Exists(path), $"CC-MAIN fixture not present at {path} (LocalOnly)");

        using PdfDocument doc = PdfDocument.Load(path);
        var context = new ConformanceContext(doc, ConformanceProfile.PdfA2b);
        UsedFontCodes[] usages = context.UsedTextGlyphs.Where(
            u => u.Font.FontDictionary.ObjectNumber == 23).ToArray();
        Assert.NotEmpty(usages);
        UsedFontCodes usage = usages[0];
        var widths = (PdfArray)context.Resolve(usage.Font.FontDictionary.Get("Widths"))!;
        var metrics = usage.Font.GetEmbeddedMetrics()!;

        Assert.True(usage.Font.GetDescriptor()?.IsSymbolic);
        Assert.True(metrics.HasSymbolCmapEncoding());
        Assert.False(metrics.HasUnicodeCmapEncoding());
        Assert.Equal(3, metrics.GetGlyphIdBySymbolCode(5));
        Assert.Equal(86, metrics.GetGlyphIdByUnicode(' '));

        WidthComparison space = Assert.Single(ProgramWidthResolver.Simple(
            usage.Font, metrics, widths, usages.SelectMany(u => u.Codes).Distinct(), isTrueType: true),
            comparison => comparison.Code == 5);
        Assert.Equal(3, space.Gid);
        Assert.Equal(278, space.Declared);
        Assert.InRange(space.Program, 277.5, 278.5);

        Finding[] objectFindings = new FontProgramRule().Check(context)
            .Where(f => f.ObjectNumber == 23)
            .ToArray();
        Assert.DoesNotContain(objectFindings,
            finding => ParitySnapshot.ClauseKey(finding.Clause) == "6.2.11.5");
        Assert.Contains(objectFindings,
            finding => ParitySnapshot.ClauseKey(finding.Clause) == "6.2.11.8");
    }
}
