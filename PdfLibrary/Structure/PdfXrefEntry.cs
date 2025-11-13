namespace PdfLibrary.Structure;

/// <summary>
/// Represents a single entry in the cross-reference table (ISO 32000-1:2008 section 7.5.4)
/// </summary>
public class PdfXrefEntry
{
    /// <summary>
    /// Creates a cross-reference entry
    /// </summary>
    /// <param name="objectNumber">Object number</param>
    /// <param name="byteOffset">Byte offset in file (for in-use objects)</param>
    /// <param name="generationNumber">Generation number</param>
    /// <param name="isInUse">True if object is in use, false if free</param>
    public PdfXrefEntry(int objectNumber, long byteOffset, int generationNumber, bool isInUse)
    {
        ObjectNumber = objectNumber;
        ByteOffset = byteOffset;
        GenerationNumber = generationNumber;
        IsInUse = isInUse;
    }

    /// <summary>
    /// Object number
    /// </summary>
    public int ObjectNumber { get; }

    /// <summary>
    /// Byte offset in the file (for in-use objects) or next free object number (for free objects)
    /// </summary>
    public long ByteOffset { get; }

    /// <summary>
    /// Generation number
    /// </summary>
    public int GenerationNumber { get; }

    /// <summary>
    /// True if the object is in use, false if free
    /// </summary>
    public bool IsInUse { get; }

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
