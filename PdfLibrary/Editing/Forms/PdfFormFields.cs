using System.Collections;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Collection facade over the AcroForm fields in a document.
/// Each access reads the live field tree so mutations are immediately visible.
/// </summary>
public sealed class PdfFormFields : IReadOnlyCollection<PdfFormField>
{
    private readonly PdfDocument _document;

    internal PdfFormFields(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>The number of form fields in the document. Reads the live field tree.</summary>
    public int Count => FormFieldTree.Read(_document).Count();

    /// <summary>
    /// Returns the field with the given fully-qualified name, or null if not found.
    /// </summary>
    public PdfFormField? this[string fullName]
    {
        get
        {
            TryGet(fullName, out PdfFormField? field);
            return field;
        }
    }

    /// <summary>
    /// Tries to get the field with the given fully-qualified name.
    /// </summary>
    public bool TryGet(string fullName, out PdfFormField? field)
    {
        foreach (PdfFormField f in FormFieldTree.Read(_document))
        {
            if (string.Equals(f.FullName, fullName, StringComparison.Ordinal))
            {
                field = f;
                return true;
            }
        }
        field = null;
        return false;
    }

    /// <summary>
    /// True when the document is a <i>dynamic</i> XFA form whose fields live only in the XFA template
    /// (no bakeable AcroForm widgets). Such a form cannot be rendered or flattened by PdfLibrary;
    /// callers should check this before offering edit/flatten and tell the user it is unsupported.
    /// Hybrid forms (XFA + a real AcroForm, e.g. the IRS W-2) return false.
    /// </summary>
    public bool IsDynamicXfa => FormFlattener.IsDynamicXfa(_document);

    /// <summary>
    /// Flattens all fields: bakes each field's normal appearance into the page content and
    /// removes all form interactivity (/AcroForm — including any /XFA — is dropped when no fields
    /// remain).
    /// </summary>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form
    /// (<see cref="IsDynamicXfa"/>): there is no AcroForm appearance to bake, and dropping /XFA would
    /// destroy the form.</exception>
    public void Flatten()
    {
        GuardNotDynamicXfa();
        FormFlattener.FlattenAll(_document);
    }

    /// <summary>
    /// Flattens the field with the given fully-qualified name.
    /// Throws <see cref="KeyNotFoundException"/> if the field is not found.
    /// Removes /AcroForm from the catalog if no fields remain after flattening.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document is a dynamic XFA form
    /// (<see cref="IsDynamicXfa"/>).</exception>
    public void Flatten(string fullName)
    {
        GuardNotDynamicXfa();
        PdfFormField? field = this[fullName];
        if (field is null)
            throw new KeyNotFoundException($"Form field '{fullName}' not found.");
        FormFlattener.FlattenField(_document, field);
        FormFlattener.PruneAcroFormIfEmpty(_document);
    }

    private void GuardNotDynamicXfa()
    {
        if (FormFlattener.IsDynamicXfa(_document))
            throw new InvalidOperationException(
                "Cannot flatten a dynamic XFA form: its fields exist only in the XFA template, which " +
                "PdfLibrary does not render, so there is no appearance to bake. Dropping /XFA would " +
                "leave only the placeholder shell. Check Forms.IsDynamicXfa before flattening.");
    }

    /// <inheritdoc/>
    public IEnumerator<PdfFormField> GetEnumerator() =>
        FormFieldTree.Read(_document).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
