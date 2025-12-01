using System.Globalization;
using System.Text;

namespace PdfLibrary.Parsing;

/// <summary>
/// Tokenizes PDF byte streams according to ISO 32000-1:2008 section 7.2
/// </summary>
internal class PdfLexer(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _bufferPosition;
    private int _bufferLength;
    private long _streamPosition = stream.CanSeek ? stream.Position : 0;

    private const int BufferSize = 4096;

    // PDF white-space characters (7.2.2)
    private static readonly HashSet<byte> WhiteSpace =
    [
        0x00, // NULL
        0x09, // TAB
        0x0A, // LF
        0x0C, // FF
        0x0D, // CR
        0x20
    ];

    // PDF delimiter characters (7.2.2)
    private static readonly HashSet<byte> Delimiters =
    [
        (byte)'(', (byte)')', (byte)'<', (byte)'>',
        (byte)'[', (byte)']', (byte)'{', (byte)'}',
        (byte)'/', (byte)'%'
    ];

    // Initialize _streamPosition to current stream position for absolute position tracking

    /// <summary>
    /// Gets the current position in the stream
    /// </summary>
    public long Position => _streamPosition + _bufferPosition;

    /// <summary>
    /// Reads the next token from the stream
    /// </summary>
    public PdfToken NextToken()
    {
        SkipWhiteSpaceAndComments();

        if (!TryPeek(out byte ch))
            return new PdfToken(PdfTokenType.EndOfFile, string.Empty, Position);

        return ch switch
        {
            (byte)'[' => ReadSingleChar(PdfTokenType.ArrayStart),
            (byte)']' => ReadSingleChar(PdfTokenType.ArrayEnd),
            (byte)'(' => ReadLiteralString(),
            (byte)'<' => ReadHexStringOrDictionaryStart(),
            (byte)'>' => ReadDictionaryEnd(),
            (byte)'/' => ReadName(),
            (byte)'+' or (byte)'-' or (byte)'.' or >= (byte)'0' and <= (byte)'9' => ReadNumber(),
            _ => ReadKeywordOrBoolean()
        };
    }

    /// <summary>
    /// Peeks at all remaining tokens without consuming them
    /// </summary>
    public List<PdfToken> ReadAllTokens()
    {
        var tokens = new List<PdfToken>();
        PdfToken token;

        while ((token = NextToken()).Type != PdfTokenType.EndOfFile)
        {
            tokens.Add(token);
        }

        return tokens;
    }

    #region Reading Methods

    private PdfToken ReadSingleChar(PdfTokenType type)
    {
        long position = Position;
        var ch = (char)Read();
        return new PdfToken(type, ch.ToString(), position);
    }

    private PdfToken ReadLiteralString()
    {
        long position = Position;
        var sb = new StringBuilder();
        Read(); // Skip opening (

        var depth = 1;
        var escaped = false;

        while (TryPeek(out byte ch) && depth > 0)
        {
            Read();

            if (escaped)
            {
                // Handle escape sequences (7.3.4.2)
                sb.Append(ch switch
                {
                    (byte)'n' => '\n',
                    (byte)'r' => '\r',
                    (byte)'t' => '\t',
                    (byte)'b' => '\b',
                    (byte)'f' => '\f',
                    (byte)'(' => '(',
                    (byte)')' => ')',
                    (byte)'\\' => '\\',
                    >= (byte)'0' and <= (byte)'7' => (char)ReadOctalEscape(ch),
                    _ => (char)ch
                });
                escaped = false;
            }
            else if (ch == (byte)'\\')
            {
                escaped = true;
            }
            else if (ch == (byte)'(')
            {
                depth++;
                sb.Append('(');
            }
            else if (ch == (byte)')')
            {
                depth--;
                if (depth > 0)
                    sb.Append(')');
            }
            else
            {
                sb.Append((char)ch);
            }
        }

        return new PdfToken(PdfTokenType.String, sb.ToString(), position);
    }

    private int ReadOctalEscape(byte first)
    {
        int value = first - '0';
        var count = 1;

        while (count < 3 && TryPeek(out byte ch) && ch >= '0' && ch <= '7')
        {
            Read();
            value = (value * 8) + (ch - '0');
            count++;
        }

        return value;
    }

    private PdfToken ReadHexStringOrDictionaryStart()
    {
        long position = Position;
        Read(); // Skip first <

        if (TryPeek(out byte next) && next == (byte)'<')
        {
            Read(); // Skip second <
            return new PdfToken(PdfTokenType.DictionaryStart, "<<", position);
        }

        // Hexadecimal string
        // Hex strings contain pairs of hex digits that should be converted to bytes
        // E.g., <01> should become byte 0x01, not the characters '0' and '1'
        var hexDigits = new StringBuilder();

        // Collect all hex digits (ignoring whitespace)
        while (TryPeek(out byte ch) && ch != (byte)'>')
        {
            Read();
            if (!WhiteSpace.Contains(ch))
                hexDigits.Append((char)ch);
        }

        if (TryPeek(out _))
            Read(); // Skip closing >

        // Convert hex digit pairs to bytes
        var hexString = hexDigits.ToString();
        var bytes = new List<byte>();

        for (var i = 0; i < hexString.Length; i += 2)
        {
            // Get two hex digits (or one if odd length, pad with 0)
            string hexPair = i + 1 < hexString.Length
                ? hexString.Substring(i, 2)
                : hexString[i] + "0";

            // Convert hex pair to byte
            if (byte.TryParse(hexPair, NumberStyles.HexNumber, null, out byte b))
            {
                bytes.Add(b);
            }
        }

        // Convert bytes to string for the token value
        // This will be parsed later by PdfParser into a PdfString with the correct bytes
        string value = Encoding.Latin1.GetString(bytes.ToArray());
        return new PdfToken(PdfTokenType.String, value, position);
    }

    private PdfToken ReadDictionaryEnd()
    {
        long position = Position;
        Read(); // Skip first >

        if (TryPeek(out byte next) && next == (byte)'>')
        {
            Read(); // Skip second >
            return new PdfToken(PdfTokenType.DictionaryEnd, ">>", position);
        }

        return new PdfToken(PdfTokenType.Unknown, ">", position);
    }

    private PdfToken ReadName()
    {
        long position = Position;
        Read(); // Skip /

        var sb = new StringBuilder();

        while (TryPeek(out byte ch) && !WhiteSpace.Contains(ch) && !Delimiters.Contains(ch))
        {
            Read();
            sb.Append((char)ch);
        }

        return new PdfToken(PdfTokenType.Name, sb.ToString(), position);
    }

    private PdfToken ReadNumber()
    {
        long position = Position;
        var sb = new StringBuilder();
        var hasDecimalPoint = false;

        // Read sign if present
        if (TryPeek(out byte first) && first is (byte)'+' or (byte)'-')
        {
            Read();
            sb.Append((char)first);
        }

        // Read digits and optional decimal point
        while (TryPeek(out byte ch))
        {
            if (ch is >= (byte)'0' and <= (byte)'9')
            {
                Read();
                sb.Append((char)ch);
            }
            else if (ch == (byte)'.' && !hasDecimalPoint)
            {
                Read();
                sb.Append('.');
                hasDecimalPoint = true;
            }
            else if (WhiteSpace.Contains(ch) || Delimiters.Contains(ch))
            {
                break;
            }
            else
            {
                break;
            }
        }

        var value = sb.ToString();
        PdfTokenType type = hasDecimalPoint ? PdfTokenType.Real : PdfTokenType.Integer;

        return new PdfToken(type, value, position);
    }

    private PdfToken ReadKeywordOrBoolean()
    {
        long position = Position;
        var sb = new StringBuilder();

        while (TryPeek(out byte ch) && !WhiteSpace.Contains(ch) && !Delimiters.Contains(ch))
        {
            Read();
            sb.Append((char)ch);
        }

        var value = sb.ToString();

        PdfTokenType type = value switch
        {
            "true" or "false" => PdfTokenType.Boolean,
            "null" => PdfTokenType.Null,
            "obj" => PdfTokenType.Obj,
            "endobj" => PdfTokenType.EndObj,
            "stream" => PdfTokenType.Stream,
            "endstream" => PdfTokenType.EndStream,
            "R" => PdfTokenType.R,
            "xref" => PdfTokenType.Xref,
            "trailer" => PdfTokenType.Trailer,
            "startxref" => PdfTokenType.StartXref,
            _ => PdfTokenType.Unknown
        };

        return new PdfToken(type, value, position);
    }

    #endregion

    #region Stream Management

    private void SkipWhiteSpaceAndComments()
    {
        while (TryPeek(out byte ch))
        {
            if (WhiteSpace.Contains(ch))
            {
                Read();
            }
            else if (ch == (byte)'%')
            {
                SkipComment();
            }
            else
            {
                break;
            }
        }
    }

    private void SkipComment()
    {
        // Skip until end of line (CR, LF, or EOF)
        while (TryPeek(out byte ch) && ch != 0x0A && ch != 0x0D)
        {
            Read();
        }

        // Skip the line ending character(s)
        if (!TryPeek(out byte eol) || (eol != 0x0A && eol != 0x0D)) return;
        Read();
        // Handle CRLF
        if (eol == 0x0D && TryPeek(out byte lf) && lf == 0x0A)
            Read();
    }

    /// <summary>
    /// Skips an End-Of-Line marker according to PDF spec (ISO 32000-1 section 7.2.3)
    /// An EOL marker is either: CR (0x0D), LF (0x0A), or CRLF (0x0D 0x0A)
    /// </summary>
    internal void SkipEOL()
    {
        if (!TryPeek(out byte ch))
            return;

        if (ch == 0x0D) // CR
        {
            Read();
            // Check for LF to handle CRLF
            if (TryPeek(out byte next) && next == 0x0A)
                Read();
        }
        else if (ch == 0x0A) // LF
        {
            Read();
        }
        // If neither CR nor LF, don't consume anything
    }

    private bool TryPeek(out byte value)
    {
        if (_bufferPosition >= _bufferLength)
        {
            if (!FillBuffer())
            {
                value = 0;
                return false;
            }
        }

        value = _buffer[_bufferPosition];
        return true;
    }

    private byte Read()
    {
        if (!TryPeek(out byte value))
            throw new EndOfStreamException("Unexpected end of PDF stream");

        _bufferPosition++;
        return value;
    }

    /// <summary>
    /// Gets the underlying stream for direct byte access (e.g., for inline images)
    /// </summary>
    public Stream? GetStream()
    {
        // Flush any buffered data back to stream position
        if (_stream.CanSeek)
        {
            // Calculate and seek to current logical position
            long currentLogicalPosition = _streamPosition + _bufferPosition;
            _stream.Seek(currentLogicalPosition, SeekOrigin.Begin);
            // Update _streamPosition to reflect the new base position
            _streamPosition = currentLogicalPosition;
            // Clear buffer so we don't double-read
            _bufferLength = 0;
            _bufferPosition = 0;
        }
        return _stream;
    }

    /// <summary>
    /// Synchronizes the lexer position after direct stream access
    /// Call this after reading from GetStream() to update position tracking
    /// </summary>
    public void SyncPositionFromStream()
    {
        if (_stream.CanSeek)
        {
            _streamPosition = _stream.Position;
            _bufferLength = 0;
            _bufferPosition = 0;
        }
    }

    /// <summary>
    /// Reads raw bytes from the stream (for stream content)
    /// </summary>
    internal byte[] ReadBytes(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));

        var result = new byte[count];
        var bytesRead = 0;

        while (bytesRead < count)
        {
            // Read from buffer if available
            int availableInBuffer = _bufferLength - _bufferPosition;
            if (availableInBuffer > 0)
            {
                int toRead = Math.Min(count - bytesRead, availableInBuffer);
                Array.Copy(_buffer, _bufferPosition, result, bytesRead, toRead);
                _bufferPosition += toRead;
                bytesRead += toRead;
            }
            else
            {
                // Need to fill buffer
                if (!FillBuffer())
                    throw new EndOfStreamException($"Unexpected end of stream: expected {count} bytes, got {bytesRead}");
            }
        }

        return result;
    }

    private bool FillBuffer()
    {
        _streamPosition += _bufferLength;
        _bufferLength = _stream.Read(_buffer, 0, BufferSize);
        _bufferPosition = 0;
        return _bufferLength > 0;
    }

    #endregion
}
