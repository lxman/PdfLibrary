using System.Collections;
using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF dictionary object (ISO 32000-1:2008 section 7.3.7)
/// Dictionaries are associative tables with name keys and any-type values
/// Written as << key value key value >> in PDF files
/// </summary>
internal sealed class PdfDictionary : PdfObject, IDictionary<PdfName, PdfObject>
{
    private readonly Dictionary<PdfName, PdfObject> _entries;

    public PdfDictionary()
    {
        _entries = new Dictionary<PdfName, PdfObject>();
    }

    public PdfDictionary(IDictionary<PdfName, PdfObject> entries)
    {
        _entries = new Dictionary<PdfName, PdfObject>(entries ?? throw new ArgumentNullException(nameof(entries)));
    }

    public override PdfObjectType Type => PdfObjectType.Dictionary;

    public override string ToPdfString()
    {
        var sb = new StringBuilder();
        sb.Append("<<");

        var first = true;
        foreach (KeyValuePair<PdfName, PdfObject> kvp in _entries)
        {
            // Skip null values as per spec (7.3.7)
            if (kvp.Value is PdfNull)
                continue;

            if (!first)
                sb.Append(' ');
            first = false;

            sb.Append(kvp.Key.ToPdfString());
            sb.Append(' ');
            sb.Append(kvp.Value.ToPdfString());
        }

        sb.Append(">>");
        return sb.ToString();
    }

    /// <summary>
    /// Gets a value from the dictionary by name, or null if not present
    /// </summary>
    public PdfObject? Get(PdfName key) =>
        _entries.GetValueOrDefault(key);

    /// <summary>
    /// Gets a value from the dictionary by string name, or null if not present
    /// </summary>
    public PdfObject? Get(string key) =>
        Get(new PdfName(key));

    /// <summary>
    /// Sets or removes a value in the dictionary
    /// Setting to null removes the entry as per PDF spec
    /// </summary>
    public void Set(PdfName key, PdfObject? value)
    {
        if (value is null or PdfNull)
            _entries.Remove(key);
        else
            _entries[key] = value;
    }

    /// <summary>
    /// Sets a value in the dictionary by string name
    /// </summary>
    public void Set(string key, PdfObject? value) =>
        Set(new PdfName(key), value);

    // IDictionary<PdfName, PdfObject> implementation
    public PdfObject this[PdfName key]
    {
        get => _entries[key];
        set => Set(key, value);
    }

    public ICollection<PdfName> Keys => _entries.Keys;
    public ICollection<PdfObject> Values => _entries.Values;
    public int Count => _entries.Count;
    public bool IsReadOnly => false;

    public void Add(PdfName key, PdfObject value) =>
        _entries.Add(key ?? throw new ArgumentNullException(nameof(key)),
                     value ?? throw new ArgumentNullException(nameof(value)));

    public void Add(KeyValuePair<PdfName, PdfObject> item) =>
        Add(item.Key, item.Value);

    public void Clear() => _entries.Clear();

    public bool Contains(KeyValuePair<PdfName, PdfObject> item) =>
        _entries.Contains(item);

    public bool ContainsKey(PdfName key) => _entries.ContainsKey(key);

    public void CopyTo(KeyValuePair<PdfName, PdfObject>[] array, int arrayIndex) =>
        ((IDictionary<PdfName, PdfObject>)_entries).CopyTo(array, arrayIndex);

    public IEnumerator<KeyValuePair<PdfName, PdfObject>> GetEnumerator() =>
        _entries.GetEnumerator();

    public bool Remove(PdfName key) => _entries.Remove(key);

    public bool Remove(KeyValuePair<PdfName, PdfObject> item) =>
        ((IDictionary<PdfName, PdfObject>)_entries).Remove(item);

    public bool TryGetValue(PdfName key, out PdfObject value) =>
        _entries.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
