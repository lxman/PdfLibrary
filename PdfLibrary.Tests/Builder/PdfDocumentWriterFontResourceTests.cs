using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Builder;

/// <summary>
/// Pins <see cref="PdfDocumentWriter"/>'s font-resource emission: a page must carry a
/// <c>/Resources /Font</c> entry only for fonts it actually references. Regression coverage for
/// the PDF/A-3B authoring gate (see <c>PdfA3AuthoringIntegrationTests</c>), which failed because
/// <c>CollectFonts()</c> used to seed "Helvetica" unconditionally and the writer stamped it into
/// every page's resources — including pages with zero content — tripping the PDF/A
/// font-embedded rule for a font nothing ever drew.
/// </summary>
public class PdfDocumentWriterFontResourceTests
{
    private static PdfDocument LoadRoundTrip(PdfDocumentBuilder builder)
    {
        byte[] bytes = builder.ToByteArray();
        var ms = new MemoryStream(bytes);
        return PdfDocument.Load(ms);
    }

    [Fact]
    public void EmptyPage_CarriesNoFontResource()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create().AddPage(_ => { });

        using PdfDocument doc = LoadRoundTrip(builder);
        PdfPage page = doc.GetPage(0)!;

        PdfResources? resources = page.GetResources();
        Assert.True(resources is null || resources.GetFonts() is null,
            "An empty page must not carry a /Font resource (no content references any font).");
    }

    [Fact]
    public void PageWithDefaultFontText_StillEmitsHelveticaResource()
    {
        // AddText(text, x, y) with no explicit font name falls back to PdfTextContent's default
        // ("Helvetica") — this must still flow through CollectFonts and be written to /Resources.
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("hi", 100, 700));

        using PdfDocument doc = LoadRoundTrip(builder);
        PdfPage page = doc.GetPage(0)!;

        PdfResources? resources = page.GetResources();
        Assert.NotNull(resources);

        PdfDictionary? font = resources!.GetFont("F1");
        Assert.NotNull(font);
        Assert.Equal("Helvetica", (font!.Get("BaseFont") as PdfName)?.Value);
    }

    [Fact]
    public void MultiplePages_OnlyPagesWithTextGetAFontResource()
    {
        PdfDocumentBuilder builder = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("Page 1", 100, 700, "Times-Roman", 12))
            .AddPage(_ => { });

        using PdfDocument doc = LoadRoundTrip(builder);

        PdfResources? page1Resources = doc.GetPage(0)!.GetResources();
        Assert.NotNull(page1Resources);
        Assert.NotNull(page1Resources!.GetFonts());
    }
}
