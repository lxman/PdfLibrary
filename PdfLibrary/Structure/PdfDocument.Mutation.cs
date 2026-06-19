using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Structure;

public partial class PdfDocument
{
    private int _nextObjectNumber = -1;

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
