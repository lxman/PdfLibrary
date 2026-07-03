using PdfLibrary.Builder;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Field-authoring surface: create/remove AcroForm fields on an existing document
/// (design: Docs/specs/2026-07-03-forms-authoring-api-design.md). Geometry is PDF user
/// space, Y-up — the same convention as <see cref="PdfFieldWidget.Rect"/>.
/// </summary>
public sealed partial class PdfFormFields
{
    /// <summary>
    /// Creates a single-line text field on the given page. Bootstraps /AcroForm when the
    /// document has none. The returned field is live — set <see cref="PdfTextField.Value"/>
    /// to fill it immediately.
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfTextField AddTextField(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Tx"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = PdfString.FromText(string.Empty),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);

        var field = (PdfTextField)this[name]!;
        FieldAppearanceGenerator.Regenerate(_document, field);
        return field;
    }

    private void GuardAuthoring()
    {
        if (FormFlattener.IsDynamicXfa(_document))
            throw new InvalidOperationException(
                "Cannot author fields on a dynamic XFA form: its fields exist only in the XFA " +
                "template, so AcroForm widgets added here would never be shown by an XFA viewer. " +
                "Check Forms.IsDynamicXfa before offering form design.");
    }
}
