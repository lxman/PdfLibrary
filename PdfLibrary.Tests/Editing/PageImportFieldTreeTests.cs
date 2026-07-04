using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

/// <summary>
/// Importing a page must bring ONLY that page's form fields, not the source form's entire field
/// forest. On XFA-style forms (the IRS W-2: 272 fields, every one under a single
/// topmostSubform[0] root), the page cloner used to follow a widget's /Parent up to the shared
/// root and then /Kids back down into every field of every page — and each stray widget's /P
/// then dragged in orphan clones of the source pages themselves (importing 2 pages of fw2.pdf
/// grew a 99 KB host to ~1.4 MB and left 453 page-less fields).
/// </summary>
public class PageImportFieldTreeTests
{
    private const string W2Path = @"C:\Users\jorda\RiderProjects\PDF\TestPDFs\fw2.pdf";

    private static PdfObject? Res(PdfDocument d, PdfObject? x) =>
        x is PdfIndirectReference r ? d.GetObject(r.ObjectNumber) : x;

    private static int WidgetAnnotCount(PdfDocument doc, PdfDictionary page)
    {
        if (Res(doc, page.Get(new PdfName("Annots"))) is not PdfArray annots) return 0;
        var n = 0;
        foreach (PdfObject e in annots)
            if (Res(doc, e) is PdfDictionary d && d.Get(new PdfName("Subtype")) is PdfName { Value: "Widget" })
                n++;
        return n;
    }

    /// <summary>First source page carrying widget annotations, with its widget count.</summary>
    private static (int Index, int Widgets) FirstFormPage(PdfDocument source)
    {
        IReadOnlyList<PdfPage> pages = source.GetPages();
        for (var i = 0; i < pages.Count; i++)
        {
            int n = WidgetAnnotCount(source, pages[i].Dictionary);
            if (n > 0) return (i, n);
        }
        throw new InvalidOperationException("fixture has no form pages");
    }

    private static PdfDocument BlankHost()
    {
        byte[] bytes = PdfDocumentBuilder.Create().AddPage(p => p.AddText("host", 100, 700)).ToByteArray();
        return PdfDocument.Load(new MemoryStream(bytes));
    }

    [Fact]
    public void Import_W2Page_BringsOnlyThatPagesFields()
    {
        if (!File.Exists(W2Path)) return; // corpus-dependent; skip when absent

        using PdfDocument source = PdfDocument.Load(W2Path);
        (int srcIdx, int srcWidgets) = FirstFormPage(source);

        using PdfDocument target = BlankHost();
        target.Edit().Pages.Import(source, srcIdx, 1);

        List<PdfFormField> fields = target.Edit().Forms.ToList();
        Assert.NotEmpty(fields);
        List<PdfFieldWidget> widgets = fields.SelectMany(f => f.Widgets).ToList();

        // Exactly the imported page's widgets — no stray fields from the rest of the form.
        Assert.Equal(srcWidgets, widgets.Count);
        // Every widget resolves to the imported page; none is page-less.
        Assert.All(widgets, w => Assert.Equal(1, w.PageIndex));
        // The hierarchical ancestry (full names) survives the spine-only clone.
        Assert.Contains(fields, f => f.FullName.StartsWith("topmostSubform[0].", StringComparison.Ordinal));
    }

    [Fact]
    public void Import_W2Page_DoesNotCloneOrphanSourcePages()
    {
        if (!File.Exists(W2Path)) return;

        using PdfDocument source = PdfDocument.Load(W2Path);
        (int srcIdx, int _) = FirstFormPage(source);

        using PdfDocument target = BlankHost();
        target.Edit().Pages.Import(source, srcIdx, 1);
        _ = target.GetPages();   // force the live pages into the object cache

        int pageDicts = target.Objects.Values
            .Count(o => o is PdfDictionary d && d.Get(new PdfName("Type")) is PdfName { Value: "Page" });
        Assert.Equal(2, pageDicts);   // host page + imported page, no orphan source-page clones
    }

    [Fact]
    public void Import_TwoW2Pages_EachBringsItsOwnSubtree_WithQualifiedRoots()
    {
        if (!File.Exists(W2Path)) return;

        using PdfDocument source = PdfDocument.Load(W2Path);
        IReadOnlyList<PdfPage> srcPages = source.GetPages();
        var formPages = new List<(int Index, int Widgets)>();
        for (var i = 0; i < srcPages.Count && formPages.Count < 2; i++)
        {
            int n = WidgetAnnotCount(source, srcPages[i].Dictionary);
            if (n > 0) formPages.Add((i, n));
        }
        Assert.Equal(2, formPages.Count);

        using PdfDocument target = BlankHost();
        PdfDocumentEditor edit = target.Edit();
        edit.Pages.Import(source, formPages[0].Index, 1);
        edit.Pages.Import(source, formPages[1].Index, 2);

        List<PdfFieldWidget> widgets = target.Edit().Forms.SelectMany(f => f.Widgets).ToList();
        Assert.Equal(formPages[0].Widgets + formPages[1].Widgets, widgets.Count);
        Assert.Equal(formPages[0].Widgets, widgets.Count(w => w.PageIndex == 1));
        Assert.Equal(formPages[1].Widgets, widgets.Count(w => w.PageIndex == 2));
        Assert.DoesNotContain(widgets, w => w.PageIndex < 0);
    }
}
