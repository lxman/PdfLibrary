using System.Globalization;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF real number object (ISO 32000-1:2008 section 7.3.3)
/// Real numbers are written as decimal numerals with optional decimal point
/// </summary>
internal sealed class PdfReal(double value) : PdfObject
{
    public double Value { get; } = value;

    public override PdfObjectType Type => PdfObjectType.Real;

    public override string ToPdfString()
    {
        if (!double.IsFinite(Value))
            throw new InvalidOperationException("A PDF real number must have a finite value.");

        // "R" gives the shortest decimal text that parses back to the same binary double, but it
        // may use exponent notation, which ISO 32000-1 section 7.3.3 forbids PDF writers to emit.
        // Expand that notation instead of limiting the number of fractional places: a full rewrite
        // must not silently move matrices, coordinates, colours, or any other real-valued operand.
        string shortest = Value.ToString("R", CultureInfo.InvariantCulture);
        int exponentMarker = shortest.IndexOf('E');
        if (exponentMarker < 0)
            exponentMarker = shortest.IndexOf('e');
        if (exponentMarker < 0)
            return shortest.Contains('.') ? shortest : shortest + ".0";

        int exponent = int.Parse(
            shortest.AsSpan(exponentMarker + 1),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);

        bool negative = shortest[0] == '-';
        int mantissaStart = negative ? 1 : 0;
        string mantissa = shortest[mantissaStart..exponentMarker];
        int point = mantissa.IndexOf('.');
        int integerDigits = point < 0 ? mantissa.Length : point;
        string digits = point < 0 ? mantissa : mantissa.Remove(point, 1);
        int decimalPosition = integerDigits + exponent;
        string sign = negative ? "-" : string.Empty;

        if (decimalPosition <= 0)
            return sign + "0." + new string('0', -decimalPosition) + digits;

        if (decimalPosition >= digits.Length)
            return sign + digits + new string('0', decimalPosition - digits.Length) + ".0";

        return sign + digits.Insert(decimalPosition, ".");
    }

    public override bool Equals(object? obj) => obj is PdfReal other && Math.Abs(other.Value - Value) < double.Epsilon;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator double(PdfReal pdfReal) => pdfReal.Value;
    public static implicit operator PdfReal(double value) => new(value);
}
