using System.Text;
using System.Text.RegularExpressions;
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

    /// <summary>Issue 80. The body writer emits each object's real generation
    /// (<c>PdfDocumentSerializer.Write</c> passes <c>kvp.Value.GenerationNumber</c>), but
    /// <c>BuildXrefTable</c> hardcoded <c>00000</c> for every entry. Any object carrying a non-zero
    /// generation — ordinary in an incrementally-updated file — therefore got an xref entry that
    /// contradicted its own <c>N G obj</c> header. Pellucid's reader and pypdf both tolerate it;
    /// PDFBox does not, so veraPDF reported the saved file as "not a valid PDF" / "appears to be an
    /// encrypted PDF" and could not check it at all. Renumbering to generation 0 instead is NOT the
    /// fix: live <c>N 1 R</c> references elsewhere in the document would stop resolving.</summary>
    [Fact]
    public void Save_WritesEachObjectsRealGenerationIntoTheXrefEntry()
    {
        byte[] original = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(original));
        doc.MaterializeAllObjects();

        // Give one real object generation 1, exactly as an incremental update would.
        PdfIndirectReference added = doc.RegisterObject(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("StreamFiltersIssue80Probe")
        });
        int probe = added.ObjectNumber;
        doc.Objects[probe].GenerationNumber = 1;

        using var saved = new MemoryStream();
        doc.Save(saved);
        string text = Encoding.ASCII.GetString(saved.ToArray());

        // The body says generation 1 ...
        Assert.Contains($"\n{probe} 1 obj\n", text);

        // ... so the xref entry for that object must say 00001, not 00000.
        // Locate the table through startxref, the way a reader does — searching for "xref" would
        // find the tail of "startxref" instead.
        Match pointer = Regex.Match(text, @"startxref\s+(\d+)\s*%%EOF\s*$");
        Assert.True(pointer.Success, "no startxref/%%EOF pointer in the saved file");
        int xref = int.Parse(pointer.Groups[1].Value);
        Assert.StartsWith("xref", text[xref..], StringComparison.Ordinal);

        Match section = Regex.Match(text[xref..], @"^xref\s+(\d+)\s+(\d+)\s+");
        Assert.True(section.Success, "malformed xref subsection header");
        int start = int.Parse(section.Groups[1].Value);

        MatchCollection entries = Regex.Matches(
            text[(xref + section.Length)..], @"(\d{10}) (\d{5}) ([nf])");
        int index = probe - start;
        Assert.True(index >= 0 && index < entries.Count,
            $"object {probe} is outside the xref subsection");

        Assert.Equal("00001", entries[index].Groups[2].Value);
    }

    /// <summary>Issue 80's guard rail. A classic xref entry is exactly 20 bytes, so the generation
    /// field must stay five digits no matter what the source document carried. 65535 is the legal
    /// maximum (ISO 32000-1 7.5.4); a malformed larger value written straight through would emit six
    /// digits and shift the byte position of every entry after it, corrupting the whole table. This
    /// covers the clamp that prevents it — without this test that branch is unexercised.</summary>
    [Fact]
    public void Save_ClampsAnOutOfRangeGenerationSoXrefEntriesStayTwentyBytes()
    {
        byte[] original = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Hello", 100, 700))
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(original));
        doc.MaterializeAllObjects();

        PdfIndirectReference added = doc.RegisterObject(new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("StreamFiltersIssue80Probe")
        });
        doc.Objects[added.ObjectNumber].GenerationNumber = 999_999; // malformed, beyond 65535

        using var saved = new MemoryStream();
        doc.Save(saved);
        string text = Encoding.ASCII.GetString(saved.ToArray());

        Match pointer = Regex.Match(text, @"startxref\s+(\d+)\s*%%EOF\s*$");
        Assert.True(pointer.Success, "no startxref/%%EOF pointer in the saved file");
        int xref = int.Parse(pointer.Groups[1].Value);
        Match section = Regex.Match(text[xref..], @"^xref\s+(\d+)\s+(\d+)\s+");
        Assert.True(section.Success, "malformed xref subsection header");

        // Every entry is 20 bytes: 10-digit offset, space, 5-digit generation, space, flag, 2 EOL.
        string body = text[(xref + section.Length)..];
        int count = int.Parse(section.Groups[2].Value);
        for (var i = 0; i < count; i++)
        {
            string entry = body.Substring(i * 20, 20);
            Assert.Matches(@"^\d{10} \d{5} [nf] \n$", entry);
        }
    }
}
