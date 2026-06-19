using PdfLibrary.Builder;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageInsertBlankTests
{
    private static byte[] OnePage() =>
        PdfDocumentBuilder.Create().AddPage(p => p.AddText("orig", 100, 700)).ToByteArray();

    [Fact]
    public void InsertBlank_AddsPage_WithMediaBox_AndRoundTrips()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage())))
        {
            PdfDocumentEditor edit = doc.Edit();
            PdfPage inserted = edit.Pages.InsertBlank(0, 200, 300);
            Assert.Equal(200, inserted.Width, 3);
            Assert.Equal(300, inserted.Height, 3);
            Assert.Equal(2, edit.Pages.Count);
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        Assert.Equal(200, reloaded.GetPage(0)!.Width, 3);
        Assert.Contains("orig", reloaded.GetPage(1)!.ExtractText());
    }
}
