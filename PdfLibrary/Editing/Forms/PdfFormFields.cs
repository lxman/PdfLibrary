using System.Collections;
using PdfLibrary.Structure;

namespace PdfLibrary.Editing.Forms;

/// <summary>
/// Collection facade over the AcroForm fields in a document.
/// Each access reads the live field tree so mutations are immediately visible.
/// </summary>
public sealed class PdfFormFields : IEnumerable<PdfFormField>
{
    private readonly PdfDocument _document;

    internal PdfFormFields(PdfDocument document)
    {
        _document = document;
    }

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

    /// <inheritdoc/>
    public IEnumerator<PdfFormField> GetEnumerator() =>
        FormFieldTree.Read(_document).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
