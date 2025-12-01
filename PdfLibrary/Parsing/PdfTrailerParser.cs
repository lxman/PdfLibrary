using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Parsing;

/// <summary>
/// Parses PDF trailer dictionaries (ISO 32000-1:2008 section 7.5.5)
/// </summary>
internal class PdfTrailerParser(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly PdfParser _parser = new(stream);

    /// <summary>
    /// Parses a trailer section starting at the current stream position
    /// Returns the trailer dictionary and the startxref position
    /// </summary>
    public (PdfTrailer trailer, long startxref) Parse()
    {
        // The parser uses a lexer which may have buffered data.
        // We need to use the lexer for reading keywords, not direct stream access.
        // For now, we'll read the "trailer" keyword directly since we're positioned there,
        // but use the parser for everything else.

        // Read the "trailer" keyword
        var keyword = ReadKeyword();
        if (keyword != "trailer")
            throw new PdfParseException($"Expected 'trailer' keyword, got: {keyword}");

        // Parse trailer dictionary - this uses the lexer
        var dictObj = _parser.ReadObject();
        if (dictObj is not PdfDictionary dictionary)
            throw new PdfParseException($"Expected dictionary after 'trailer' keyword, got: {dictObj?.Type}");

        var trailer = new PdfTrailer(dictionary);

        // After parsing the dictionary, the parser may have a buffered token from peeking.
        // Use the parser's NextToken() method to properly handle buffered tokens.
        // The next token should be "startxref"
        var token = _parser.NextToken();

        if (token.Type != PdfTokenType.StartXref)
            throw new PdfParseException($"Expected 'startxref' keyword after trailer, got: {token.Type} (value: '{token.Value}')");

        // Read the integer value
        token = _parser.NextToken();
        if (token.Type != PdfTokenType.Integer)
            throw new PdfParseException($"Expected integer after 'startxref' keyword, got: {token.Type}");

        var startxref = long.Parse(token.Value);
        return (trailer, startxref);
    }

    /// <summary>
    /// Finds the startxref position by reading from the end of the file
    /// </summary>
    public static long FindStartXref(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable", nameof(stream));

        // Read the last 1024 bytes (should be enough to find startxref)
        const int searchSize = 1024;
        var fileSize = stream.Length;
        var searchStart = Math.Max(0, fileSize - searchSize);

        stream.Position = searchStart;
        var buffer = new byte[Math.Min(searchSize, fileSize)];
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        // Convert to string and search for "startxref"
        var text = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        var startxrefIndex = text.LastIndexOf("startxref", StringComparison.Ordinal);

        if (startxrefIndex == -1)
            throw new PdfParseException("Could not find 'startxref' keyword at end of PDF file");

        // Find the number after "startxref"
        var numberStart = startxrefIndex + "startxref".Length;

        // Skip whitespace
        while (numberStart < text.Length && char.IsWhiteSpace(text[numberStart]))
            numberStart++;

        // Extract number
        var sb = new StringBuilder();
        while (numberStart < text.Length && char.IsDigit(text[numberStart]))
        {
            sb.Append(text[numberStart]);
            numberStart++;
        }

        if (sb.Length == 0)
            throw new PdfParseException("Could not parse startxref value");

        return !long.TryParse(sb.ToString(), out var startxref)
            ? throw new PdfParseException($"Invalid startxref value: {sb}")
            : startxref;
    }

    /// <summary>
    /// Reads a PDF keyword from the stream
    /// </summary>
    private string? ReadKeyword()
    {
        SkipWhitespace();

        var sb = new StringBuilder();
        int b;

        while ((b = _stream.ReadByte()) != -1)
        {
            var ch = (char)b;

            // Keywords end at whitespace or delimiters
            if (char.IsWhiteSpace(ch) || IsDelimiter(ch))
            {
                // Move back one position
                _stream.Position--;
                break;
            }

            sb.Append(ch);
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private void SkipWhitespace()
    {
        int b;
        while ((b = _stream.ReadByte()) != -1)
        {
            var ch = (char)b;
            if (char.IsWhiteSpace(ch)) continue;
            _stream.Position--;
            break;
        }
    }

    private static bool IsDelimiter(char ch) =>
        ch is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';
}
