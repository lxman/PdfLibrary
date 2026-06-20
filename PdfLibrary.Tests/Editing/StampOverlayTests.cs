using System.Globalization;
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

    [Fact]
    public void Stamp_UnderNonInvariantCulture_ProducesValidContent()
    {
        // Load and materialize the doc under invariant culture (parser uses double.Parse without culture guard).
        // We then switch culture for the stamp+save step to verify the cm/builder fix holds.
        using var doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();

        CultureInfo previous = CultureInfo.CurrentCulture;
        using var ms = new MemoryStream();
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            edit.Pages.Stamp(0, s => s.Content(c => c.AddText("KULTUR", 50, 400, "Helvetica", 24)).Center());
            edit.Save(ms);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        ms.Position = 0;
        using var reloaded = PdfDocument.Load(ms);
        Assert.Contains("KULTUR", reloaded.GetPage(0)!.ExtractText());
    }

    [Fact]
    public void Stamp_DefaultSize_WithIndirectMediaBox_ProducesNonZeroBBox()
    {
        using var doc = PdfDocument.Load(new MemoryStream(OnePage()));
        PdfDocumentEditor edit = doc.Edit();
        PdfDictionary page = PageTreeOps.PageDicts(doc)[0];
        // Rewrite MediaBox to use indirect width/height entries.
        PdfIndirectReference w = doc.RegisterObject(new PdfReal(400));
        PdfIndirectReference h = doc.RegisterObject(new PdfReal(300));
        page[new PdfName("MediaBox")] = new PdfArray(new PdfInteger(0), new PdfInteger(0), w, h);

        edit.Pages.Stamp(0, s => s.Content(c => c.AddText("Z", 10, 10, "Helvetica", 8))); // default size

        var res = (PdfDictionary)page[new PdfName("Resources")];
        var xobjs = (PdfDictionary)res[new PdfName("XObject")];
        var stamp = (PdfStream)doc.GetObject(((PdfIndirectReference)xobjs[new PdfName("Stamp0")]).ObjectNumber)!;
        var bbox = (PdfArray)stamp.Dictionary[new PdfName("BBox")];
        Assert.Equal(400, ((PdfReal)bbox[2]).Value, 1);
        Assert.Equal(300, ((PdfReal)bbox[3]).Value, 1);
    }
}
