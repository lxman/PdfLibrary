using PdfLibrary.Builder;
using PdfLibrary.Builder.Bookmark;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class DestinationCodecTests
{
    // Build a two-page doc and get its document + the page indirect refs via PageTreeOps.Kids
    private static (PdfDocument doc, PdfIndirectReference page0, PdfIndirectReference page1) BuildDoc()
    {
        byte[] bytes = PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("A", 100, 700))
            .AddPage(p => p.AddText("B", 100, 700))
            .ToByteArray();
        PdfDocument doc = PdfDocument.Load(new MemoryStream(bytes));
        doc.Edit(); // materialize + flatten
        PdfArray kids = PageTreeOps.Kids(doc);
        var p0 = (PdfIndirectReference)kids[0];
        var p1 = (PdfIndirectReference)kids[1];
        return (doc, p0, p1);
    }

    [Fact]
    public void Encode_XYZ_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.At(0, 10.5, 700.0, 1.25);
        PdfArray arr = DestinationCodec.Encode(dest, p0);

        Assert.Equal(5, arr.Count);
        Assert.Equal(p0, arr[0]);
        Assert.IsType<PdfName>(arr[1]);
        Assert.Equal("XYZ", ((PdfName)arr[1]).Value);
        // coords must be PdfReal or PdfNull; check values
        Assert.Equal(10.5, GetCoord(arr[2]));
        Assert.Equal(700.0, GetCoord(arr[3]));
        Assert.Equal(1.25, GetCoord(arr[4]));
        doc.Dispose();
    }

    [Fact]
    public void Encode_XYZ_NullCoords_UsesNull()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.ToPage(0);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(5, arr.Count);
        Assert.IsType<PdfNull>(arr[2]);
        Assert.IsType<PdfNull>(arr[3]);
        Assert.IsType<PdfNull>(arr[4]);
        doc.Dispose();
    }

    [Fact]
    public void Encode_Fit_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitPage(0);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(2, arr.Count);
        Assert.Equal("Fit", ((PdfName)arr[1]).Value);
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitH_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitWidth(0, 700.0);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(3, arr.Count);
        Assert.Equal("FitH", ((PdfName)arr[1]).Value);
        Assert.Equal(700.0, GetCoord(arr[2]));
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitH_NullTop_UsesNull()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitWidth(0, null);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(3, arr.Count);
        Assert.IsType<PdfNull>(arr[2]);
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitV_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitHeight(0, 50.0);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(3, arr.Count);
        Assert.Equal("FitV", ((PdfName)arr[1]).Value);
        Assert.Equal(50.0, GetCoord(arr[2]));
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitB_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = new() { PageIndex = 0, Type = PdfDestinationType.FitB };
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(2, arr.Count);
        Assert.Equal("FitB", ((PdfName)arr[1]).Value);
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitBH_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = new() { PageIndex = 0, Type = PdfDestinationType.FitBH, Top = 300.0 };
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(3, arr.Count);
        Assert.Equal("FitBH", ((PdfName)arr[1]).Value);
        Assert.Equal(300.0, GetCoord(arr[2]));
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitBV_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = new() { PageIndex = 0, Type = PdfDestinationType.FitBV, Left = 100.0 };
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(3, arr.Count);
        Assert.Equal("FitBV", ((PdfName)arr[1]).Value);
        Assert.Equal(100.0, GetCoord(arr[2]));
        doc.Dispose();
    }

    [Fact]
    public void Encode_FitR_ProducesCorrectArray()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitRect(0, 10.0, 20.0, 200.0, 400.0);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        Assert.Equal(6, arr.Count);
        Assert.Equal("FitR", ((PdfName)arr[1]).Value);
        Assert.Equal(10.0, GetCoord(arr[2]));
        Assert.Equal(20.0, GetCoord(arr[3]));
        Assert.Equal(200.0, GetCoord(arr[4]));
        Assert.Equal(400.0, GetCoord(arr[5]));
        doc.Dispose();
    }

    [Fact]
    public void Decode_XYZ_ReturnsCorrectDestination()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination src = PdfDestination.At(0, 10.5, 700.0, 1.25);
        PdfArray arr = DestinationCodec.Encode(src, p0);
        PdfDestination? dec = DestinationCodec.Decode(doc, arr);
        Assert.NotNull(dec);
        Assert.Equal(0, dec.PageIndex);
        Assert.Equal(PdfDestinationType.XYZ, dec.Type);
        Assert.Equal(10.5, dec.Left);
        Assert.Equal(700.0, dec.Top);
        Assert.Equal(1.25, dec.Zoom);
        doc.Dispose();
    }

    [Fact]
    public void Decode_XYZ_NullCoords_ReturnsNullCoords()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination src = PdfDestination.ToPage(0);
        PdfArray arr = DestinationCodec.Encode(src, p0);
        PdfDestination? dec = DestinationCodec.Decode(doc, arr);
        Assert.NotNull(dec);
        Assert.Null(dec.Left);
        Assert.Null(dec.Top);
        Assert.Null(dec.Zoom);
        doc.Dispose();
    }

    [Fact]
    public void Decode_Page1_ResolvesCorrectPageIndex()
    {
        (PdfDocument doc, _, PdfIndirectReference p1) = BuildDoc();
        PdfDestination src = PdfDestination.FitPage(1);
        PdfArray arr = DestinationCodec.Encode(src, p1);
        PdfDestination? dec = DestinationCodec.Decode(doc, arr);
        Assert.NotNull(dec);
        Assert.Equal(1, dec.PageIndex);
        doc.Dispose();
    }

    [Fact]
    public void Decode_UnresolvablePage_ReturnsNull()
    {
        (PdfDocument doc, _, _) = BuildDoc();
        // array with bogus page ref
        var bogusRef = new PdfIndirectReference(99999, 0);
        var arr = new PdfArray(bogusRef, new PdfName("Fit"));
        PdfDestination? dec = DestinationCodec.Decode(doc, arr);
        Assert.Null(dec);
        doc.Dispose();
    }

    [Fact]
    public void Decode_Fit_ProducesCorrectDestination()
    {
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination src = PdfDestination.FitPage(0);
        PdfDestination? dec = DestinationCodec.Decode(doc, DestinationCodec.Encode(src, p0));
        Assert.NotNull(dec);
        Assert.Equal(PdfDestinationType.Fit, dec.Type);
        doc.Dispose();
    }

    [Fact]
    public void Encode_CoordsUseInvariantCulture()
    {
        // Spot-check: encode with a coord that would differ in de-DE (comma vs. dot)
        (PdfDocument doc, PdfIndirectReference p0, _) = BuildDoc();
        PdfDestination dest = PdfDestination.FitWidth(0, 700.5);
        PdfArray arr = DestinationCodec.Encode(dest, p0);
        // The array contains PdfReal objects — their ToPdfString() must use '.' not ','
        string pdf = arr.ToPdfString();
        Assert.Contains("700", pdf);
        Assert.DoesNotContain(",", pdf);
        doc.Dispose();
    }

    // Helper: pull double value from a PdfReal/PdfInteger, or null on PdfNull
    private static double? GetCoord(PdfObject obj) =>
        obj switch
        {
            PdfReal r => r.Value,
            PdfInteger i => (double)i.Value,
            PdfNull => null,
            _ => throw new InvalidOperationException($"Unexpected coord type: {obj.GetType().Name}")
        };
}
