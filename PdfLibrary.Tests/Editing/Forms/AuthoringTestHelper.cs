using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Editing.Forms;

/// <summary>Shared fixtures for the forms-authoring test files.</summary>
public static class AuthoringTestHelper
{
    /// <summary>A one-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainSinglePage() =>
        PdfDocumentEditor.Open(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray()));

    /// <summary>A two-page document with NO /AcroForm.</summary>
    public static PdfDocumentEditor OpenPlainTwoPages() =>
        PdfDocumentEditor.Open(new MemoryStream(
            PdfDocumentBuilder.Create().AddPage(_ => { }).AddPage(_ => { }).ToByteArray()));

    /// <summary>Saves and reopens through real bytes; disposes the input editor.</summary>
    public static PdfDocumentEditor SaveAndReopen(PdfDocumentEditor editor)
    {
        using var ms = new MemoryStream();
        editor.Save(ms);
        editor.Dispose();
        return PdfDocumentEditor.Open(new MemoryStream(ms.ToArray()));
    }

    /// <summary>A dynamic-XFA shell (AcroForm with /XFA, no widgets), opened for editing.
    /// Mirrors XfaFlattenGateTests.BuildDynamicXfaShell.</summary>
    public static PdfDocumentEditor OpenDynamicXfaShell()
    {
        byte[] simple = PdfDocumentBuilder.Create().AddPage(_ => { }).ToByteArray();
        var doc = PdfDocument.Load(new MemoryStream(simple));
        var acro = new PdfDictionary();
        acro[new PdfName("Fields")] = new PdfArray();
        acro[new PdfName("XFA")] = PdfString.FromText(
            "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>");
        doc.CatalogDictionary![new PdfName("AcroForm")] = acro;
        return doc.Edit();
    }
}
