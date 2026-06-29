using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class OrphanWidgetPageIndexTests
{
    /// <summary>
    /// A widget that is not referenced by any page's /Annots (an orphan — e.g. left behind by a
    /// partial flatten) must report PageIndex == -1, per the documented PdfFieldWidget contract.
    /// Regression: PopulateWidgets used Dictionary.TryGetValue(out pageIndex), whose miss path
    /// silently overwrote the intended -1 with default(int) == 0, so orphans were reported as
    /// living on page 0 — which made geometry-driven renderers (WPF/Avalonia) paint them on page 1.
    /// </summary>
    [Fact]
    public void Widget_NotReferencedByAnyPageAnnots_ReportsPageIndexMinusOne()
    {
        byte[] pdf = FormTestDocs.WithTextField("name");
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdf));

        // Orphan the widget: empty page 0's /Annots while the field stays in /AcroForm /Fields,
        // so the field is still reachable through the tree but no page references its widget.
        PdfDictionary page = doc.GetPages()[0].Dictionary;
        PdfObject? annotsRaw = page.Get(new PdfName("Annots"));
        if (annotsRaw is PdfIndirectReference ir) annotsRaw = doc.GetObject(ir.ObjectNumber);
        Assert.IsType<PdfArray>(annotsRaw);
        ((PdfArray)annotsRaw!).Clear();

        PdfDocumentEditor editor = doc.Edit();
        PdfFieldWidget widget = editor.Forms["name"]!.Widgets[0];

        Assert.Equal(-1, widget.PageIndex);
    }
}
