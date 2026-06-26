using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class StampAllTests
{
    private static byte[] ThreePages() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("P1", 100, 700))
            .AddPage(p => p.AddText("P2", 100, 700))
            .AddPage(p => p.AddText("P3", 100, 700))
            .ToByteArray();

    [Fact]
    public void StampAll_Watermark_OnEveryPage()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePages())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.StampAll(s => s.Watermark("DRAFT").Diagonal().Opacity(0.2));
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(3, reloaded.PageCount);
        for (var i = 0; i < 3; i++)
        {
            string t = reloaded.GetPage(i)!.ExtractText();
            Assert.Contains($"P{i + 1}", t);
            Assert.Contains("DRAFT", t);
        }
    }

    [Fact]
    public void StampRange_OnlyStampsTheRange()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePages())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.StampRange(1, 1, s => s.Watermark("MIDONLY").Center());
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.DoesNotContain("MIDONLY", reloaded.GetPage(0)!.ExtractText());
        Assert.Contains("MIDONLY", reloaded.GetPage(1)!.ExtractText());
        Assert.DoesNotContain("MIDONLY", reloaded.GetPage(2)!.ExtractText());
    }
}
