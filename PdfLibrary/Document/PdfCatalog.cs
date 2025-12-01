using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Represents the PDF document catalog (ISO 32000-1:2008 section 7.7.2)
/// The catalog is the root of the document's object hierarchy
/// </summary>
internal class PdfCatalog
{
    private readonly PdfDictionary _dictionary;
    private readonly PdfDocument? _document;

    /// <summary>
    /// Creates a catalog from a dictionary
    /// </summary>
    public PdfCatalog(PdfDictionary dictionary, PdfDocument? document = null)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _document = document;

        // Verify this is a catalog
        if (!_dictionary.TryGetValue(PdfName.TypeName, out PdfObject typeObj) ||
            typeObj is not PdfName typeName) return;
        if (typeName.Value != "Catalog")
            throw new ArgumentException($"Dictionary is not a Catalog (Type = {typeName.Value})");
    }

    /// <summary>
    /// Gets the underlying dictionary
    /// </summary>
    public PdfDictionary Dictionary => _dictionary;

    /// <summary>
    /// Gets the page tree root (required)
    /// </summary>
    public PdfPageTree? GetPageTree()
    {
        if (!_dictionary.TryGetValue(new PdfName("Pages"), out PdfObject? pagesObj))
            return null;

        // Resolve indirect reference if needed
        if (pagesObj is PdfIndirectReference reference && _document is not null)
        {
            pagesObj = _document.ResolveReference(reference);
        }

        return pagesObj is not PdfDictionary pagesDict
            ? null
            : new PdfPageTree(pagesDict, _document);
    }

    /// <summary>
    /// Gets the page layout
    /// </summary>
    public string? PageLayout
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("PageLayout"), out PdfObject obj) && obj is PdfName name)
                return name.Value;
            return null;
        }
    }

    /// <summary>
    /// Gets the page mode
    /// </summary>
    public string? PageMode
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("PageMode"), out PdfObject obj) && obj is PdfName name)
                return name.Value;
            return null;
        }
    }

    /// <summary>
    /// Gets the outlines (bookmarks) dictionary
    /// </summary>
    public PdfDictionary? GetOutlines()
    {
        if (!_dictionary.TryGetValue(new PdfName("Outlines"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the AcroForm dictionary (interactive forms)
    /// </summary>
    public PdfDictionary? GetAcroForm()
    {
        if (!_dictionary.TryGetValue(new PdfName("AcroForm"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }

    /// <summary>
    /// Gets the metadata stream
    /// </summary>
    public PdfStream? GetMetadata()
    {
        if (!_dictionary.TryGetValue(new PdfName("Metadata"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfStream;
    }

    /// <summary>
    /// Gets the document language
    /// </summary>
    public string? Language
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Lang"), out PdfObject obj) && obj is PdfString str)
                return str.ToString();
            return null;
        }
    }

    /// <summary>
    /// Gets the viewer preferences dictionary
    /// </summary>
    public PdfDictionary? GetViewerPreferences()
    {
        if (!_dictionary.TryGetValue(new PdfName("ViewerPreferences"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        return obj as PdfDictionary;
    }
}
