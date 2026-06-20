using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class StampOverlayTests
{
    private static byte[] OnePage() =>
        PdfDocumentBuilder.Create().AddPage(p => p.AddText("BODYTEXT", 100, 700)).ToByteArray();

    [Fact]
    public void Stamp_TextOverlay_RoundTrips_BothTextsExtract()
    {
        using var ms = new MemoryStream();
        using (var doc = PdfDocument.Load(new MemoryStream(OnePage())))
        {
            PdfDocumentEditor edit = doc.Edit();
            edit.Pages.Stamp(0, s => s.Content(c => c.AddText("STAMPED", 50, 400, "Helvetica", 24)));
            edit.Save(ms);
        }
        ms.Position = 0;
        using var reloaded = PdfDocument.Load(ms);
        Assert.Equal(1, reloaded.PageCount);
        string text = reloaded.GetPage(0)!.ExtractText();
        Assert.Contains("BODYTEXT", text);
        Assert.Contains("STAMPED", text);
    }

    [Fact]
    public void Stamp_Opacity_RegistersExtGState()
    {
        using var doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Pages.Stamp(0, s => s.Content(c => c.AddText("X", 10, 10, "Helvetica", 8)).Opacity(0.25));

        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        var res = (PdfDictionary)page[new PdfName("Resources")];
        Assert.True(res.ContainsKey(new PdfName("ExtGState")));
    }

    [Fact]
    public void Stamp_OutOfRange_Throws()
    {
        using var doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Throws<ArgumentOutOfRangeException>(() => edit.Pages.Stamp(5, s => s.Content(c => { })));
    }
}
