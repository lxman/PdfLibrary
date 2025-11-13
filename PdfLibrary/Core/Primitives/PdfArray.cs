using System.Collections;
using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF array object (ISO 32000-1:2008 section 7.3.6)
/// Arrays are one-dimensional heterogeneous collections enclosed in square brackets
/// </summary>
public sealed class PdfArray : PdfObject, IList<PdfObject>
{
    private readonly List<PdfObject> _items;

    public PdfArray()
    {
        _items = [];
    }

    public PdfArray(IEnumerable<PdfObject> items)
    {
        _items = new List<PdfObject>(items ?? throw new ArgumentNullException(nameof(items)));
    }

    public PdfArray(params PdfObject[] items)
    {
        _items = new List<PdfObject>(items ?? throw new ArgumentNullException(nameof(items)));
    }

    public override PdfObjectType Type => PdfObjectType.Array;

    public override string ToPdfString()
    {
        var sb = new StringBuilder();
        sb.Append('[');

        for (var i = 0; i < _items.Count; i++)
        {
            if (i > 0)
                sb.Append(' ');

            sb.Append(_items[i].ToPdfString());
        }

        sb.Append(']');
        return sb.ToString();
    }

    // IList<PdfObject> implementation
    public PdfObject this[int index]
    {
        get => _items[index];
        set => _items[index] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Count => _items.Count;
    public bool IsReadOnly => false;

    public void Add(PdfObject item) => _items.Add(item ?? throw new ArgumentNullException(nameof(item)));
    public void Clear() => _items.Clear();
    public bool Contains(PdfObject item) => _items.Contains(item);
    public void CopyTo(PdfObject[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    public IEnumerator<PdfObject> GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(PdfObject item) => _items.IndexOf(item);
    public void Insert(int index, PdfObject item) => _items.Insert(index, item ?? throw new ArgumentNullException(nameof(item)));
    public bool Remove(PdfObject item) => _items.Remove(item);
    public void RemoveAt(int index) => _items.RemoveAt(index);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
