using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Optimization;

public class PdfOptimizerTests
{
    private static PdfDocument LoadBuilt() =>
        PdfDocument.Load(new MemoryStream(
            PdfDocumentBuilder.Create()
                .AddPage(p => p.AddText("Hello", 100, 700))
                .ToByteArray()));

    [Fact]
    public void CollectReachable_IncludesCatalog_ExcludesOrphan()
    {
        using PdfDocument doc = LoadBuilt();
        doc.MaterializeAllObjects();
        doc.AddObject(9999, 0, new PdfDictionary()); // unreferenced orphan

        ISet<int> live = ObjectGraphWalker.CollectReachable(doc);

        Assert.Contains(doc.Trailer.Root!.ObjectNumber, live);
        Assert.DoesNotContain(9999, live);
    }

    [Fact]
    public void Write_WithLiveSet_DropsOrphan_PreservesPages()
    {
        using PdfDocument doc = LoadBuilt();
        doc.MaterializeAllObjects();
        doc.AddObject(9999, 0, new PdfDictionary());
        int fullCount = doc.Objects.Count;

        ISet<int> live = ObjectGraphWalker.CollectReachable(doc);
        using var ms = new MemoryStream();
        PdfDocumentSerializer.Write(doc, ms, live);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(1, reloaded.PageCount);
        Assert.True(reloaded.Objects.Count <= fullCount); // orphan 9999 not written
        reloaded.MaterializeAllObjects();
        Assert.DoesNotContain(9999, reloaded.Objects.Keys);
    }

    [Fact]
    public void CompressStreams_AddsFlate_Losslessly()
    {
        using PdfDocument doc = LoadBuilt();
        doc.MaterializeAllObjects();

        var before = doc.Objects.Values.OfType<PdfStream>()
            .ToDictionary(s => s.ObjectNumber, s => s.GetDecodedData());

        PdfOptimizer.CompressUncompressedStreamsForTest(doc);

        bool anyFlate = false;
        foreach (PdfStream s in doc.Objects.Values.OfType<PdfStream>())
        {
            Assert.Equal(before[s.ObjectNumber], s.GetDecodedData()); // lossless
            if (s.Dictionary.TryGetValue(PdfName.Filter, out PdfObject f) && f is PdfName { Value: "FlateDecode" })
                anyFlate = true;
        }
        Assert.True(anyFlate, "expected at least one stream to become FlateDecode");
    }

    [Fact]
    public void Optimize_BuiltDoc_StaysValid_AndNotLarger()
    {
        byte[] src = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("The quick brown fox jumps over the lazy dog. " + new string('x', 4000), 50, 700))
            .ToByteArray();

        using var plain = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(new MemoryStream(src))) d.Save(plain);

        using var opt = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(d, opt);

        opt.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(opt);
        Assert.Equal(1, reloaded.PageCount);
        Assert.True(opt.Length < plain.Length,
            $"optimized {opt.Length} should beat plain {plain.Length}"); // compressible content stream
    }

    [Theory]
    [InlineData(@"PdfLibrary.Examples\TestPdfs\comprehensive.pdf")]
    public void Optimize_CorpusFile_PreservesPagesAndText(string rel)
    {
        string path = Path.Combine(@"C:\Users\jorda\RiderProjects\PDF", rel);
        if (!File.Exists(path)) return; // corpus-dependent

        int pages; int textLen;
        using (PdfDocument o = PdfDocument.Load(path)) { pages = o.PageCount; textLen = o.ExtractAllText().Length; }

        using var opt = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(path)) PdfOptimizer.Optimize(d, opt);
        opt.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(opt);
        Assert.Equal(pages, reloaded.PageCount);
        Assert.Equal(textLen, reloaded.ExtractAllText().Length);
    }
}
