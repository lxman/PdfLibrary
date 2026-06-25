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
    /// Flattens all fields: bakes each field's normal appearance into the page content and
    /// removes all form interactivity (/AcroForm is dropped when no fields remain).
    /// </summary>
    public void Flatten() => FormFlattener.FlattenAll(_document);

    /// <summary>
    /// Flattens the field with the given fully-qualified name.
    /// Throws <see cref="KeyNotFoundException"/> if the field is not found.
    /// Removes /AcroForm from the catalog if no fields remain after flattening.
    /// </summary>
    public void Flatten(string fullName)
    {
        PdfFormField? field = this[fullName];
        if (field is null)
            throw new KeyNotFoundException($"Form field '{fullName}' not found.");
        FormFlattener.FlattenField(_document, field);
        FormFlattener.PruneAcroFormIfEmpty(_document);
    }

    /// <inheritdoc/>
    public IEnumerator<PdfFormField> GetEnumerator() =>
        FormFieldTree.Read(_document).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
