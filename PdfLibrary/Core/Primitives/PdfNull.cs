namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents the PDF null object (ISO 32000-1:2008 section 7.3.9)
/// The null object has a type and value unequal to any other object.
/// </summary>
public sealed class PdfNull : PdfObject
{
    /// <summary>
    /// Singleton instance of the null object
    /// </summary>
    public static readonly PdfNull Instance = new();

    private PdfNull() { }

    public override PdfObjectType Type => PdfObjectType.Null;

    public override string ToPdfString() => "null";

    public override bool Equals(object? obj) => obj is PdfNull;

    public override int GetHashCode() => 0;
}
