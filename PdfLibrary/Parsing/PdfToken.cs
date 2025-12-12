namespace PdfLibrary.Parsing;

/// <summary>
/// Represents a token in a PDF file (ISO 32000-1:2008 section 7.2)
/// </summary>
internal readonly struct PdfToken(PdfTokenType type, string value, long position)
{
    /// <summary>
    /// Type of token
    /// </summary>
    public PdfTokenType Type { get; } = type;

    /// <summary>
    /// String value of the token
    /// </summary>
    public string Value { get; } = value;

    /// <summary>
    /// Byte position in the stream where this token starts
    /// </summary>
    public long Position { get; } = position;

    public override string ToString() => $"{Type}: {Value} @ {Position}";
}
