using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageMoveTests
{
    private static byte[] ThreePages() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("ALPHA", 100, 700))
            .AddPage(p => p.AddText("BETA", 100, 700))
            .AddPage(p => p.AddText("GAMMA", 100, 700))
            .ToByteArray();

    [Fact]
    public void Move_ReordersPages_AndRoundTrips()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePages())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.Move(0, 2); // ALPHA goes to the end
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(3, reloaded.PageCount);
        Assert.Contains("BETA", reloaded.GetPage(0)!.ExtractText());
        Assert.Contains("ALPHA", reloaded.GetPage(2)!.ExtractText());
    }

    [Fact]
    public void Move_OutOfRange_Throws()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(ThreePages()));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Throws<ArgumentOutOfRangeException>(() => edit.Pages.Move(0, 9));
    }
}
