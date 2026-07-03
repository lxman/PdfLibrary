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
        FieldAuthor.RegenerateAuthored(_document, field);
        return field;
    }

    /// <summary>
    /// Creates an unchecked checkbox with on-state "Yes" and generated check-mark /AP states.
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfButtonField AddCheckbox(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = new PdfName("Off"),
            [new PdfName("AS")] = new PdfName("Off"),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);

        FieldAppearanceGenerator.EnsureButtonAppearance(_document, dict, "Yes", isRadio: false);
        FieldAppearanceGenerator.EnsurePrintable(dict);
        return (PdfButtonField)this[name]!;
    }

    /// <summary>
    /// Creates an unsigned signature-field placeholder (no /V; signing is out of scope).
    /// </summary>
    /// <exception cref="ArgumentException">Empty/dotted/duplicate name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfSignatureField AddSignatureField(int pageIndex, string name, PdfRect rect)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        PdfDictionary page = FieldAuthor.GetPageDict(_document, pageIndex);

        var dict = new PdfDictionary
        {
            [new PdfName("Type")] = new PdfName("Annot"),
            [new PdfName("Subtype")] = new PdfName("Widget"),
            [new PdfName("FT")] = new PdfName("Sig"),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("Rect")] = FieldAuthor.RectArray(rect)
        };
        PdfIndirectReference fieldRef = _document.RegisterObject(dict);
        FieldAuthor.AddToAnnots(_document, page, fieldRef);
        FieldAuthor.EnsureFieldsArray(_document).Add(fieldRef);
        FieldAppearanceGenerator.EnsurePrintable(dict);
        return (PdfSignatureField)this[name]!;
    }

    /// <summary>
    /// Creates a radio group: a parent /Btn field with one widget per option (options may sit on
    /// different pages). Created with nothing selected; set <see cref="PdfButtonField.SelectedOption"/>
    /// to choose one. Radio + NoToggleToOff flags are set (Acrobat's default posture).
    /// </summary>
    /// <exception cref="ArgumentException">Bad name; no options; empty/"Off"/duplicate on-state.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Bad page index in any placement.</exception>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form.</exception>
    public PdfButtonField AddRadioGroup(string name, IReadOnlyList<PdfRadioOptionPlacement> options)
    {
        GuardAuthoring();
        FieldAuthor.ValidateNewName(_document, name);
        if (options is null || options.Count == 0)
            throw new ArgumentException("A radio group needs at least one option placement.", nameof(options));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PdfRadioOptionPlacement o in options)
        {
            if (string.IsNullOrWhiteSpace(o.OnState) || o.OnState == "Off")
                throw new ArgumentException(
                    "Radio on-state names must be non-empty and must not be 'Off'.", nameof(options));
            if (!seen.Add(o.OnState))
                throw new ArgumentException($"Duplicate radio on-state '{o.OnState}'.", nameof(options));
            FieldAuthor.GetPageDict(_document, o.PageIndex); // validate every page index BEFORE mutating
        }

        int radioFf = (1 << (FieldFlags.Radio - 1)) | (1 << (FieldFlags.NoToggleToOff - 1));
        var kids = new PdfArray();
        var parent = new PdfDictionary
        {
            [new PdfName("FT")] = new PdfName("Btn"),
            [new PdfName("Ff")] = new PdfInteger(radioFf),
            [new PdfName("T")] = PdfString.FromText(name),
            [new PdfName("V")] = new PdfName("Off"),
            [new PdfName("Kids")] = kids
        };
        PdfIndirectReference parentRef = _document.RegisterObject(parent);

        foreach (PdfRadioOptionPlacement o in options)
        {
            var widget = new PdfDictionary
            {
                [new PdfName("Type")] = new PdfName("Annot"),
                [new PdfName("Subtype")] = new PdfName("Widget"),
                [new PdfName("Parent")] = parentRef,
                [new PdfName("AS")] = new PdfName("Off"),
                [new PdfName("Rect")] = FieldAuthor.RectArray(o.Rect)
            };
            PdfIndirectReference widgetRef = _document.RegisterObject(widget);
            FieldAuthor.AddToAnnots(_document, FieldAuthor.GetPageDict(_document, o.PageIndex), widgetRef);
            kids.Add(widgetRef);
            FieldAppearanceGenerator.EnsureButtonAppearance(_document, widget, o.OnState, isRadio: true);
            FieldAppearanceGenerator.EnsurePrintable(widget);
        }

        FieldAuthor.EnsureFieldsArray(_document).Add(parentRef);
        return (PdfButtonField)this[name]!;
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
