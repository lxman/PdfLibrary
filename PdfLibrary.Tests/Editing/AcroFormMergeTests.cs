using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing;

public class AcroFormMergeTests
{
    private static PdfObject? Deref(PdfDocument doc, PdfObject? o) =>
        o is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : o;

    private static List<string> AcroFieldNames(PdfDocument doc)
    {
        var result = new List<string>();
        if (Deref(doc, doc.CatalogDictionary?.Get(new PdfName("AcroForm"))) is not PdfDictionary acro) return result;
        if (Deref(doc, acro.Get(new PdfName("Fields"))) is not PdfArray fields) return result;
        foreach (PdfObject f in fields)
            if (Deref(doc, f) is PdfDictionary field && field.Get(new PdfName("T")) is PdfString t)
                result.Add(t.Value);
        return result;
    }

    private static byte[] FormWith(string field) =>
        PdfDocumentBuilder.Create()
            .AddPage(p => p.AddTextField(field, 100, 700, 200, 30))
            .WithAcroForm(f => f.SetNeedAppearances(true))
            .ToByteArray();

    [Fact]
    public void Import_RegistersFormField_InTargetAcroForm()
    {
        byte[] plain = PdfDocumentBuilder.Create().AddPage(p => p.AddText("host", 100, 700)).ToByteArray();
        using PdfDocument target = PdfDocument.Load(new MemoryStream(plain));
        using PdfDocument source = PdfDocument.Load(new MemoryStream(FormWith("username")));

        PdfDocumentEditor edit = target.Edit();
        edit.Pages.Import(source, 0, 1);

        Assert.Contains("username", AcroFieldNames(target));
    }

    [Fact]
    public void Merge_CollidingFieldNames_AreQualified()
    {
        using PdfDocument a = PdfDocument.Load(new MemoryStream(FormWith("name")));
        using PdfDocument b = PdfDocument.Load(new MemoryStream(FormWith("name")));
        using PdfDocument merged = PdfDocumentEditor.Merge([a, b]);

        List<string> names = AcroFieldNames(merged);
        Assert.Contains("name", names);
        Assert.Contains("name#2", names);
    }
}
