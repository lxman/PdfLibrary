namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF integer object (ISO 32000-1:2008 section 7.3.3)
/// Integer objects represent mathematical integers written as decimal numerals
/// </summary>
public sealed class PdfInteger(int value) : PdfObject
{
    public int Value { get; } = value;

    public override PdfObjectType Type => PdfObjectType.Integer;

    public override string ToPdfString() => Value.ToString();

    public override bool Equals(object? obj) => obj is PdfInteger other && other.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator int(PdfInteger pdfInt) => pdfInt.Value;
    public static implicit operator PdfInteger(int value) => new(value);
}
