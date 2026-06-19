using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageRemoveTests
{
    private static byte[] ThreePages() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("ONEONE", 100, 700))
            .AddPage(p => p.AddText("TWOTWO", 100, 700))
            .AddPage(p => p.AddText("THREETHREE", 100, 700))
            .ToByteArray();

    [Fact]
    public void RemoveAt_DropsPage_AndOrphanedContent_OnSave()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePages())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.RemoveAt(1); // remove TWO
            edit.Save(ms);          // RemoveOrphans defaults true
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("ONEONE", text);
        Assert.Contains("THREETHREE", text);
        Assert.DoesNotContain("TWOTWO", text); // orphaned content stream GC'd
    }

    [Fact]
    public void RemoveAt_LastRemainingPage_Throws()
    {
        byte[] one = PdfDocumentBuilder.Create().AddPage(p => p.AddText("solo", 100, 700)).ToByteArray();
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(one));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Throws<InvalidOperationException>(() => edit.Pages.RemoveAt(0));
    }
}
