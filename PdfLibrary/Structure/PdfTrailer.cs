using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Structure;

/// <summary>
/// Represents the trailer dictionary of a PDF file (ISO 32000-1:2008 section 7.5.5)
/// The trailer provides information about how to read the cross-reference table and special objects
/// </summary>
public class PdfTrailer
{
    private readonly PdfDictionary _dictionary;

    /// <summary>
    /// Creates a trailer with the specified dictionary
    /// </summary>
    public PdfTrailer(PdfDictionary dictionary)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    /// <summary>
    /// Creates an empty trailer
    /// </summary>
    public PdfTrailer() : this(new PdfDictionary())
    {
    }

    /// <summary>
    /// Gets the underlying dictionary
    /// </summary>
    public PdfDictionary Dictionary => _dictionary;

    /// <summary>
    /// Gets or sets the Size entry - total number of entries in the cross-reference table
    /// </summary>
    public int? Size
    {
        get => _dictionary.TryGetValue(new PdfName("Size"), out PdfObject obj) && obj is PdfInteger integer
            ? integer.Value
            : null;
        set => _dictionary[new PdfName("Size")] = value.HasValue ? new PdfInteger(value.Value) : PdfNull.Instance;
    }

    /// <summary>
    /// Gets or sets the Prev entry - byte offset of the previous cross-reference section
    /// </summary>
    public long? Prev
    {
        get => _dictionary.TryGetValue(new PdfName("Prev"), out PdfObject obj) && obj is PdfInteger integer
            ? integer.Value
            : null;
        set => _dictionary[new PdfName("Prev")] = value.HasValue ? new PdfInteger((int)value.Value) : PdfNull.Instance;
    }

    /// <summary>
    /// Gets or sets the Root entry - reference to catalog dictionary
    /// </summary>
    public PdfIndirectReference? Root
    {
        get => _dictionary.TryGetValue(new PdfName("Root"), out PdfObject obj) && obj is PdfIndirectReference reference
            ? reference
            : null;
        set => _dictionary[new PdfName("Root")] = value is not null ? value : PdfNull.Instance;
    }

    /// <summary>
    /// Gets or sets the Encrypt entry - reference to encryption dictionary
    /// </summary>
    public PdfIndirectReference? Encrypt
    {
        get => _dictionary.TryGetValue(new PdfName("Encrypt"), out PdfObject obj) && obj is PdfIndirectReference reference
            ? reference
            : null;
        set => _dictionary[new PdfName("Encrypt")] = value is not null ? value : PdfNull.Instance;
    }

    /// <summary>
    /// Gets or sets the Info entry - reference to document information dictionary
    /// </summary>
    public PdfIndirectReference? Info
    {
        get => _dictionary.TryGetValue(new PdfName("Info"), out PdfObject obj) && obj is PdfIndirectReference reference
            ? reference
            : null;
        set => _dictionary[new PdfName("Info")] = value is not null ? value : PdfNull.Instance;
    }

    /// <summary>
    /// Gets or sets the ID entry - array of two byte strings
    /// </summary>
    public PdfArray? Id
    {
        get => _dictionary.TryGetValue(new PdfName("ID"), out PdfObject obj) && obj is PdfArray array
            ? array
            : null;
        set => _dictionary[new PdfName("ID")] = value is not null ? value : PdfNull.Instance;
    }

    public override string ToString() => _dictionary.ToPdfString();
}
