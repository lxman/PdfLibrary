namespace PdfLibrary.Structure;

/// <summary>
/// Cross-reference entry types (ISO 32000-1 section 7.5.8.3)
/// </summary>
public enum PdfXrefEntryType
{
    /// <summary>Free entry</summary>
    Free = 0,
    /// <summary>Uncompressed object at byte offset</summary>
    Uncompressed = 1,
    /// <summary>Compressed object in object stream</summary>
    Compressed = 2
}

/// <summary>
/// Represents a single entry in the cross-reference table (ISO 32000-1:2008 section 7.5.4)
/// </summary>
public class PdfXrefEntry
{
    /// <summary>
    /// Creates a cross-reference entry
    /// </summary>
    /// <param name="objectNumber">Object number</param>
    /// <param name="byteOffset">Byte offset in file (type 1) or object stream number (type 2)</param>
    /// <param name="generationNumber">Generation number (type 1) or index in object stream (type 2)</param>
    /// <param name="isInUse">True if object is in use, false if free</param>
    /// <param name="entryType">Entry type (default: Uncompressed for in-use, Free for not in-use)</param>
    public PdfXrefEntry(int objectNumber, long byteOffset, int generationNumber, bool isInUse,
        PdfXrefEntryType? entryType = null)
    {
        ObjectNumber = objectNumber;
        ByteOffset = byteOffset;
        GenerationNumber = generationNumber;
        IsInUse = isInUse;

        // Default entry type based on isInUse if not specified
        EntryType = entryType ?? (isInUse ? PdfXrefEntryType.Uncompressed : PdfXrefEntryType.Free);
    }

    /// <summary>
    /// Object number
    /// </summary>
    public int ObjectNumber { get; }

    /// <summary>
    /// For type 1: Byte offset in the file
    /// For type 2: Object number of the object stream containing this object
    /// For type 0: Next free object number
    /// </summary>
    public long ByteOffset { get; }

    /// <summary>
    /// For type 1: Generation number
    /// For type 2: Index of this object within the object stream
    /// For type 0: Generation number to use if this object is reused
    /// </summary>
    public int GenerationNumber { get; }

    /// <summary>
    /// True if the object is in use, false if free
    /// </summary>
    public bool IsInUse { get; }

    /// <summary>
    /// Entry type (0=free, 1=uncompressed, 2=compressed in object stream)
    /// </summary>
    public PdfXrefEntryType EntryType { get; }

    /// <summary>
    /// Creates an in-use entry
    /// </summary>
    public static PdfXrefEntry InUse(int objectNumber, long byteOffset, int generationNumber) =>
        new(objectNumber, byteOffset, generationNumber, true);

    /// <summary>
    /// Creates a free entry
    /// </summary>
    public static PdfXrefEntry Free(int objectNumber, int nextFreeObjectNumber, int generationNumber) =>
        new(objectNumber, nextFreeObjectNumber, generationNumber, false);

    public override string ToString() =>
        IsInUse
            ? $"{ObjectNumber} {GenerationNumber} in-use @ {ByteOffset}"
            : $"{ObjectNumber} {GenerationNumber} free (next: {ByteOffset})";
}
