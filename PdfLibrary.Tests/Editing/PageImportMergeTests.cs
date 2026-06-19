using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageImportMergeTests
{
    private static byte[] OnePage(string text) =>
        PdfDocumentBuilder.Create().AddPage(p => p.AddText(text, 100, 700)).ToByteArray();

    private static byte[] Pages(params string[] texts)
    {
        var b = PdfDocumentBuilder.Create();
        foreach (string t in texts) b.AddPage(p => p.AddText(t, 100, 700));
        return b.ToByteArray();
    }

    [Fact]
    public void Import_AddsSourcePage_TextPreserved()
    {
        using var ms = new MemoryStream();
        using (PdfDocument target = PdfDocument.Load(new MemoryStream(OnePage("TARGETONLY"))))
        using (PdfDocument source = PdfDocument.Load(new MemoryStream(OnePage("IMPORTEDPAGE"))))
        {
            PdfDocumentEditor edit = target.Edit();
            edit.Pages.Import(source, sourceIndex: 0, at: 1);
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        Assert.Contains("TARGETONLY", reloaded.GetPage(0)!.ExtractText());
        Assert.Contains("IMPORTEDPAGE", reloaded.GetPage(1)!.ExtractText());
    }

    [Fact]
    public void Duplicate_CopiesPageWithinDocument()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage("CLONEME")));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.Duplicate(0, at: 1);
        Assert.Equal(2, edit.Pages.Count);
        Assert.Contains("CLONEME", edit.Pages[1].ExtractText());
    }

    [Fact]
    public void Append_AddsAllSourcePages()
    {
        using var ms = new MemoryStream();
        using (PdfDocument target = PdfDocument.Load(new MemoryStream(OnePage("HOST"))))
        using (PdfDocument source = PdfDocument.Load(new MemoryStream(Pages("ADDA", "ADDB"))))
        {
            PdfDocumentEditor edit = target.Edit();
            edit.Append(source);
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(3, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("HOST", text);
        Assert.Contains("ADDA", text);
        Assert.Contains("ADDB", text);
    }

    [Fact]
    public void Merge_CombinesAllSources()
    {
        using PdfDocument a = PdfDocument.Load(new MemoryStream(Pages("MA1", "MA2")));
        using PdfDocument b = PdfDocument.Load(new MemoryStream(OnePage("MB1")));

        using var ms = new MemoryStream();
        using (PdfDocument merged = PdfDocumentEditor.Merge([a, b]))
            merged.Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(3, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("MA1", text);
        Assert.Contains("MA2", text);
        Assert.Contains("MB1", text);
    }

    [Fact]
    public void Extract_ProducesNewDocumentWithRange()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(Pages("EX0", "EX1", "EX2"))))
        {
            PdfDocumentEditor edit = doc.Edit();
            using PdfDocument extracted = edit.Extract(start: 1, count: 1);
            extracted.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(1, reloaded.PageCount);
        Assert.Contains("EX1", reloaded.GetPage(0)!.ExtractText());
        Assert.DoesNotContain("EX0", reloaded.ExtractAllText());
    }

    [Fact]
    public void Import_FromEncryptedSource_PreservesText()
    {
        // Encrypted source, loaded (NOT edited) — the cloner must decrypt stream bytes during the copy.
        byte[] enc = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("SECRETPAGE", 100, 700))
            .WithPassword("pw")
            .ToByteArray();

        using var ms = new MemoryStream();
        using (PdfDocument target = PdfDocument.Load(new MemoryStream(OnePage("HOSTPAGE"))))
        using (PdfDocument source = PdfDocument.Load(new MemoryStream(enc), "pw"))
        {
            Assert.True(source.IsEncrypted);
            PdfDocumentEditor edit = target.Edit();
            edit.Pages.Import(source, 0, 1);
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.False(reloaded.IsEncrypted);
        Assert.Equal(2, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("HOSTPAGE", text);
        Assert.Contains("SECRETPAGE", text);
    }
}
