using System.Globalization;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF real number object (ISO 32000-1:2008 section 7.3.3)
/// Real numbers are written as decimal numerals with optional decimal point
/// </summary>
public sealed class PdfReal(double value) : PdfObject
{
    public double Value { get; } = value;

    public override PdfObjectType Type => PdfObjectType.Real;

    public override string ToPdfString()
    {
        // PDF spec requires decimal point representation
        // Use invariant culture to ensure proper decimal formatting
        var result = Value.ToString("0.######", CultureInfo.InvariantCulture);

        // Ensure there's a decimal point if it's a whole number
        if (!result.Contains('.'))
            result += ".0";

        return result;
    }

    public override bool Equals(object? obj) => obj is PdfReal other && Math.Abs(other.Value - Value) < double.Epsilon;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator double(PdfReal pdfReal) => pdfReal.Value;
    public static implicit operator PdfReal(double value) => new(value);
}
