using System.Globalization;
using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class TextStringRoundTripTests
{
    private static byte[] BlankDoc()
    {
        using var ms = new MemoryStream();
        using var doc = PdfDocument.CreateEmpty();
        doc.Save(ms);
        return ms.ToArray();
    }

    private static byte[] OnePageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700))
            .ToByteArray();

    private static byte[] SaveAndGetBytes(PdfDocument doc)
    {
        var ms = new MemoryStream();
        doc.Edit().Save(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("Simple Title")]
    [InlineData("Café Ω — 日本語 \U0001F600")]
    public void Metadata_Title_RoundTrips_AcrossSaveAndReload(string title)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(BlankDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.Metadata.Title = title;

        byte[] saved = SaveAndGetBytes(doc);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(saved));
        PdfDocumentEditor edit2 = reloaded.Edit();
        Assert.Equal(title, edit2.Metadata.Title);
    }

    [Fact]
    public void Outline_Title_RoundTrips_NonAscii_UnderDeDe()
    {
        var deDe = new CultureInfo("de-DE");
        CultureInfo saved = CultureInfo.CurrentCulture;
        CultureInfo savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = deDe;
            CultureInfo.CurrentUICulture = deDe;

            const string outlineTitle = "Kapitel — 日本語";

            using PdfDocument doc = PdfDocument.Load(new MemoryStream(OnePageDoc()));
            PdfDocumentEditor edit = doc.Edit();
            edit.Outlines.Add(outlineTitle, PdfDestination.ToPage(0));

            var ms = new MemoryStream();
            edit.Save(ms);
            ms.Position = 0;

            using PdfDocument reloaded = PdfDocument.Load(ms);
            PdfDocumentEditor edit2 = reloaded.Edit();
            PdfOutlineItem only = Assert.Single(edit2.Outlines);
            Assert.Equal(outlineTitle, only.Title);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }
}
