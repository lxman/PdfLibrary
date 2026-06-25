using PdfLibrary.Builder;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing;

public class ViewerSettingsAdditionalPrefsTests
{
    private static PdfDocument OnePageDoc()
    {
        byte[] pdf = PdfDocumentBuilder.Create().AddPage(p => p.AddText("x", 100, 700)).ToByteArray();
        return PdfDocument.Load(new MemoryStream(pdf));
    }

    [Fact]
    public void AdditionalPreferences_RoundTrip()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        PdfViewerSettings vs = editor.ViewerSettings;

        vs.HideMenubar = true;
        vs.HideWindowUI = true;
        vs.NonFullScreenPageMode = PdfPageMode.UseOutlines;
        vs.Direction = PdfReadingDirection.RightToLeft;
        vs.PrintScaling = PdfPrintScaling.None;
        vs.Duplex = PdfDuplex.DuplexFlipLongEdge;

        Assert.True(vs.HideMenubar);
        Assert.True(vs.HideWindowUI);
        Assert.Equal(PdfPageMode.UseOutlines, vs.NonFullScreenPageMode);
        Assert.Equal(PdfReadingDirection.RightToLeft, vs.Direction);
        Assert.Equal(PdfPrintScaling.None, vs.PrintScaling);
        Assert.Equal(PdfDuplex.DuplexFlipLongEdge, vs.Duplex);
    }

    [Fact]
    public void AdditionalPreference_NullClears()
    {
        using PdfDocument doc = OnePageDoc();
        using PdfDocumentEditor editor = doc.Edit();
        editor.ViewerSettings.Direction = PdfReadingDirection.RightToLeft;

        editor.ViewerSettings.Direction = null;

        Assert.Null(editor.ViewerSettings.Direction);
    }
}
