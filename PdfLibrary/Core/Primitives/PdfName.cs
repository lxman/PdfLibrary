using System.Globalization;
using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF name object (ISO 32000-1:2008 section 7.3.5)
/// Names are atomic symbols uniquely defined by sequences of characters
/// Names are written with a leading SOLIDUS (/) in PDF files
/// </summary>
internal sealed class PdfName : PdfObject
{
    /// <summary>
    /// Creates a PDF name from a string value (without the leading solidus)
    /// </summary>
    public PdfName(string value)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    public override PdfObjectType Type => PdfObjectType.Name;

    /// <summary>
    /// Gets the name value (without the leading solidus)
    /// </summary>
    public string Value { get; }

    public override string ToPdfString()
    {
        if (string.IsNullOrEmpty(Value))
            return "/"; // Empty name

        var sb = new StringBuilder();
        sb.Append('/');

        foreach (char c in Value)
        {
            var b = (byte)c;

            // Check if a character needs to be encoded
            // Regular characters: EXCLAMATION MARK (21h) to TILDE (7Eh) except # and delimiters
            bool isRegular = b is >= 0x21 and <= 0x7E &&
                             c != '#' &&
                             c != '(' && c != ')' &&
                             c != '<' && c != '>' &&
                             c != '[' && c != ']' &&
                             c != '{' && c != '}' &&
                             c != '/' && c != '%';

            // White space must always be encoded
            bool isWhiteSpace = c is ' ' or '\t' or '\r' or '\n' or '\f' or '\0';

            if (isWhiteSpace || !isRegular || b < 0x21 || b > 0x7E)
            {
                // Encode using #XX hex notation
                sb.Append($"#{b:X2}");
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses a PDF name from its string representation (with or without the leading solidus)
    /// Handles #XX hex escape sequences
    /// </summary>
    public static PdfName Parse(string input)
    {
        // Handle null input
        ArgumentNullException.ThrowIfNull(input);

        // Handle empty input
        if (input.Length == 0)
            return new PdfName("");

        // Remove leading solidus if present
        string value = input.StartsWith('/') ? input[1..] : input;

        if (string.IsNullOrEmpty(value))
            return new PdfName(""); // Empty name

        var sb = new StringBuilder();
        var i = 0;

        while (i < value.Length)
        {
            if (value[i] == '#' && i + 2 < value.Length)
            {
                // Parse hex escape sequence
                string hexCode = value.Substring(i + 1, 2);
                if (byte.TryParse(hexCode, NumberStyles.HexNumber, null, out byte b))
                {
                    sb.Append((char)b);
                    i += 3;
                    continue;
                }
            }

            sb.Append(value[i]);
            i++;
        }

        return new PdfName(sb.ToString());
    }

    public override bool Equals(object? obj) =>
        obj is PdfName other && other.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator string(PdfName pdfName) => pdfName.Value;

    // Common PDF names as static properties for convenience
    // Note: Cannot use "Type" as it conflicts with PdfObject.Type property
    public static readonly PdfName TypeName = new("Type");
    public static readonly PdfName Subtype = new("Subtype");
    public static readonly PdfName Length = new("Length");
    public static readonly PdfName Filter = new("Filter");
    public static readonly PdfName DecodeParms = new("DecodeParms");
    public static readonly PdfName Width = new("Width");
    public static readonly PdfName Height = new("Height");
    public static readonly PdfName ColorSpace = new("ColorSpace");
    public static readonly PdfName BitsPerComponent = new("BitsPerComponent");
}
