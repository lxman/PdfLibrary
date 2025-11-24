using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Document;

/// <summary>
/// Represents a PDF page tree (ISO 32000-1:2008 section 7.7.3)
/// The page tree is a balanced tree structure containing all pages in the document
/// </summary>
public class PdfPageTree
{
    private readonly PdfDictionary _dictionary;
    private readonly PdfDocument? _document;
    private List<PdfPage>? _cachedPages;

    /// <summary>
    /// Creates a page tree from a dictionary
    /// </summary>
    public PdfPageTree(PdfDictionary dictionary, PdfDocument? document = null)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        _document = document;

        // Verify this is a page tree node
        if (_dictionary.TryGetValue(PdfName.TypeName, out PdfObject typeObj) && typeObj is PdfName typeName)
        {
            if (typeName.Value != "Pages")
                throw new ArgumentException($"Dictionary is not a Pages node (Type = {typeName.Value})");
        }
    }

    /// <summary>
    /// Gets the underlying dictionary
    /// </summary>
    public PdfDictionary Dictionary => _dictionary;

    /// <summary>
    /// Gets the total count of pages in this tree (required)
    /// </summary>
    public int Count
    {
        get
        {
            if (_dictionary.TryGetValue(new PdfName("Count"), out PdfObject obj) && obj is PdfInteger count)
                return count.Value;
            return 0;
        }
    }

    /// <summary>
    /// Gets all pages in the tree (flattened)
    /// </summary>
    public List<PdfPage> GetPages()
    {
        if (_cachedPages is not null)
            return _cachedPages;

        _cachedPages = [];
        CollectPages(_dictionary, _cachedPages);
        return _cachedPages;
    }

    /// <summary>
    /// Gets a specific page by index (0-based)
    /// </summary>
    public PdfPage? GetPage(int index)
    {
        List<PdfPage> pages = GetPages();
        if (index < 0 || index >= pages.Count)
            return null;

        return pages[index];
    }

    /// <summary>
    /// Recursively collects all pages from the page tree
    /// </summary>
    private void CollectPages(PdfDictionary node, List<PdfPage> pages)
    {
        // Get the Kids array
        if (!node.TryGetValue(new PdfName("Kids"), out PdfObject kidsObj))
            return;

        if (kidsObj is not PdfArray kidsArray)
            return;

        foreach (PdfObject kid in kidsArray)
        {
            PdfObject? kidObj = kid;

            // Resolve indirect reference
            if (kidObj is PdfIndirectReference reference && _document is not null)
            {
                kidObj = _document.ResolveReference(reference);
            }

            if (kidObj is not PdfDictionary kidDict)
                continue;

            // Check if this is a page or a page tree node
            if (!kidDict.TryGetValue(PdfName.TypeName, out PdfObject typeObj) || typeObj is not PdfName typeName)
                continue;

            if (typeName.Value == "Pages")
            {
                // Intermediate node - recurse
                CollectPages(kidDict, pages);
            }
            else if (typeName.Value == "Page")
            {
                // Leaf node - add page
                pages.Add(new PdfPage(kidDict, _document, node));
            }
        }
    }

    /// <summary>
    /// Gets inheritable resources from this node
    /// </summary>
    public PdfResources? GetResources()
    {
        if (!_dictionary.TryGetValue(new PdfName("Resources"), out PdfObject? obj))
            return null;

        if (obj is PdfIndirectReference reference && _document is not null)
            obj = _document.ResolveReference(reference);

        if (obj is not PdfDictionary resourceDict)
            return null;

        return new PdfResources(resourceDict, _document);
    }

    /// <summary>
    /// Gets inheritable MediaBox from this node
    /// </summary>
    public PdfArray? GetMediaBox()
    {
        if (_dictionary.TryGetValue(new PdfName("MediaBox"), out PdfObject obj) && obj is PdfArray array)
            return array;
        return null;
    }

    /// <summary>
    /// Gets inheritable CropBox from this node
    /// </summary>
    public PdfArray? GetCropBox()
    {
        if (_dictionary.TryGetValue(new PdfName("CropBox"), out PdfObject obj) && obj is PdfArray array)
            return array;
        return null;
    }

    /// <summary>
    /// Gets inheritable Rotate from this node
    /// </summary>
    public int? GetRotate()
    {
        if (_dictionary.TryGetValue(new PdfName("Rotate"), out PdfObject obj) && obj is PdfInteger rotate)
            return rotate.Value;
        return null;
    }
}
