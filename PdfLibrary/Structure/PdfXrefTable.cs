namespace PdfLibrary.Structure;

/// <summary>
/// Represents a cross-reference table (ISO 32000-1:2008 section 7.5.4)
/// Maps object numbers to byte offsets in the PDF file
/// </summary>
public class PdfXrefTable
{
    private readonly Dictionary<int, PdfXrefEntry> _entries = new();

    /// <summary>
    /// Adds an entry to the cross-reference table
    /// </summary>
    public void Add(PdfXrefEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries[entry.ObjectNumber] = entry;
    }

    /// <summary>
    /// Gets an entry by object number
    /// </summary>
    public PdfXrefEntry? GetEntry(int objectNumber)
    {
        return _entries.GetValueOrDefault(objectNumber);
    }

    /// <summary>
    /// Checks if an object number exists in the table
    /// </summary>
    public bool Contains(int objectNumber) => _entries.ContainsKey(objectNumber);

    /// <summary>
    /// Gets the byte offset for an object number (only for in-use objects)
    /// </summary>
    public long? GetByteOffset(int objectNumber)
    {
        PdfXrefEntry? entry = GetEntry(objectNumber);
        return entry?.IsInUse == true ? entry.ByteOffset : null;
    }

    /// <summary>
    /// Gets all entries in the table
    /// </summary>
    public IReadOnlyCollection<PdfXrefEntry> Entries => _entries.Values;

    /// <summary>
    /// Gets the count of entries in the table
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Clears all entries from the table
    /// </summary>
    public void Clear() => _entries.Clear();
}
