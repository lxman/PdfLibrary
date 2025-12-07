using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Parsing;

/// <summary>
/// Parses PDF tokens into PDF objects (ISO 32000-1:2008 section 7.3)
/// </summary>
internal class PdfParser(PdfLexer lexer)
{
    private readonly PdfLexer _lexer = lexer ?? throw new ArgumentNullException(nameof(lexer));
    private readonly Queue<PdfToken> _tokenBuffer = new();
    private Func<PdfIndirectReference, PdfObject?>? _referenceResolver;
    private PdfLibrary.Security.PdfDecryptor? _decryptor;
    private int _currentObjectNumber = -1;
    private int _currentGenerationNumber = 0;

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
    /// Sets the decryptor for decrypting encrypted strings and streams
    /// </summary>
    public void SetDecryptor(PdfLibrary.Security.PdfDecryptor? decryptor)
    {
        _decryptor = decryptor;
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

        while ((obj = ReadObject()) is not null)
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
        // First try to parse as a long (supports large integers found in some PDFs)
        if (!long.TryParse(firstToken.Value, out long longValue))
            throw new PdfParseException($"Invalid integer: {firstToken.Value}");

        // Check if this could be an indirect reference (N G R) or indirect object (N G obj)
        // Object numbers must fit in int range
        bool couldBeObjectNumber = longValue is >= 0 and <= int.MaxValue;

        PdfToken peek1 = PeekToken();

        // If not followed by another integer, or if the value is too large for an object number,
        // it's just a plain integer
        if (peek1.Type != PdfTokenType.Integer || !couldBeObjectNumber)
            return new PdfInteger(longValue);

        var objectNumber = (int)longValue;
        PdfToken genToken = NextToken();
        if (!int.TryParse(genToken.Value, out int generationNumber))
            return new PdfInteger(objectNumber);

        PdfToken peek2 = PeekToken();

        switch (peek2.Type)
        {
            // Check for indirect reference (N G R)
            case PdfTokenType.R:
                NextToken(); // Consume R
                return new PdfIndirectReference(objectNumber, generationNumber);
            // Check for indirect object definition (N G obj)
            case PdfTokenType.Obj:
            {
                NextToken(); // Consume obj

                // Set current object context for string decryption
                int prevObjectNumber = _currentObjectNumber;
                int prevGenerationNumber = _currentGenerationNumber;
                _currentObjectNumber = objectNumber;
                _currentGenerationNumber = generationNumber;

                try
                {
                    // Handle empty objects (ISO 32000-1: empty object = null)
                    PdfToken contentPeek = PeekToken();
                    PdfObject? content;
                    if (contentPeek.Type == PdfTokenType.EndObj)
                    {
                        // Empty object - treat as null
                        content = PdfNull.Instance;
                    }
                    else
                    {
                        content = ReadObject();
                    }

                    ExpectToken(PdfTokenType.EndObj);

                    if (content is null) return PdfNull.Instance;
                    content.IsIndirect = true;
                    content.ObjectNumber = objectNumber;
                    content.GenerationNumber = generationNumber;

                    return content;
                }
                finally
                {
                    // Restore previous object context
                    _currentObjectNumber = prevObjectNumber;
                    _currentGenerationNumber = prevGenerationNumber;
                }
            }
            default:
                // Otherwise, we have two integers - push back and return the first
                PushBackToken(genToken);
                return new PdfInteger(objectNumber);
        }
    }

    private PdfString ParseString(PdfToken token)
    {
        // NOTE: The lexer (PdfLexer.ReadHexStringOrDictionaryStart) has already converted
        // hex strings like <36322F00...> to bytes and then to a Latin-1 string.
        // We should NOT try to re-parse as hex here - just extract the bytes back from Latin-1.
        // Otherwise, if all bytes happen to represent hex digit characters (0-9, A-F),
        // we would incorrectly re-parse and get wrong byte values (double-conversion bug).

        // Simply return the string as-is - the PdfString constructor will use Latin-1
        // to convert back to the original bytes that the lexer parsed from hex.
        var pdfString = new PdfString(token.Value);

        // If we're inside an indirect object and have a decryptor, decrypt the string
        // Per PDF spec ISO 32000-1 section 7.6.2: "All strings in the document are encrypted"
        if (_decryptor is not null && _currentObjectNumber >= 0)
        {
            byte[] encryptedBytes = pdfString.Bytes;
            byte[] decryptedBytes = _decryptor.Decrypt(encryptedBytes, _currentObjectNumber, _currentGenerationNumber);

            // Create new PdfString with decrypted bytes
            pdfString = new PdfString(decryptedBytes);

            Logging.PdfLogger.Log(Logging.LogCategory.PdfTool,
                $"DECRYPT STRING: obj {_currentObjectNumber} gen {_currentGenerationNumber}, " +
                $"encrypted=[{string.Join(" ", encryptedBytes.Take(10).Select(b => b.ToString("X2")))}...] " +
                $"decrypted=[{string.Join(" ", decryptedBytes.Take(10).Select(b => b.ToString("X2")))}...]");
        }

        // DIAGNOSTIC: Log PdfString creation for palette debugging
        // Log ALL long strings, not just ones with expected bytes
        if (token.Value.Length >= 50)
        {
            byte[] bytes = pdfString.Bytes;
            string bytesHex = string.Join(" ", bytes.Take(20).Select(b => b.ToString("X2")));
            Logging.PdfLogger.Log(Logging.LogCategory.PdfTool, $"PARSER STRING: created PdfString len={bytes.Length}, first 20 bytes=[{bytesHex}]");
        }

        return pdfString;
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
            if (element is not null)
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
            if (value is not null)
                dict[key] = value;
        }

        return dict;
    }

    /// <summary>
    /// Resolves an indirect reference to get the stream length
    /// </summary>
    private int ResolveStreamLength(PdfIndirectReference reference)
    {
        if (_referenceResolver is null)
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
internal class PdfParseException : Exception
{
    public PdfParseException(string message) : base(message)
    {
    }

    public PdfParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
