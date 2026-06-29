using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>
/// Flattening a complex, deeply-nested AcroForm (the IRS fillable W-2, producer "Designer 6.5":
/// 272 fields under hierarchical subforms topmostSubform[0].CopyX[0]...). The simple single-page
/// builder fixtures do not exercise nested field removal.
/// </summary>
public class FlattenHierarchicalTests
{
    private const string W2Path = @"C:\Users\jorda\RiderProjects\PDF\TestPDFs\fw2.pdf";

    private static int FormsCount(PdfDocument doc) => FormFieldTree.Read(doc).Count;

    private static PdfObject? Res(PdfDocument d, PdfObject? x) =>
        x is PdfIndirectReference r ? d.GetObject(r.ObjectNumber) : x;

    private static int WidgetAnnotCount(PdfDocument doc)
    {
        int total = 0;
        foreach (PdfPage p in doc.GetPages())
        {
            if (Res(doc, p.Dictionary.Get(new PdfName("Annots"))) is not PdfArray annots) continue;
            foreach (PdfObject e in annots)
                if (Res(doc, e) is PdfDictionary dd && dd.Get(new PdfName("Subtype")) is PdfName { Value: "Widget" })
                    total++;
        }
        return total;
    }

    private static int PageDoCount(PdfDocument doc, int pageIndex)
    {
        PdfDictionary page = doc.GetPages()[pageIndex].Dictionary;
        var sb = new StringBuilder();
        void App(PdfObject? c) { if (Res(doc, c) is PdfStream s) sb.Append(Encoding.Latin1.GetString(s.GetDecodedData())); }
        PdfObject? cs = page.Get(new PdfName("Contents"));
        if (Res(doc, cs) is PdfArray arr) foreach (PdfObject it in arr) App(it); else App(cs);
        string c = sb.ToString();
        int n = 0;
        for (int i = c.IndexOf(" Do", StringComparison.Ordinal); i >= 0; i = c.IndexOf(" Do", i + 3, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>
    /// RC1: RemoveFieldFromAcroForm only scanned the top-level /AcroForm /Fields array, so the W-2's
    /// nested terminal fields were never removed — Forms.Count stayed 272 after flatten. A full
    /// Flatten() must empty the field tree.
    /// </summary>
    [Fact]
    public void FlattenAll_RemovesEveryNestedField()
    {
        if (!File.Exists(W2Path)) return; // corpus-dependent; skip when absent (cross-platform)

        using PdfDocument doc = PdfDocument.Load(W2Path);
        Assert.Equal(272, FormsCount(doc)); // sanity: fixture is the expected form

        PdfDocumentEditor editor = doc.Edit();
        // Fill a couple of editable text fields, then flatten everything.
        if (editor.Forms.TryGet("topmostSubform[0].Copy1[0].Col_Left[0].f2_04[0]", out PdfFormField? f)
            && f is PdfTextField t)
            t.Value = "TEST123";

        editor.Forms.Flatten();

        Assert.Equal(0, FormsCount(doc));
    }

    /// <summary>
    /// RC-A + RC1 together: after fill → flatten → save → reopen, no widget may report PageIndex 0
    /// for a page that originally had no widgets. This is the corruption Focal saw — flatten-orphaned
    /// widgets collapsing to page 0 and being painted on the instructions page by geometry-driven
    /// renderers (WPF/Avalonia). After the fix there are no orphans (RC1) and any stray reports -1 (RC-A).
    /// </summary>
    [Fact]
    public void FlattenAll_NoWidgetCollapsesToPageZero()
    {
        if (!File.Exists(W2Path)) return;

        byte[] outBytes;
        using (PdfDocument doc = PdfDocument.Load(W2Path))
        {
            PdfDocumentEditor editor = doc.Edit();
            if (editor.Forms.TryGet("topmostSubform[0].Copy1[0].Col_Left[0].f2_04[0]", out PdfFormField? f)
                && f is PdfTextField t)
                t.Value = "TEST123";
            editor.Forms.Flatten();
            using var ms = new MemoryStream();
            editor.Save(ms);
            outBytes = ms.ToArray();
        }

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outBytes));
        int onPageZero = FormFieldTree.Read(reloaded)
            .SelectMany(field => field.Widgets)
            .Count(w => w.PageIndex == 0);
        Assert.Equal(0, onPageZero);
    }

    /// <summary>
    /// RC2: a full flatten must leave NO widget annotations behind. On the W-2, 252/272 widgets have
    /// no /AP (XFA form), and FlattenField's "no /AP" path used to `continue` without removing them —
    /// so after flatten the page /Annots still held hundreds of empty interactive boxes.
    /// </summary>
    [Fact]
    public void FlattenAll_RemovesEveryWidgetAnnotation()
    {
        if (!File.Exists(W2Path)) return;

        byte[] outBytes;
        using (PdfDocument doc = PdfDocument.Load(W2Path))
        {
            PdfDocumentEditor editor = doc.Edit();
            editor.Forms.Flatten();
            using var ms = new MemoryStream();
            editor.Save(ms);
            outBytes = ms.ToArray();
        }

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outBytes));
        Assert.Equal(0, WidgetAnnotCount(reloaded));
    }

    /// <summary>
    /// RC3: a filled value must be baked onto its own page as page content (a Do invocation of the
    /// generated appearance), never dropped. Guards against the "remove widget without painting" path.
    /// </summary>
    [Fact]
    public void FlattenAll_FilledValueBakesOnItsPage()
    {
        if (!File.Exists(W2Path)) return;

        const string field = "topmostSubform[0].Copy1[0].Col_Left[0].f2_04[0]";
        int pageIndex;
        int doBefore;
        byte[] outBytes;
        using (PdfDocument doc = PdfDocument.Load(W2Path))
        {
            PdfDocumentEditor editor = doc.Edit();
            pageIndex = editor.Forms[field]!.Widgets[0].PageIndex;
            doBefore = PageDoCount(doc, pageIndex);
            ((PdfTextField)editor.Forms[field]!).Value = "551122";
            editor.Forms.Flatten();
            using var ms = new MemoryStream();
            editor.Save(ms);
            outBytes = ms.ToArray();
        }

        using PdfDocument reloaded = PdfDocument.Load(new MemoryStream(outBytes));
        Assert.True(PageDoCount(reloaded, pageIndex) > doBefore,
            $"expected a baked appearance (Do) on page {pageIndex}");
        // And the form is fully gone.
        Assert.Equal(0, FormsCount(reloaded));
        Assert.Equal(0, WidgetAnnotCount(reloaded));
    }
}
