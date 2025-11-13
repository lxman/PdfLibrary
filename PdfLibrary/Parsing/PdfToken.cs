namespace PdfLibrary.Parsing;

/// <summary>
/// Represents a token in a PDF file (ISO 32000-1:2008 section 7.2)
/// </summary>
public readonly struct PdfToken(PdfTokenType type, string value, long position)
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

/// <summary>
/// Types of tokens in PDF syntax
/// </summary>
public enum PdfTokenType
{
    // Primitives
    Integer,            // 123, -17, +42
    Real,               // 3.14, -0.5, +1.0
    String,             // (text) or <48656C6C6F>
    Name,               // /Name, /Type, /Lime#20Green
    Boolean,            // true, false
    Null,               // null

    // Delimiters
    ArrayStart,         // [
    ArrayEnd,           // ]
    DictionaryStart,    // <<
    DictionaryEnd,      // >>

    // Keywords
    Obj,                // obj
    EndObj,             // endobj
    Stream,             // stream
    EndStream,          // endstream
    R,                  // R (indirect reference)
    Xref,               // xref
    Trailer,            // trailer
    StartXref,          // startxref

    // Special
    Comment,            // % comment text
    EndOfFile,          // End of stream
    Unknown             // Unrecognized token
}
