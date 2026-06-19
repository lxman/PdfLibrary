using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Structure;

public partial class PdfDocument
{
    private int _nextObjectNumber = -1;

    /// <summary>The document catalog dictionary (resolves /Root), or null.</summary>
    internal PdfDictionary? CatalogDictionary =>
        Trailer.Root is { } root ? GetObject(root.ObjectNumber) as PdfDictionary : null;

    /// <summary>The root /Pages node dictionary, or null.</summary>
    internal PdfDictionary? PageTreeRootDictionary
    {
        get
        {
            PdfDictionary? catalog = CatalogDictionary;
            if (catalog is null) return null;
            if (!catalog.TryGetValue(new PdfName("Pages"), out PdfObject pagesObj)) return null;
            if (pagesObj is PdfIndirectReference reference) pagesObj = GetObject(reference.ObjectNumber)!;
            return pagesObj as PdfDictionary;
        }
    }

    /// <summary>Creates a minimal valid in-memory document: a catalog + empty page tree.</summary>
    internal static PdfDocument CreateEmpty()
    {
        var doc = new PdfDocument();

        var pages = new PdfDictionary();
        pages[PdfName.TypeName] = new PdfName("Pages");
        pages[new PdfName("Kids")] = new PdfArray();
        pages[new PdfName("Count")] = new PdfInteger(0);
        doc.AddObject(1, 0, pages);

        var catalog = new PdfDictionary();
        catalog[PdfName.TypeName] = new PdfName("Catalog");
        catalog[new PdfName("Pages")] = new PdfIndirectReference(1, 0);
        doc.AddObject(2, 0, catalog);

        doc.Trailer.Root = new PdfIndirectReference(2, 0);
        return doc;
    }

    /// <summary>Allocates a fresh, unused object number (monotonic).</summary>
    internal int AllocateObjectNumber()
    {
        if (_nextObjectNumber < 0)
        {
            int maxObjects = _objects.Count == 0 ? 0 : _objects.Keys.Max();
            int maxXref = XrefTable.Entries.Count == 0 ? 0 : XrefTable.Entries.Max(e => e.ObjectNumber);
            _nextObjectNumber = Math.Max(Math.Max(maxObjects, maxXref), 0) + 1;
        }
        return _nextObjectNumber++;
    }

    /// <summary>Allocates a number, stores <paramref name="obj"/> as a new indirect object, returns its reference.</summary>
    internal PdfIndirectReference RegisterObject(PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        int number = AllocateObjectNumber();
        AddObject(number, 0, obj);
        return new PdfIndirectReference(number, 0);
    }

    /// <summary>Overwrites the object stored at <paramref name="number"/>.</summary>
    internal void ReplaceObject(int number, PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        AddObject(number, 0, obj);
    }

    /// <summary>Removes the object at <paramref name="number"/> from the in-memory graph.</summary>
    internal void RemoveObject(int number) => _objects.Remove(number);
}
