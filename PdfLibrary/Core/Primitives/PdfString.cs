using System.Text;

namespace PdfLibrary.Core.Primitives;

/// <summary>
/// Represents a PDF string object (ISO 32000-1:2008 section 7.3.4)
/// Strings can be literal (enclosed in parentheses) or hexadecimal (enclosed in angle brackets)
/// </summary>
internal sealed class PdfString(byte[] bytes, PdfStringFormat format = PdfStringFormat.Literal)
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
                        // PDF literal-string escapes are OCTAL (\ddd). This previously emitted the
                        // byte in DECIMAL, which the parser then read back as octal — corrupting every
                        // byte >= 64 on a save/optimize round-trip (e.g. a UTF-16 BOM FE FF came back
                        // as AC AD, garbling titles, URLs and any binary string).
                        sb.Append('\\')
                          .Append((char)('0' + ((b >> 6) & 7)))
                          .Append((char)('0' + ((b >> 3) & 7)))
                          .Append((char)('0' + (b & 7)));
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

    /// <summary>
    /// Creates a PDF TEXT string (ISO 32000 §7.9.2): single-byte PDFDocEncoding when representable,
    /// otherwise UTF-16BE with a FE FF BOM. UTF-16BE serializes as hex; PDFDocEncoding as a literal.
    /// Use this for Info values, outline titles, annotation /Contents, etc. — NOT for byte strings.
    /// </summary>
    public static PdfString FromText(string text)
    {
        byte[] bytes = PdfDocEncoding.Encode(text);
        bool utf16 = bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF;
        return new PdfString(bytes, utf16 ? PdfStringFormat.Hexadecimal : PdfStringFormat.Literal);
    }

    /// <summary>Decodes this string as a PDF text string (BOM-sniffed PDFDocEncoding/UTF-16BE/UTF-8).</summary>
    public string GetText() => PdfDocEncoding.Decode(_bytes);

    /// <summary>
    /// Creates a string from raw byte-literal text via Latin-1 (byte == char). For BYTE strings and
    /// ASCII tokens only (the /ID, PDF date strings, etc.) — never for human-facing text.
    /// </summary>
    public static PdfString FromByteLiteral(string value) =>
        new(Encoding.Latin1.GetBytes(value), PdfStringFormat.Literal);

    public static implicit operator string(PdfString pdfString) => pdfString.Value;
    public static implicit operator PdfString(string value) => new(value);
}
