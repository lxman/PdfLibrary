using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class XfaFlattenGateTests
{
    private const string W2Path = @"C:\Users\jorda\RiderProjects\PDF\TestPDFs\fw2.pdf";

    private static PdfObject? Res(PdfDocument d, PdfObject? x) =>
        x is PdfIndirectReference r ? d.GetObject(r.ObjectNumber) : x;

    private static bool HasXfa(PdfDocument doc) =>
        Res(doc, doc.CatalogDictionary?.Get(new PdfName("AcroForm"))) is PdfDictionary acro
        && acro.Get(new PdfName("XFA")) is not null;

    /// <summary>A dynamic XFA shell: /AcroForm carries /XFA but has no positioned widgets.</summary>
    private static PdfDocument BuildDynamicXfaShell()
    {
        byte[] simple = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        var doc = PdfDocument.Load(new MemoryStream(simple));
        var acro = new PdfDictionary();
        acro[new PdfName("Fields")] = new PdfArray();
        acro[new PdfName("XFA")] = PdfString.FromText(
            "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>");
        doc.CatalogDictionary![new PdfName("AcroForm")] = acro;
        return doc;
    }

    // ── Dynamic XFA: detect + refuse + preserve ─────────────────────────────────

    [Fact]
    public void DynamicXfa_IsDetected()
    {
        using PdfDocument doc = BuildDynamicXfaShell();
        PdfDocumentEditor editor = doc.Edit();
        Assert.True(editor.Forms.IsDynamicXfa);
    }

    [Fact]
    public void DynamicXfa_Flatten_Throws_AndPreservesXfa()
    {
        using PdfDocument doc = BuildDynamicXfaShell();
        PdfDocumentEditor editor = doc.Edit();

        Assert.Throws<InvalidOperationException>(() => editor.Forms.Flatten());
        // The /XFA (the only representation of the form) must be left intact, not stripped.
        Assert.True(HasXfa(doc), "dynamic XFA form must be preserved after a refused flatten");
    }

    // ── Hybrid XFA (W-2): not dynamic, flatten strips /XFA ───────────────────────

    [Fact]
    public void HybridXfa_IsNotDynamic()
    {
        if (!File.Exists(W2Path)) return;
        using PdfDocument doc = PdfDocument.Load(W2Path);
        Assert.True(HasXfa(doc));                         // fixture really is an XFA form
        Assert.False(doc.Edit().Forms.IsDynamicXfa);      // but it has a bakeable AcroForm
    }

    [Fact]
    public void HybridXfa_FullFlatten_DropsXfa()
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
        Assert.False(HasXfa(reloaded), "a fully flattened hybrid form must no longer carry /XFA");
    }
}
