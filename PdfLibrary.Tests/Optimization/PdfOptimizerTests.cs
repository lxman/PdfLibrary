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

    // ── Task 1: ObjStm + xref-stream builders ────────────────────────────────

    [Fact]
    public void BuildObjectStream_HasObjStmShape()
    {
        var a = new PdfDictionary(); a[new PdfName("K")] = new PdfInteger(1); a.ObjectNumber = 4;
        var b = new PdfArray { new PdfInteger(2) }; b.ObjectNumber = 5;

        (PdfStream objStm, Dictionary<int, int> idx) =
            ObjectStreamWriter.BuildObjectStream(new List<(int, PdfObject)> { (4, a), (5, b) });

        Assert.Equal("ObjStm", ((PdfName)objStm.Dictionary[new PdfName("Type")]).Value);
        Assert.Equal(2, ((PdfInteger)objStm.Dictionary[new PdfName("N")]).Value);
        Assert.True(objStm.Dictionary.ContainsKey(new PdfName("First")));
        Assert.Equal(0, idx[4]); Assert.Equal(1, idx[5]);
        // Decoded payload must contain both objects' serialized forms.
        string decoded = System.Text.Encoding.ASCII.GetString(objStm.GetDecodedData());
        Assert.StartsWith("4 0 ", decoded); // header pair "objNum offset"
        Assert.Contains("/K 1", decoded);   // object a content
    }

    // ── Task 2: Compressed write path ────────────────────────────────────────

    [Fact]
    public void WriteCompressed_RoundTrips_BuiltDoc()
    {
        byte[] src = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello compressed world", 100, 700))
            .AddPage(p => p.AddText("Second page", 100, 700))
            .ToByteArray();

        using var ms = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(new MemoryStream(src)))
        {
            d.MaterializeAllObjects();
            ObjectStreamWriter.Write(d, ms, ObjectGraphWalker.CollectReachable(d));
        }
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        Assert.Contains("Hello compressed world", reloaded.ExtractAllText());
    }

    // ── Task 3: Wire into optimizer ───────────────────────────────────────────

    [Fact]
    public void Optimize_WithObjectStreams_BeatsClassic_AndStaysValid()
    {
        byte[] src = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("alpha", 50, 700))
            .AddPage(p => p.AddText("beta", 50, 700))
            .AddPage(p => p.AddText("gamma", 50, 700))
            .ToByteArray();

        using var classic = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(d, classic, new PdfOptimizationOptions { UseObjectStreams = false });

        using var packed = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(new MemoryStream(src)))
            PdfOptimizer.Optimize(d, packed, new PdfOptimizationOptions { UseObjectStreams = true });

        packed.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(packed);
        Assert.Equal(3, reloaded.PageCount);
        Assert.True(packed.Length < classic.Length, $"packed {packed.Length} should beat classic {classic.Length}");
    }

    // ── Task 4: Corpus regression ─────────────────────────────────────────────

    [Theory]
    [InlineData(@"TestPDFs\fw2.pdf")]
    [InlineData(@"PdfLibrary.Examples\TestPdfs\comprehensive.pdf")]
    public void Optimize_ObjStm_DoesNotBloat_AndStaysValid(string rel)
    {
        string path = Path.Combine(@"C:\Users\jorda\RiderProjects\PDF", rel);
        if (!File.Exists(path)) return;

        long orig = new FileInfo(path).Length;
        int pages, textLen;
        using (PdfDocument o = PdfDocument.Load(path)) { pages = o.PageCount; textLen = o.ExtractAllText().Length; }

        // Compare classic vs objstm
        using var msClassic = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(path))
            PdfOptimizer.Optimize(d, msClassic, new PdfOptimizationOptions { UseObjectStreams = false });

        using var ms = new MemoryStream();
        using (PdfDocument d = PdfDocument.Load(path))
            PdfOptimizer.Optimize(d, ms, new PdfOptimizationOptions { UseObjectStreams = true });
        ms.Position = 0;

        // Emit sizes for reporting
        Console.WriteLine($"[SIZE] {rel}: orig={orig} classic={msClassic.Length} objstm={ms.Length} ratio={ms.Length * 100.0 / orig:F1}%");

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(pages, reloaded.PageCount);
        Assert.Equal(textLen, reloaded.ExtractAllText().Length);
        // The object-stream rewrite must not balloon a compact object-stream PDF the way classic xref did.
        Assert.True(ms.Length <= orig * 1.05, $"{rel}: optimized {ms.Length} vs original {orig} — still bloating");
    }
}
