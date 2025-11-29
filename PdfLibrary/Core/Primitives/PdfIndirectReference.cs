namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents an indirect reference to a PDF object (ISO 32000-1:2008 section 7.3.10)
/// Format: objectNumber generationNumber R
/// Example: 12 0 R
/// </summary>
internal sealed class PdfIndirectReference : PdfObject
{
    public PdfIndirectReference(int objectNumber, int generationNumber)
    {
        if (objectNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(objectNumber), "Object number must be positive");
        if (generationNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(generationNumber), "Generation number must be non-negative");

        ObjectNumber = objectNumber;
        GenerationNumber = generationNumber;
    }

    public override PdfObjectType Type => PdfObjectType.IndirectReference;

    /// <summary>
    /// Gets the object number (positive integer)
    /// </summary>
    public new int ObjectNumber { get; }

    /// <summary>
    /// Gets the generation number (non-negative integer)
    /// </summary>
    public new int GenerationNumber { get; }

    public override string ToPdfString() =>
        $"{ObjectNumber} {GenerationNumber} R";

    public override bool Equals(object? obj) =>
        obj is PdfIndirectReference other &&
        other.ObjectNumber == ObjectNumber &&
        other.GenerationNumber == GenerationNumber;

    public override int GetHashCode() =>
        HashCode.Combine(ObjectNumber, GenerationNumber);

    /// <summary>
    /// Creates an indirect object definition wrapper
    /// Format: objectNumber generationNumber obj...content...endobj
    /// </summary>
    public static string ToIndirectObjectDefinition(int objectNumber, int generationNumber, PdfObject content)
    {
        return $"{objectNumber} {generationNumber} obj\n{content.ToPdfString()}\nendobj";
    }
}
