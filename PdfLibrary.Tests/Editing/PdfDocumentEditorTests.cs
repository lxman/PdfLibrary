using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PdfDocumentEditorTests
{
    private static byte[] TwoPageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page one alpha", 100, 700))
            .AddPage(p => p.AddText("Page two beta", 100, 700))
            .ToByteArray();

    [Fact]
    public void Edit_ExposesPages_MatchingPageCount()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        Assert.Equal(2, edit.Pages.Count);
        Assert.Equal(doc.PageCount, edit.Pages.Count);
    }

    [Fact]
    public void Save_NoChanges_RoundTripsTextAndPageCount()
    {
        using var ms = new MemoryStream();
        using (PdfDocument doc = PdfDocument.Load(new MemoryStream(TwoPageDoc())))
            doc.Edit().Save(ms);
        ms.Position = 0;

        using PdfDocument reloaded = PdfDocument.Load(ms);
        Assert.Equal(2, reloaded.PageCount);
        string text = reloaded.ExtractAllText();
        Assert.Contains("Page one alpha", text);
        Assert.Contains("Page two beta", text);
    }
}
