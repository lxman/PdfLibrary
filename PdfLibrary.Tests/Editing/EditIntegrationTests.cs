using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class EditIntegrationTests
{
    private static byte[] Pages(params string[] texts)
    {
        var b = PdfDocumentBuilder.Create();
        foreach (string t in texts) b.AddPage(p => p.AddText(t, 100, 700));
        return b.ToByteArray();
    }

    [Fact]
    public void Save_WithObjectStreams_RoundTripsEditedDoc()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(Pages("PACKA", "PACKB"))))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.Rotate(0, 90);
            edit.Save(ms, new PdfSaveOptions { UseObjectStreams = true });
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        Assert.Equal(90, reloaded.GetPage(0)!.Rotate);
        string text = reloaded.ExtractAllText();
        Assert.Contains("PACKA", text);
        Assert.Contains("PACKB", text);
    }

    [Fact]
    public void Save_EncryptedInput_WithObjectStreams_IsUnencrypted()
    {
        byte[] enc = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("ENCPACK", 100, 700))
            .AddPage(p => p.AddText("ENCPACK2", 100, 700))
            .WithPassword("pw").ToByteArray();

        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(enc), "pw"))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Save(ms, new PdfSaveOptions { UseObjectStreams = true });
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.False(reloaded.IsEncrypted);
        Assert.Contains("ENCPACK", reloaded.ExtractAllText());
    }

    [Fact]
    public void AppendRange_AddsOnlyTheSlice()
    {
        using var ms = new MemoryStream();
        using (PdfDocument target = PdfDocument.Load(new MemoryStream(Pages("HOST"))))
        using (PdfDocument source = PdfDocument.Load(new MemoryStream(Pages("S0", "S1", "S2"))))
        {
            PdfDocumentEditor edit = target.Edit();
            edit.Pages.AppendRange(source, start: 1, count: 1); // only S1
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("HOST", text);
        Assert.Contains("S1", text);
        Assert.DoesNotContain("S0", text);
        Assert.DoesNotContain("S2", text);
    }

    [Fact]
    public void Save_ToFilePath_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), $"edit_save_{Guid.NewGuid():N}.pdf");
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(Pages("FILEA", "FILEB"))))
                doc.Edit().Save(path);
            using PdfDocument reloaded = PdfDocument.Load(path);
            Assert.Equal(2, reloaded.PageCount);
            Assert.Contains("FILEA", reloaded.ExtractAllText());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CreateBlank_StartsEmpty_AcceptsInsertedPage()
    {
        using var edit = PdfDocumentEditor.CreateBlank();
        Assert.Empty(edit.Pages);
        edit.Pages.InsertBlank(0, 200, 300);
        Assert.Single(edit.Pages);
    }

    [Fact]
    public void Merge_WithEncryptedSource_PreservesText()
    {
        byte[] enc = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("MERGEENC", 100, 700))
            .WithPassword("pw").ToByteArray();

        using PdfDocument a = PdfDocument.Load(new MemoryStream(Pages("MERGEPLAIN")));
        using PdfDocument b = PdfDocument.Load(new MemoryStream(enc), "pw");

        using var ms = new MemoryStream();
        using (PdfDocument merged = PdfDocumentEditor.Merge([a, b]))
            merged.Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.False(reloaded.IsEncrypted);
        Assert.Equal(2, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("MERGEPLAIN", text);
        Assert.Contains("MERGEENC", text);
    }

    [Fact]
    public void RemoveAt_PromotesGrandchildBookmark_WhenParentDeleted()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("CHAPTER", 100, 700))
            .AddPage(p => p.AddText("SECTION", 100, 700))
            .AddBookmark("Chapter 1", bm => bm.ToPage(0).AddChild("Section 1.1", c => c.ToPage(1)))
            .ToByteArray();

        using PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.RemoveAt(0); // delete CHAPTER (parent bookmark targets it); child targets surviving page

        List<string> titles = CollectOutlineTitles(doc);
        Assert.Contains("Section 1.1", titles);   // child promoted, survives
        Assert.DoesNotContain("Chapter 1", titles); // parent stripped
    }

    private static List<string> CollectOutlineTitles(PdfDocument doc)
    {
        var titles = new List<string>();
        if (Deref(doc, doc.CatalogDictionary?.Get(new PdfName("Outlines"))) is not PdfDictionary outlines)
            return titles;
        Walk(outlines.Get(new PdfName("First")));
        return titles;

        void Walk(PdfObject? reference)
        {
            var guard = 0;
            while (reference is not null && guard++ < 10000)
            {
                if (Deref(doc, reference) is not PdfDictionary item) break;
                if (item.Get(new PdfName("Title")) is PdfString t) titles.Add(t.Value);
                Walk(item.Get(new PdfName("First")));
                reference = item.Get(new PdfName("Next"));
            }
        }
    }

    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;
}
