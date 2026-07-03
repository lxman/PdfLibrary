using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Dictionary-level construction shared by the PdfFormFields authoring methods: AcroForm
/// bootstrap, field/widget wiring into page /Annots and /AcroForm /Fields, and validation.
/// The builder's WriteFormField recipes are transcribed here for the parsed object model;
/// appearance generation stays in FieldAppearanceGenerator/ButtonStateWriter.
/// </summary>
internal static class FieldAuthor
{
    /// <summary>Returns the /AcroForm dictionary, creating and catalog-wiring it (with the
    /// Helvetica /DA default) when the document has none. Never sets /NeedAppearances —
    /// authoring always generates appearance streams itself. /DR is NOT written here: the
    /// first Regenerate call routes through AppearanceFontResolver, which synthesises the
    /// standard-14 font and registers it in /DR /Font (spec §2.2's /DR requirement is met
    /// by that existing self-healing path, not duplicated here).</summary>
    internal static PdfDictionary EnsureAcroForm(PdfDocument doc)
    {
        PdfDictionary catalog = doc.CatalogDictionary
            ?? throw new InvalidOperationException("Document has no catalog.");
        if (Resolve(doc, catalog.Get(new PdfName("AcroForm"))) is PdfDictionary existing)
            return existing;
        var acro = new PdfDictionary
        {
            [new PdfName("Fields")] = new PdfArray(),
            [new PdfName("DA")] = PdfString.FromText("/Helv 0 Tf 0 g")
        };
        catalog[new PdfName("AcroForm")] = doc.RegisterObject(acro);
        return acro;
    }

    internal static PdfArray EnsureFieldsArray(PdfDocument doc)
    {
        PdfDictionary acro = EnsureAcroForm(doc);
        if (Resolve(doc, acro.Get(new PdfName("Fields"))) is PdfArray fields) return fields;
        var created = new PdfArray();
        acro[new PdfName("Fields")] = created;
        return created;
    }

    /// <summary>Root-level partial-name validation: non-empty, no '.', unique against the live tree.</summary>
    internal static void ValidateNewName(PdfDocument doc, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Field name must be non-empty.", nameof(name));
        if (name.Contains('.'))
            throw new ArgumentException(
                "Field name must not contain '.' — the period separates hierarchy levels in full names.",
                nameof(name));
        if (FormFieldTree.Read(doc).Any(f => string.Equals(f.FullName, name, StringComparison.Ordinal)))
            throw new ArgumentException($"A field named '{name}' already exists.", nameof(name));
    }

    internal static PdfDictionary GetPageDict(PdfDocument doc, int pageIndex)
    {
        List<PdfPage> pages = doc.GetPages();
        if (pageIndex < 0 || pageIndex >= pages.Count)
            throw new ArgumentOutOfRangeException(nameof(pageIndex), pageIndex,
                $"Page index must be in [0, {pages.Count}).");
        return pages[pageIndex].Dictionary;
    }

    /// <summary>Normalized [minX minY maxX maxY] /Rect array.</summary>
    internal static PdfArray RectArray(PdfRect rect) => new()
    {
        new PdfReal(Math.Min(rect.Left, rect.Right)),
        new PdfReal(Math.Min(rect.Bottom, rect.Top)),
        new PdfReal(Math.Max(rect.Left, rect.Right)),
        new PdfReal(Math.Max(rect.Bottom, rect.Top))
    };

    /// <summary>Appends the widget to the page's /Annots (creating the array when absent).</summary>
    internal static void AddToAnnots(PdfDocument doc, PdfDictionary page, PdfIndirectReference widgetRef)
    {
        if (Resolve(doc, page.Get(new PdfName("Annots"))) is PdfArray annots)
        {
            annots.Add(widgetRef);
            return;
        }
        page[new PdfName("Annots")] = new PdfArray { widgetRef };
    }

    private static PdfObject? Resolve(PdfDocument doc, PdfObject? obj) =>
        obj is PdfIndirectReference r ? doc.GetObject(r.ObjectNumber) : obj;
}
