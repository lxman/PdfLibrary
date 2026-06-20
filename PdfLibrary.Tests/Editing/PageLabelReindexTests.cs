using PdfLibrary.Builder;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class PageLabelReindexTests
{
    private static byte[] FourPageDoc() =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddText("0", 100, 700))
            .AddPage(p => p.AddText("1", 100, 700))
            .AddPage(p => p.AddText("2", 100, 700))
            .AddPage(p => p.AddText("3", 100, 700))
            .ToByteArray();

    private static (PdfDocument doc, PdfDocumentEditor edit) Labeled()
    {
        PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();
        // 0 -> roman (i, ii, iii), 3 -> decimal
        edit.PageLabels.Set(0, PdfPageLabelStyle.LowercaseRoman);
        edit.PageLabels.Set(3, PdfPageLabelStyle.Decimal);
        return (doc, edit);
    }

    private static List<(int start, PdfPageLabelStyle style)> Snapshot(PdfPageLabels labels) =>
        labels.Ranges.Select(r => (r.StartPageIndex, r.Style)).ToList();

    [Fact]
    public void RemoveAt_PreviewCase_DropsBoundaryAndShifts()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            edit.Pages.RemoveAt(1); // remove a page inside the roman range
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[]
            {
                (0, PdfPageLabelStyle.LowercaseRoman),
                (2, PdfPageLabelStyle.Decimal)
            }, snap);
        }
    }

    [Fact]
    public void RemoveAt_ExactBoundary_DropsThatRange()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            edit.Pages.RemoveAt(3); // remove the page that starts the decimal range
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[] { (0, PdfPageLabelStyle.LowercaseRoman) }, snap);
        }
    }

    [Fact]
    public void InsertBlank_ShiftsStartsAtOrAfter()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            edit.Pages.InsertBlank(1, 200, 200); // insert inside roman range
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[]
            {
                (0, PdfPageLabelStyle.LowercaseRoman),
                (4, PdfPageLabelStyle.Decimal)
            }, snap);
        }
    }

    [Fact]
    public void InsertBlankAtBoundary_ShiftsTheBoundaryRange()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            edit.Pages.InsertBlank(3, 200, 200); // start>=3 shifts +1
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[]
            {
                (0, PdfPageLabelStyle.LowercaseRoman),
                (4, PdfPageLabelStyle.Decimal)
            }, snap);
        }
    }

    [Fact]
    public void Move_RemodelsAsRemovePlusInsert()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            // Move page 3 (decimal boundary) to front.
            edit.Pages.Move(3, 0);
            // remove(3): decimal range at 3 dropped, nothing else shifts (no start > 3).
            // insert(0): roman start 0 shifts to 1.
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[] { (1, PdfPageLabelStyle.LowercaseRoman) }, snap);
        }
    }

    [Fact]
    public void Import_ShiftsAtInsertIndex()
    {
        (PdfDocument doc, PdfDocumentEditor edit) = Labeled();
        using (doc)
        {
            using PdfDocument src = PdfDocument.Load(new MemoryStream(FourPageDoc()));
            edit.Pages.Import(src, 0, 0); // insert at 0
            List<(int, PdfPageLabelStyle)> snap = Snapshot(edit.PageLabels);
            Assert.Equal(new[]
            {
                (1, PdfPageLabelStyle.LowercaseRoman),
                (4, PdfPageLabelStyle.Decimal)
            }, snap);
        }
    }

    [Fact]
    public void UnlabeledDoc_NoOp_NoPageLabelsCreated()
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(FourPageDoc()));
        PdfDocumentEditor edit = doc.Edit();

        edit.Pages.InsertBlank(1, 200, 200);
        edit.Pages.Move(0, 2);
        using (PdfDocument src = PdfDocument.Load(new MemoryStream(FourPageDoc())))
            edit.Pages.Import(src, 0, 0);
        edit.Pages.RemoveAt(2);

        Assert.Null(doc.CatalogDictionary!.Get(new PdfName("PageLabels")));
        Assert.Empty(edit.PageLabels.Ranges);
    }
}
