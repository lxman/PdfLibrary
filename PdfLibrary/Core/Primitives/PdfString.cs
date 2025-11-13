using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF string object (ISO 32000-1:2008 section 7.3.4)
/// Strings can be literal (enclosed in parentheses) or hexadecimal (enclosed in angle brackets)
/// </summary>
public sealed class PdfString(byte[] bytes, PdfStringFormat format = PdfStringFormat.Literal)
    : PdfObject
{
    private readonly byte[] _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    private readonly PdfStringFormat _format = format;

    public PdfString(string value, PdfStringFormat format = PdfStringFormat.Literal)
        : this(Encoding.Latin1.GetBytes(value), format)
    {
    }

    public override PdfObjectType Type => PdfObjectType.String;

    /// <summary>
    /// Gets the raw bytes of the string
    /// </summary>
    public byte[] Bytes => (byte[])_bytes.Clone();

    /// <summary>
    /// Gets the string value using Latin-1 encoding (PDFDocEncoding)
    /// </summary>
    public string Value => Encoding.Latin1.GetString(_bytes);

    public override string ToPdfString()
    {
        return _format switch
        {
            PdfStringFormat.Literal => ToLiteralString(),
            PdfStringFormat.Hexadecimal => ToHexadecimalString(),
            _ => throw new InvalidOperationException($"Unknown string format: {_format}")
        };
    }

    private string ToLiteralString()
    {
        var sb = new StringBuilder();
        sb.Append('(');

        foreach (byte b in _bytes)
        {
            var c = (char)b;
            switch (c)
            {
                case '(':
                case ')':
                case '\\':
                    sb.Append('\\').Append(c);
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    if (b is < 32 or > 126)
                        sb.Append($"\\{b:000}"); // Octal escape
                    else
                        sb.Append(c);
                    break;
            }
        }

        sb.Append(')');
        return sb.ToString();
    }

    private string ToHexadecimalString()
    {
        var sb = new StringBuilder();
        sb.Append('<');

        foreach (byte b in _bytes)
        {
            sb.Append($"{b:X2}");
        }

        sb.Append('>');
        return sb.ToString();
    }

    public override bool Equals(object? obj) =>
        obj is PdfString other && _bytes.SequenceEqual(other._bytes);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (byte b in _bytes)
            hash.Add(b);
        return hash.ToHashCode();
    }

    public static implicit operator string(PdfString pdfString) => pdfString.Value;
    public static implicit operator PdfString(string value) => new(value);
}

/// <summary>
/// Format for PDF string representation
/// </summary>
public enum PdfStringFormat
{
    /// <summary>
    /// Literal strings enclosed in parentheses: (text)
    /// </summary>
    Literal,

    /// <summary>
    /// Hexadecimal strings enclosed in angle brackets: &lt;48656C6C6F&gt;
    /// </summary>
    Hexadecimal
}
