using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageRotateTests
{
    private static byte[] OnePage() =>
        PdfDocumentBuilder.Create().AddPage(p => p.AddText("rot", 100, 700)).ToByteArray();

    [Fact]
    public void Rotate_SetsRotation_AndRoundTrips()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.Rotate(0, 90);
            edit.Save(ms);
        }
        ms.Position = 0;
        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(90, reloaded.GetPage(0)!.Rotate);
    }

    [Fact]
    public void Rotate_Negative_NormalizesInto0To360()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.Rotate(0, -90);
        Assert.Equal(270, edit.Pages[0].Rotate);
    }

    [Fact]
    public void Rotate_NonMultipleOf90_Throws()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Throws<ArgumentException>(() => edit.Pages.Rotate(0, 45));
    }

    [Fact]
    public void RotateBy_AccumulatesFromCurrent()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.Rotate(0, 90);
        edit.Pages.RotateBy(0, 90);
        Assert.Equal(180, edit.Pages[0].Rotate);
    }
}
