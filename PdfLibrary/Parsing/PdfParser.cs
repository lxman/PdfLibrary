using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Parsing;

/// <summary>
/// Parses PDF tokens into PDF objects (ISO 32000-1:2008 section 7.3)
/// </summary>
public class PdfParser(PdfLexer lexer)
{
    private readonly PdfLexer _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
    private readonly Queue<PdfToken> _tokenBuffer = new();
    private Func<PdfIndirectReference, PdfObject?>? _referenceResolver;

    public PdfParser(Stream stream) : this(new PdfLexer(stream))
    {
    }

    /// <summary>
    /// Sets a function to resolve indirect references during parsing
    /// </summary>
    public void SetReferenceResolver(Func<PdfIndirectReference, PdfObject?> resolver)
    {
        _referenceResolver = resolver;
    }

    /// <summary>
    /// Gets the current position in the stream
    /// </summary>
    public long Position => _lexer.Position;

    /// <summary>
    /// Gets the underlying lexer for token-level access
    /// </summary>
    internal PdfLexer Lexer => _lexer;

    /// <summary>
    /// Reads the next PDF object from the stream
    /// </summary>
    public PdfObject? ReadObject()
    {
        PdfToken token = NextToken();

        if (token.Type == PdfTokenType.EndOfFile)
            return null;

        return token.Type switch
        {
            PdfTokenType.Null => PdfNull.Instance,
            PdfTokenType.Boolean => ParseBoolean(token),
            PdfTokenType.Integer => ParseIntegerOrReference(token),
            PdfTokenType.Real => new PdfReal(double.Parse(token.Value)),
            PdfTokenType.String => ParseString(token),
            PdfTokenType.Name => ParseName(token),
            PdfTokenType.ArrayStart => ParseArray(),
            PdfTokenType.DictionaryStart => ParseDictionaryOrStream(),
            _ => throw new PdfParseException($"Unexpected token: {token.Type} at position {token.Position}")
        };
    }

    /// <summary>
    /// Reads all objects from the stream
    /// </summary>
    public List<PdfObject> ReadAllObjects()
    {
        var objects = new List<PdfObject>();
        PdfObject? obj;

        while ((obj = ReadObject()) != null)
        {
            objects.Add(obj);
        }

        return objects;
    }

    #region Token Management

    internal PdfToken NextToken()
    {
        if (_tokenBuffer.Count > 0)
            return _tokenBuffer.Dequeue();

        return _lexer.NextToken();
    }

    private PdfToken PeekToken()
    {
        if (_tokenBuffer.Count == 0)
            _tokenBuffer.Enqueue(_lexer.NextToken());

        return _tokenBuffer.Peek();
    }

    private void PushBackToken(PdfToken token)
    {
        // Insert at the front of the queue by creating a new queue
        var newQueue = new Queue<PdfToken>();
        newQueue.Enqueue(token);
        while (_tokenBuffer.Count > 0)
            newQueue.Enqueue(_tokenBuffer.Dequeue());

        // Replace the buffer with the new queue
        _tokenBuffer.Clear();
        while (newQueue.Count > 0)
            _tokenBuffer.Enqueue(newQueue.Dequeue());
    }

    private void ExpectToken(PdfTokenType expectedType)
    {
        PdfToken token = NextToken();
        if (token.Type != expectedType)
            throw new PdfParseException(
                $"Expected {expectedType} but got {token.Type} at position {token.Position}");
    }

    #endregion

    #region Parsing Methods

    private static PdfBoolean ParseBoolean(PdfToken token) =>
        token.Value == "true" ? PdfBoolean.True : PdfBoolean.False;

    private PdfObject ParseIntegerOrReference(PdfToken firstToken)
    {
        // Check if this is an indirect reference (N G R) or indirect object (N G obj)
        if (!int.TryParse(firstToken.Value, out int objectNumber))
            throw new PdfParseException($"Invalid integer: {firstToken.Value}");

        PdfToken peek1 = PeekToken();

        // If not followed by another integer, it's just an integer
        if (peek1.Type != PdfTokenType.Integer)
            return new PdfInteger(objectNumber);

        PdfToken genToken = NextToken();
        if (!int.TryParse(genToken.Value, out int generationNumber))
            return new PdfInteger(objectNumber);

        PdfToken peek2 = PeekToken();

        // Check for indirect reference (N G R)
        if (peek2.Type == PdfTokenType.R)
        {
            NextToken(); // Consume R
            return new PdfIndirectReference(objectNumber, generationNumber);
        }

        // Check for indirect object definition (N G obj)
        if (peek2.Type == PdfTokenType.Obj)
        {
            NextToken(); // Consume obj
            PdfObject? content = ReadObject();
            ExpectToken(PdfTokenType.EndObj);

            if (content != null)
            {
                content.IsIndirect = true;
                content.ObjectNumber = objectNumber;
                content.GenerationNumber = generationNumber;
            }

            return content ?? PdfNull.Instance;
        }

        // Otherwise, we have two integers - push back and return the first
        PushBackToken(genToken);
        return new PdfInteger(objectNumber);
    }

    private static PdfString ParseString(PdfToken token)
    {
        // Check if it's a hex string (contains only hex digits)
        bool isHex = token.Value.All(c => c is >= '0' and <= '9' ||
                                           c is >= 'A' and <= 'F' ||
                                           c is >= 'a' and <= 'f');

        if (isHex && token.Value.Length > 0)
        {
            // Convert hex string to bytes
            var bytes = new List<byte>();
            for (var i = 0; i < token.Value.Length; i += 2)
            {
                string hex = i + 1 < token.Value.Length
                    ? token.Value.Substring(i, 2)
                    : token.Value.Substring(i, 1) + "0"; // Pad with 0 if odd length

                bytes.Add(Convert.ToByte(hex, 16));
            }

            return new PdfString(bytes.ToArray(), PdfStringFormat.Hexadecimal);
        }

        // Literal string
        return new PdfString(token.Value);
    }

    private static PdfName ParseName(PdfToken token) =>
        PdfName.Parse(token.Value);

    private PdfArray ParseArray()
    {
        var array = new PdfArray();

        while (true)
        {
            PdfToken peek = PeekToken();

            if (peek.Type == PdfTokenType.ArrayEnd)
            {
                NextToken(); // Consume ]
                break;
            }

            if (peek.Type == PdfTokenType.EndOfFile)
                throw new PdfParseException("Unexpected end of file while parsing array");

            PdfObject? element = ReadObject();
            if (element != null)
                array.Add(element);
        }

        return array;
    }

    private PdfObject ParseDictionaryOrStream()
    {
        PdfDictionary dict = ParseDictionary();

        // Check if followed by stream
        PdfToken peek = PeekToken();
        if (peek.Type == PdfTokenType.Stream)
        {
            return ParseStream(dict);
        }

        return dict;
    }

    private PdfDictionary ParseDictionary()
    {
        var dict = new PdfDictionary();

        while (true)
        {
            PdfToken peek = PeekToken();

            if (peek.Type == PdfTokenType.DictionaryEnd)
            {
                NextToken(); // Consume >>
                break;
            }

            if (peek.Type == PdfTokenType.EndOfFile)
                throw new PdfParseException("Unexpected end of file while parsing dictionary");

            // Read key (must be a name)
            PdfToken keyToken = NextToken();
            if (keyToken.Type != PdfTokenType.Name)
                throw new PdfParseException($"Dictionary key must be a name, got {keyToken.Type}");

            PdfName key = PdfName.Parse(keyToken.Value);

            // Read value
            PdfObject? value = ReadObject();
            if (value != null)
                dict[key] = value;
        }

        return dict;
    }

    /// <summary>
    /// Resolves an indirect reference to get the stream length
    /// </summary>
    private int ResolveStreamLength(PdfIndirectReference reference)
    {
        if (_referenceResolver == null)
            throw new PdfParseException("Cannot resolve indirect reference for stream length: no reference resolver set");

        PdfObject? resolvedObj = _referenceResolver(reference);
        return resolvedObj is PdfInteger resolvedInt
            ? resolvedInt.Value
            : throw new PdfParseException($"Resolved stream length is not an integer: {resolvedObj?.Type}");
    }

    private PdfStream ParseStream(PdfDictionary dictionary)
    {
        NextToken(); // Consume a 'stream' keyword

        // Get stream length from the dictionary
        if (!dictionary.TryGetValue(PdfName.Length, out PdfObject lengthObj))
            throw new PdfParseException("Stream dictionary missing Length entry");

        int length = lengthObj switch
        {
            PdfInteger lengthInt => lengthInt.Value,
            PdfIndirectReference lengthRef => ResolveStreamLength(lengthRef),
            _ => throw new PdfParseException($"Invalid stream length type: {lengthObj.Type}")
        };

        // Skip EOL after a 'stream' keyword (ISO 32000-1 section 7.3.8.1)
        _lexer.SkipEOL();

        // Read stream data
        byte[] data = _lexer.ReadBytes(length);

        // Skip EOL after stream data before 'endstream' (ISO 32000-1 section 7.3.8.1)
        // Per spec: "There should be an end-of-line marker after the data and before
        // endstream; this marker shall not be included in the stream length."
        _lexer.SkipEOL();

        ExpectToken(PdfTokenType.EndStream);
        return new PdfStream(dictionary, data);
    }

    #endregion
}

/// <summary>
/// Exception thrown when PDF parsing fails
/// </summary>
public class PdfParseException : Exception
{
    public PdfParseException(string message) : base(message)
    {
    }

    public PdfParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
