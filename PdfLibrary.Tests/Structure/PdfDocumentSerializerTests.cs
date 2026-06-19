using System.Text;
using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Structure;

public class PdfDocumentSerializerTests
{
    [Fact]
    public void MaterializeAllObjects_LoadsEveryInUseXrefEntry()
    {
        string path = System.IO.Path.Combine(
            @"C:\Users\jorda\RiderProjects\PDF",
            @"PDFs\pdf20examples\Simple PDF 2.0 file.pdf");
        if (!System.IO.File.Exists(path)) return; // corpus-dependent; skip if absent

        using PdfDocument doc = PdfDocument.Load(path);
        doc.MaterializeAllObjects();

        int inUse = doc.XrefTable.Entries.Count(e => e.IsInUse);
        Assert.True(doc.Objects.Count >= inUse,
            $"materialized {doc.Objects.Count} objects but xref has {inUse} in-use entries");
    }


    [Fact]
    public void SerializeIndirectObject_Dictionary_WrapsInObjEndobj()
    {
        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog")
        };

        string text = Encoding.ASCII.GetString(
            PdfDocumentSerializer.SerializeIndirectObject(5, 0, dict));

        Assert.StartsWith("5 0 obj\n", text);
        Assert.Contains("/Type", text);
        Assert.Contains("/Catalog", text);
        Assert.EndsWith("endobj\n", text);
    }

    [Fact]
    public void SerializeIndirectObject_Stream_EmitsRealBytesNotPlaceholder()
    {
        var stream = new PdfStream(new PdfDictionary(), "hello stream"u8.ToArray());

        string text = Encoding.ASCII.GetString(
            PdfDocumentSerializer.SerializeIndirectObject(7, 0, stream));

        Assert.StartsWith("7 0 obj\n", text);
        Assert.Contains("stream\n", text);
        Assert.Contains("hello stream", text);              // real data...
        Assert.DoesNotContain("bytes of binary data", text); // ...not the ToPdfString placeholder
        Assert.Contains("endstream", text);
    }

    [Fact]
    public void Save_BuiltDocument_RoundTripsPageCount()
    {
        byte[] original = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .AddPage(p => p.AddText("World", 100, 700))
            .ToByteArray();

        using PdfDocument loaded = PdfDocument.Load(new MemoryStream(original));

        using var saved = new MemoryStream();
        loaded.Save(saved);
        saved.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(saved);
        Assert.Equal(2, reloaded.PageCount);
        Assert.NotNull(reloaded.GetPage(0));
    }

    private const string CorpusRoot = @"C:\Users\jorda\RiderProjects\PDF";

    [Theory]
    [InlineData(@"TestPDFs\SimpleTest1.pdf")]
    [InlineData(@"TestPDFs\Resume.pdf")]
    [InlineData(@"PDFs\pdf20examples\Simple PDF 2.0 file.pdf")]
    [InlineData(@"PdfLibrary.Examples\TestPdfs\comprehensive.pdf")]
    public void Save_CorpusFile_PreservesPagesAndText(string relPath)
    {
        string path = System.IO.Path.Combine(CorpusRoot, relPath);
        if (!System.IO.File.Exists(path)) return; // corpus-dependent; skip if absent

        using PdfDocument original = PdfDocument.Load(path);
        int pages = original.PageCount;
        int textLen = original.ExtractAllText().Length;

        using var ms = new MemoryStream();
        original.Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(pages, reloaded.PageCount);
        Assert.Equal(textLen, reloaded.ExtractAllText().Length);
    }

    [Fact]
    public void Save_EncryptedDocument_ThrowsNotSupported()
    {
        string path = System.IO.Path.Combine(CorpusRoot,
            @"TestPDFs\targeted_custom_generated\EncryptedAes128_EmptyPassword.pdf");
        if (!System.IO.File.Exists(path)) return;

        using PdfDocument doc = PdfDocument.Load(path, "");
        using var ms = new MemoryStream();
        Assert.Throws<NotSupportedException>(() => doc.Save(ms));
    }
}
