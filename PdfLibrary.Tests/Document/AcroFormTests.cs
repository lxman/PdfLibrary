using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Tests.Document;

/// <summary>
/// Tests for <see cref="PdfDocument.GetAcroForm"/> / <see cref="PdfDocument.HasForm"/> — the public
/// read-only view of a document's interactive form (ISO 32000-1, 12.7): the <c>/AcroForm</c> entry of the
/// catalog, and a cheap existence check (top-level <c>/Fields</c> array count, not a terminal-field walk).
/// </summary>
public class AcroFormTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>An in-memory document whose catalog <c>/AcroForm</c> declares a <c>/Fields</c> array of
    /// <paramref name="topLevelFieldCount"/> placeholder field dictionaries.</summary>
    private static PdfDocument DocWithAcroForm(int topLevelFieldCount)
    {
        var fields = new PdfArray();
        for (var i = 0; i < topLevelFieldCount; i++)
        {
            fields.Add(new PdfDictionary
            {
                [new PdfName("T")] = new PdfString(System.Text.Encoding.ASCII.GetBytes($"Field{i}")),
                [new PdfName("FT")] = new PdfName("Tx"),
            });
        }

        var acroForm = new PdfDictionary
        {
            [new PdfName("Fields")] = fields,
        };
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("AcroForm")] = acroForm,
        };
        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>An in-memory document whose catalog <c>/AcroForm</c> points at a <c>/Fields</c> array held
    /// in an indirect object (the common real-world shape). <paramref name="fieldCount"/> placeholder
    /// fields live in that indirect array.</summary>
    private static PdfDocument DocWithIndirectFieldsArray(int fieldCount)
    {
        var fields = new PdfArray();
        for (var i = 0; i < fieldCount; i++)
            fields.Add(new PdfDictionary { [new PdfName("T")] = new PdfString(System.Text.Encoding.ASCII.GetBytes($"F{i}")) });

        var acroForm = new PdfDictionary
        {
            [new PdfName("Fields")] = new PdfIndirectReference(2, 0),
        };
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("AcroForm")] = acroForm,
        };

        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.AddObject(2, 0, fields);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>An in-memory document whose catalog has an <c>/AcroForm</c> dictionary but no
    /// <c>/Fields</c> key (a malformed/empty form).</summary>
    private static PdfDocument DocWithAcroFormButNoFields()
    {
        var catalog = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Catalog"),
            [new PdfName("AcroForm")] = new PdfDictionary(),
        };
        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    /// <summary>An in-memory document with a catalog but no <c>/AcroForm</c> key at all (a plain PDF).</summary>
    private static PdfDocument DocWithoutAcroForm()
    {
        var catalog = new PdfDictionary { [new PdfName("Type")] = new PdfName("Catalog") };
        var doc = new PdfDocument();
        doc.AddObject(1, 0, catalog);
        doc.Trailer.Dictionary[new PdfName("Root")] = new PdfIndirectReference(1, 0);
        return doc;
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void NoAcroFormKey_HasFormFalse_GetAcroFormNull()
    {
        PdfDocument doc = DocWithoutAcroForm();
        Assert.False(doc.HasForm);
        Assert.Null(doc.GetAcroForm());
    }

    [Fact]
    public void AcroFormWithoutFields_HasFormFalse_CountZero()
    {
        PdfDocument doc = DocWithAcroFormButNoFields();
        // /AcroForm exists, but it declares no fields → not a usable form.
        AcroFormInfo? form = doc.GetAcroForm();
        Assert.NotNull(form);
        Assert.Equal(0, form!.TopLevelFieldCount);
        Assert.False(form.HasFields);
        Assert.False(doc.HasForm);
    }

    [Fact]
    public void InlineFields_HasFormTrue_CountMatches()
    {
        PdfDocument doc = DocWithAcroForm(topLevelFieldCount: 3);
        Assert.True(doc.HasForm);
        AcroFormInfo? form = doc.GetAcroForm();
        Assert.NotNull(form);
        Assert.Equal(3, form!.TopLevelFieldCount);
        Assert.True(form.HasFields);
    }

    [Fact]
    public void IndirectFieldsArray_isResolved()
    {
        // The common real-world shape: /Fields is an indirect reference, not inline.
        PdfDocument doc = DocWithIndirectFieldsArray(fieldCount: 2);
        Assert.True(doc.HasForm);
        AcroFormInfo? form = doc.GetAcroForm();
        Assert.NotNull(form);
        Assert.Equal(2, form!.TopLevelFieldCount);
    }

    [Fact]
    public void SingleField_HasFormTrue()
    {
        PdfDocument doc = DocWithAcroForm(topLevelFieldCount: 1);
        Assert.True(doc.HasForm);
        Assert.Equal(1, doc.GetAcroForm()!.TopLevelFieldCount);
    }
}
