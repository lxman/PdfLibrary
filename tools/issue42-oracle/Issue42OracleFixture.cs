using System.Linq;
using System.Text;
using PdfLibrary.Conformance;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Conformance;

/// <summary>
/// THROWAWAY — builds the issue-42 renderer-oracle fixture, then delete.
///
/// <para>Issue 42 asks what a reader should do with a CID that falls BEYOND a /CIDToGIDMap stream's
/// covered range. ISO 32000-2 9.7.6.3 says "the glyph for CID 0 shall be substituted"; ISO 32000-1's
/// same subclause is SILENT (the sentence is a PDF 2.0 addition), and the CIDFont table in both
/// editions defines the map only positionally. So the spec alone does not settle what real readers
/// do — this produces a fixture to ask them.</para>
///
/// <para>Method: take a real corpus document with an embedded CIDFontType2 whose /CIDToGIDMap is a
/// stream covering many CIDs, and rewrite that stream to cover only CID 0. Every CID the page draws
/// then falls beyond coverage. Rendering the original and the truncated copy in poppler/mutool/gs
/// separates the two candidate behaviours: identity (CID used as GID — text still shows, wrong or
/// right) versus .notdef (text blanks).</para>
/// </summary>
[Trait("Category", "LocalOnly")]
public class Issue42OracleFixture
{
    private const string Corpus = @"D:\PdfCorpora\real-world\local-708";
    private static string OutDir =>
        Environment.GetEnvironmentVariable("ISSUE42_OUT") ?? Path.GetTempPath();

    [Fact]
    public void Build()
    {
        var sb = new StringBuilder();
        var built = 0;

        foreach (string file in Directory.GetFiles(Corpus, "*.pdf").OrderBy(f => f, StringComparer.Ordinal))
        {
            if (built >= 3)
                break;

            try
            {
                if (!TryBuild(file, sb, ref built))
                    continue;
            }
            catch (Exception ex)
            {
                sb.AppendLine($"  (skip {Path.GetFileName(file)}: {ex.GetType().Name})");
            }
        }

        File.WriteAllText(Path.Combine(OutDir, "issue42-fixture.txt"), sb.ToString());
        Assert.True(built > 0, "no CIDFontType2 + CIDToGIDMap-stream candidate found");
    }

    private static bool TryBuild(string file, StringBuilder sb, ref int built)
    {
        using PdfDocument doc = PdfDocument.Load(
            new FileStream(file, FileMode.Open, FileAccess.Read), string.Empty);
        var ctx = new ConformanceContext(doc, ConformanceProfile.PdfA2b);

        // A descendant CIDFontType2 carrying a /CIDToGIDMap STREAM (not /Identity) with real coverage.
        foreach (PdfDictionary font in ctx.ReferencedFonts)
        {
            if ((Resolve(doc, font.Get("Subtype")) as PdfName)?.Value != "CIDFontType2")
                continue;
            if (Resolve(doc, font.Get("CIDToGIDMap")) is not PdfStream map)
                continue;

            int covered = map.GetDecodedData(doc.Decryptor).Length / 2;
            if (covered < 8)
                continue; // want a map with real coverage, so truncation is a clear change

            // Truncate to CID 0 only: every drawn CID > 0 now falls beyond the covered range.
            map.SetEncodedData(new byte[2], "FlateDecode");

            string outPath = Path.Combine(OutDir, $"issue42-truncated-{built}.pdf");
            doc.Save(outPath);

            sb.AppendLine($"[{built}] {Path.GetFileName(file)}");
            sb.AppendLine($"     covered CIDs before : {covered}");
            sb.AppendLine($"     covered CIDs after  : 1  (CID 0 only)");
            sb.AppendLine($"     pages               : {ctx.Pages.Count}");
            sb.AppendLine($"     original            : {file}");
            sb.AppendLine($"     truncated           : {outPath}");
            sb.AppendLine();
            built++;
            return true;
        }

        return false;
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.ResolveReference(r) : obj;
}
