using System.Text;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Editing;
using PdfLibrary.Editing.Forms;
using PdfLibrary.Structure;
using Xunit;

namespace PdfLibrary.Tests.Editing.Forms;

public class FlattenTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes all /Contents streams of the first page from raw PDF bytes and concatenates them.
    /// </summary>
    private static string DecodePageContents(byte[] pdfBytes)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdfBytes));
        var pages = doc.GetPages();
        Assert.NotEmpty(pages);
        PdfDictionary page = pages[0].Dictionary;

        var sb = new StringBuilder();
        PdfLibrary.Core.PdfObject? contentsRaw = page.Get(new PdfName("Contents"));

        void AppendStream(PdfLibrary.Core.PdfObject? obj)
        {
            if (obj is PdfIndirectReference ir)
                obj = doc.GetObject(ir.ObjectNumber);
            if (obj is PdfStream s)
                sb.Append(Encoding.Latin1.GetString(s.GetDecodedData()));
        }

        if (contentsRaw is PdfArray arr)
        {
            foreach (PdfLibrary.Core.PdfObject item in arr)
                AppendStream(item);
        }
        else
        {
            AppendStream(contentsRaw);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Returns true if the catalog of the given bytes has an /AcroForm entry with a non-empty /Fields.
    /// </summary>
    private static bool HasAcroFormWithFields(byte[] pdfBytes)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdfBytes));
        PdfDictionary? catalog = doc.CatalogDictionary;
        if (catalog is null) return false;

        PdfLibrary.Core.PdfObject? acroRaw = catalog.Get(new PdfName("AcroForm"));
        if (acroRaw is PdfIndirectReference acroRef)
            acroRaw = doc.GetObject(acroRef.ObjectNumber);
        if (acroRaw is not PdfDictionary acro) return false;

        PdfLibrary.Core.PdfObject? fieldsRaw = acro.Get(new PdfName("Fields"));
        if (fieldsRaw is PdfIndirectReference fieldsRef)
            fieldsRaw = doc.GetObject(fieldsRef.ObjectNumber);
        if (fieldsRaw is not PdfArray fields) return false;

        return fields.Count > 0;
    }

    /// <summary>
    /// Counts Widget annotations on the first page of the given bytes whose field name matches.
    /// </summary>
    private static int CountWidgetsForField(byte[] pdfBytes, string fieldName)
    {
        using PdfDocument doc = PdfDocument.Load(new MemoryStream(pdfBytes));
        var pages = doc.GetPages();
        if (pages.Count == 0) return 0;
        PdfDictionary page = pages[0].Dictionary;

        PdfLibrary.Core.PdfObject? annotsRaw = page.Get(new PdfName("Annots"));
        if (annotsRaw is PdfIndirectReference ar)
            annotsRaw = doc.GetObject(ar.ObjectNumber);
        if (annotsRaw is not PdfArray annots) return 0;

        int count = 0;
        foreach (PdfLibrary.Core.PdfObject entry in annots)
        {
            PdfLibrary.Core.PdfObject? resolved = entry is PdfIndirectReference ir2
                ? doc.GetObject(ir2.ObjectNumber)
                : entry;
            if (resolved is not PdfDictionary dict) continue;

            // Check Subtype == Widget
            PdfLibrary.Core.PdfObject? subtype = dict.Get(new PdfName("Subtype"));
            if (subtype is not PdfName { Value: "Widget" }) continue;

            // Check field name
            string? t = GetFieldNameFromWidget(doc, dict);
            if (string.Equals(t, fieldName, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    private static string? GetFieldNameFromWidget(PdfDocument doc, PdfDictionary widget)
    {
        PdfDictionary? current = widget;
        int guard = 0;
        while (current is not null && guard++ < 64)
        {
            PdfLibrary.Core.PdfObject? tRaw = current.Get(new PdfName("T"));
            if (tRaw is PdfString ts) return ts.Value;
            if (tRaw is PdfName tn) return tn.Value;

            PdfLibrary.Core.PdfObject? parentRaw = current.Get(new PdfName("Parent"));
            if (parentRaw is PdfIndirectReference pr)
                parentRaw = doc.GetObject(pr.ObjectNumber);
            current = parentRaw as PdfDictionary;
        }
        return null;
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FlattenAll_RemovesWidgetFromAnnots()
    {
        byte[] originalPdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(originalPdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "Hello";
                edit.Forms.Flatten();
                edit.Save(outPath);
            }

            byte[] flatBytes = File.ReadAllBytes(outPath);
            int widgetCount = CountWidgetsForField(flatBytes, "name");
            Assert.Equal(0, widgetCount);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void FlattenAll_RemovesAcroFormOrEmptiesFields()
    {
        byte[] originalPdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(originalPdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "Hello";
                edit.Forms.Flatten();
                edit.Save(outPath);
            }

            byte[] flatBytes = File.ReadAllBytes(outPath);
            bool hasActiveFields = HasAcroFormWithFields(flatBytes);
            Assert.False(hasActiveFields,
                "Expected /AcroForm to be removed or have empty /Fields after Flatten");
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void FlattenAll_FieldNotFoundViaFormsIndexer()
    {
        byte[] originalPdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(originalPdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "Hello";
                edit.Forms.Flatten();
                edit.Save(outPath);
            }

            using PdfDocument reloaded = PdfDocument.Load(outPath);
            PdfDocumentEditor reEdit = reloaded.Edit();
            PdfFormField? field = reEdit.Forms["name"];
            Assert.Null(field);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void FlattenAll_BakesDoIntoPageContent()
    {
        byte[] originalPdf = FormTestDocs.WithTextField("name");
        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(originalPdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["name"]!).Value = "Hello";
                edit.Forms.Flatten();
                edit.Save(outPath);
            }

            byte[] flatBytes = File.ReadAllBytes(outPath);
            string contents = DecodePageContents(flatBytes);
            Assert.Contains("Do", contents);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void FlattenByName_OnlyRemovesNamedField()
    {
        byte[] originalPdf;
        {
            var builder = PdfLibrary.Builder.PdfDocumentBuilder.Create()
                .WithAcroForm(f => f.SetNeedAppearances(true))
                .AddPage(p =>
                {
                    p.AddTextField("field1", 72, 700, 200, 20);
                    p.AddTextField("field2", 72, 660, 200, 20);
                });
            originalPdf = builder.ToByteArray();
        }

        string outPath = Path.GetTempFileName();
        try
        {
            using (PdfDocument doc = PdfDocument.Load(new MemoryStream(originalPdf)))
            {
                PdfDocumentEditor edit = doc.Edit();
                ((PdfTextField)edit.Forms["field1"]!).Value = "One";
                ((PdfTextField)edit.Forms["field2"]!).Value = "Two";
                edit.Forms.Flatten("field1");
                edit.Save(outPath);
            }

            byte[] flatBytes = File.ReadAllBytes(outPath);

            Assert.Equal(0, CountWidgetsForField(flatBytes, "field1"));
            Assert.Equal(1, CountWidgetsForField(flatBytes, "field2"));
            Assert.True(HasAcroFormWithFields(flatBytes));
        }
        finally
        {
            File.Delete(outPath);
        }
    }
}
