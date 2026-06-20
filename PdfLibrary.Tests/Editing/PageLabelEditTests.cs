using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Optimization;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageLabelEditTests
{
    private static byte[] FourPageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("0", 100, 700))
            .AddPage(p => p.AddText("1", 100, 700))
            .AddPage(p => p.AddText("2", 100, 700))
            .AddPage(p => p.AddText("3", 100, 700))
            .ToByteArray();

    private static byte[] SaveReload(PdfDocument doc)
    {
        using var ms = new MemoryStream();
        doc.Edit().Save(ms, new PdfSaveOptions { RemoveOrphans = true });
        return ms.ToArray();
    }

    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    [Fact]
    public void SetRomanAndDecimal_RoundTrips_RangesSortedAndCovered()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        // Insert decimal first to verify sorting on write.
        edit.PageLabels.Set(3, PdfPageLabelStyle.Decimal);
        edit.PageLabels.Set(0, PdfPageLabelStyle.LowercaseRoman);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageLabels labels = reloaded.Edit().PageLabels;

        Assert.Equal(2, labels.Ranges.Count);
        Assert.Equal(0, labels.Ranges[0].StartPageIndex);
        Assert.Equal(PdfPageLabelStyle.LowercaseRoman, labels.Ranges[0].Style);
        Assert.Equal(3, labels.Ranges[1].StartPageIndex);
        Assert.Equal(PdfPageLabelStyle.Decimal, labels.Ranges[1].Style);

        // Get(index) maps each page to its covering range.
        Assert.Equal(0, labels.Get(0)!.StartPageIndex);
        Assert.Equal(0, labels.Get(2)!.StartPageIndex);
        Assert.Equal(3, labels.Get(3)!.StartPageIndex);

        // /Nums sorted on disk.
        var pl = (PdfDictionary)Deref(reloaded, reloaded.CatalogDictionary!.Get(new PdfName("PageLabels")))!;
        var nums = (PdfArray)Deref(reloaded, pl.Get(new PdfName("Nums")))!;
        Assert.Equal(0, ((PdfInteger)nums[0]).Value);
        Assert.Equal(3, ((PdfInteger)nums[2]).Value);
    }

    [Fact]
    public void PrefixAndStart_RoundTrip()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.PageLabels.Set(0, PdfPageLabelStyle.Decimal, prefix: "A-", start: 5);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageLabelRange r = reloaded.Edit().PageLabels.Get(1)!;
        Assert.Equal("A-", r.Prefix);
        Assert.Equal(5, r.StartNumber);
        Assert.Equal(PdfPageLabelStyle.Decimal, r.Style);
    }

    [Fact]
    public void Remove_DropsRange()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.PageLabels.Set(0, PdfPageLabelStyle.LowercaseRoman);
        edit.PageLabels.Set(2, PdfPageLabelStyle.Decimal);

        Assert.True(edit.PageLabels.Remove(2));
        Assert.False(edit.PageLabels.Remove(2));

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageLabels labels = reloaded.Edit().PageLabels;
        Assert.Single(labels.Ranges);
        Assert.Equal(0, labels.Ranges[0].StartPageIndex);
    }

    [Fact]
    public void Clear_RemovesPageLabels()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.PageLabels.Set(0, PdfPageLabelStyle.Decimal);
        edit.PageLabels.Clear();

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        Assert.Null(reloaded.CatalogDictionary!.Get(new PdfName("PageLabels")));
        Assert.Empty(reloaded.Edit().PageLabels.Ranges);
    }

    [Fact]
    public void AllStyles_MapCorrectly()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.PageLabels.Set(0, PdfPageLabelStyle.Decimal);
        edit.PageLabels.Set(1, PdfPageLabelStyle.UppercaseRoman);
        edit.PageLabels.Set(2, PdfPageLabelStyle.UppercaseLetters);
        edit.PageLabels.Set(3, PdfPageLabelStyle.LowercaseLetters);

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(SaveReload(doc)));
        PdfPageLabels labels = reloaded.Edit().PageLabels;
        Assert.Equal(PdfPageLabelStyle.Decimal, labels.Get(0)!.Style);
        Assert.Equal(PdfPageLabelStyle.UppercaseRoman, labels.Get(1)!.Style);
        Assert.Equal(PdfPageLabelStyle.UppercaseLetters, labels.Get(2)!.Style);
        Assert.Equal(PdfPageLabelStyle.LowercaseLetters, labels.Get(3)!.Style);
    }

    [Fact]
    public void SetSameStart_Overwrites()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        edit.PageLabels.Set(0, PdfPageLabelStyle.Decimal);
        edit.PageLabels.Set(0, PdfPageLabelStyle.LowercaseRoman);

        Assert.Single(edit.PageLabels.Ranges);
        Assert.Equal(PdfPageLabelStyle.LowercaseRoman, edit.PageLabels.Ranges[0].Style);
    }
}
