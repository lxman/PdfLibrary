using System.Text;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Parsing;

/// <summary>
/// Parses PDF cross-reference tables (ISO 32000-1:2008 section 7.5.4)
/// and cross-reference streams (ISO 32000-1:2008 section 7.5.8)
/// </summary>
internal class PdfXrefParser
{
    private readonly Stream _stream;

    // Note: The document parameter is kept for API compatibility but is no longer used.
    // We intentionally do NOT resolve references during xref parsing because the xref table
    // is still being built at that point.
    public PdfXrefParser(Stream stream, PdfDocument? document = null)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        // document parameter intentionally ignored - see note above
    }

    /// <summary>
    /// Parses a cross-reference table or stream starting at the current stream position
    /// Returns the xref table and trailer dictionary (if available)
    /// </summary>
    public PdfXrefParseResult Parse()
    {
        // Peek at the first line to determine format
        long startPosition = _stream.Position;
        string? firstLine = ReadLine();
        _stream.Position = startPosition;

        if (string.IsNullOrEmpty(firstLine))
            throw new PdfParseException("Empty xref section");

        // Check if this is a cross-reference stream (starts with object number)
        // or traditional xref table (starts with "xref" keyword)
        if (char.IsDigit(firstLine.TrimStart()[0]))
        {
            // Cross-reference stream (PDF 1.5+)
            return ParseXRefStream();
        }

        // Traditional xref table (trailer comes separately)
        PdfXrefTable table = ParseTraditionalXRef();
        return new PdfXrefParseResult(table, null, false);
    }

    /// <summary>
    /// Parses a traditional cross-reference table
    /// </summary>
    private PdfXrefTable ParseTraditionalXRef()
    {
        var table = new PdfXrefTable();

        // Read "xref" keyword
        string? keyword = ReadLine();
        if (keyword?.Trim() != "xref")
            throw new PdfParseException($"Expected 'xref' keyword, got: {keyword}");

        // Read subsections
        while (true)
        {
            long position = _stream.Position;
            string? line = ReadLine();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Check if we've reached the trailer
            if (line.Trim() == "trailer")
            {
                // Move back before "trailer" keyword
                _stream.Position = position;
                break;
            }

            // Parse subsection header (firstObjectNumber count)
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                throw new PdfParseException($"Invalid xref subsection header: {line}");

            if (!int.TryParse(parts[0], out int firstObjectNumber))
                throw new PdfParseException($"Invalid first object number: {parts[0]}");

            if (!int.TryParse(parts[1], out int count))
                throw new PdfParseException($"Invalid entry count: {parts[1]}");

            // Read entries for this subsection
            for (var i = 0; i < count; i++)
            {
                int objectNumber = firstObjectNumber + i;
                PdfXrefEntry entry = ParseEntry(objectNumber);
                table.Add(entry);
            }
        }

        return table;
    }

    /// <summary>
    /// Parses a cross-reference stream (PDF 1.5+)
    /// ISO 32000-1:2008 section 7.5.8
    /// </summary>
    private PdfXrefParseResult ParseXRefStream()
    {
        var table = new PdfXrefTable();

        // Parse the XRef stream object
        // NOTE: Do NOT set up a reference resolver here! We're in the middle of building
        // the xref table, so GetObject() would try to look up objects in an incomplete table.
        // The xref stream dictionary only needs direct values (/W, /Size, /Index, /Prev are
        // all direct integers/arrays). Indirect references like /Root, /Info are resolved later.
        var parser = new PdfParser(_stream);

        PdfObject? obj = parser.ReadObject();

        if (obj is not PdfStream xrefStream)
            throw new PdfParseException("Expected XRef stream object");

        // Verify this is a cross-reference stream
        if (!xrefStream.Dictionary.TryGetValue(new PdfName("Type"), out PdfObject typeObj) ||
            typeObj is not PdfName { Value: "XRef" })
        {
            throw new PdfParseException("Stream is not a cross-reference stream (/Type /XRef missing)");
        }

        // Get /W array - specifies field widths [type, field2, field3]
        if (!xrefStream.Dictionary.TryGetValue(new PdfName("W"), out PdfObject wObj) ||
            wObj is not PdfArray { Count: 3 } wArray)
        {
            throw new PdfParseException("XRef stream missing or invalid /W array");
        }

        var fieldWidths = new int[3];
        for (var i = 0; i < 3; i++)
        {
            if (wArray[i] is PdfInteger wInt)
                fieldWidths[i] = wInt.Value;
            else
                throw new PdfParseException($"Invalid /W array element at index {i}");
        }

        // Get /Index array (or default to [0, Size])
        int[] index;
        if (xrefStream.Dictionary.TryGetValue(new PdfName("Index"), out PdfObject indexObj) &&
            indexObj is PdfArray indexArray)
        {
            index = new int[indexArray.Count];
            for (var i = 0; i < indexArray.Count; i++)
            {
                if (indexArray[i] is PdfInteger indexInt)
                    index[i] = indexInt.Value;
                else
                    throw new PdfParseException($"Invalid /Index array element at index {i}");
            }
        }
        else
        {
            // Default: [0, Size]
            if (!xrefStream.Dictionary.TryGetValue(new PdfName("Size"), out PdfObject sizeObj) ||
                sizeObj is not PdfInteger size)
            {
                throw new PdfParseException("XRef stream missing /Size");
            }
            index = [0, size.Value];
        }

        // Decode the stream data
        byte[] decodedData = xrefStream.GetDecodedData();

        // Parse binary xref entries
        ParseXRefStreamEntries(table, decodedData, fieldWidths, index);

        // The XRef stream's dictionary contains the trailer information
        // (Root, Info, Size, etc. per ISO 32000-1 section 7.5.8)
        return new PdfXrefParseResult(table, xrefStream.Dictionary, true);
    }

    /// <summary>
    /// Parses binary cross-reference entries from decoded stream data
    /// </summary>
    private static void ParseXRefStreamEntries(PdfXrefTable table, byte[] data, int[] fieldWidths, int[] index)
    {
        int bytesPerEntry = fieldWidths[0] + fieldWidths[1] + fieldWidths[2];
        var dataOffset = 0;

        // Process each subsection specified in /Index
        for (var i = 0; i < index.Length; i += 2)
        {
            int firstObjectNumber = index[i];
            int count = index[i + 1];

            for (var j = 0; j < count; j++)
            {
                int objectNumber = firstObjectNumber + j;

                if (dataOffset + bytesPerEntry > data.Length)
                    throw new PdfParseException("XRef stream data truncated");

                // Read fields
                long field1 = ReadBigEndianInt(data, dataOffset, fieldWidths[0]);
                dataOffset += fieldWidths[0];

                long field2 = ReadBigEndianInt(data, dataOffset, fieldWidths[1]);
                dataOffset += fieldWidths[1];

                long field3 = ReadBigEndianInt(data, dataOffset, fieldWidths[2]);
                dataOffset += fieldWidths[2];

                // Determine entry type (default to 1 if not specified)
                int entryType = fieldWidths[0] == 0 ? 1 : (int)field1;

                PdfXrefEntry entry = entryType switch
                {
                    0 => new PdfXrefEntry(objectNumber, field2, (int)field3, false, PdfXrefEntryType.Free),
                    1 => new PdfXrefEntry(objectNumber, field2, (int)field3, true, PdfXrefEntryType.Uncompressed),
                    2 => new PdfXrefEntry(objectNumber, field2, (int)field3, true, PdfXrefEntryType.Compressed),
                    _ => throw new PdfParseException($"Invalid XRef entry type: {entryType}")
                };

                table.Add(entry);
            }
        }
    }

    /// <summary>
    /// Reads a big-endian integer from a byte array
    /// </summary>
    private static long ReadBigEndianInt(byte[] data, int offset, int length)
    {
        long value = 0;
        for (var i = 0; i < length; i++)
        {
            value = (value << 8) | data[offset + i];
        }
        return value;
    }

    /// <summary>
    /// Parses a single cross-reference entry
    /// Format: nnnnnnnnnn ggggg (n|f)
    /// </summary>
    private PdfXrefEntry ParseEntry(int objectNumber)
    {
        string? line = ReadLine();
        if (string.IsNullOrEmpty(line))
            throw new PdfParseException("Unexpected end of xref table");

        // Remove extra whitespace but preserve structure
        line = line.Trim();

        // Entry format: 10 digits, space, 5 digits, space, (n|f)
        if (line.Length < 18)
            throw new PdfParseException($"Invalid xref entry format: {line}");

        // Parse byte offset (or next free object number)
        string offsetStr = line[..10].Trim();
        if (!long.TryParse(offsetStr, out long byteOffset))
            throw new PdfParseException($"Invalid byte offset in xref entry: {offsetStr}");

        // Parse generation number
        string genStr = line.Substring(11, 5).Trim();
        if (!int.TryParse(genStr, out int generationNumber))
            throw new PdfParseException($"Invalid generation number in xref entry: {genStr}");

        // Parse in-use flag
        char flag = line.Length >= 18 ? line[17] : ' ';
        bool isInUse = flag switch
        {
            'n' => true,
            'f' => false,
            _ => throw new PdfParseException($"Invalid xref entry flag: {flag} (expected 'n' or 'f')")
        };

        return new PdfXrefEntry(objectNumber, byteOffset, generationNumber, isInUse);
    }

    /// <summary>
    /// Reads a line from the stream
    /// </summary>
    private string? ReadLine()
    {
        var sb = new StringBuilder();
        int b;

        while ((b = _stream.ReadByte()) != -1)
        {
            var ch = (char)b;

            // Handle line endings (CR, LF, or CRLF)
            if (ch == '\r')
            {
                // Peek ahead for LF
                int next = _stream.ReadByte();
                if (next != -1 && next != '\n')
                {
                    // Not CRLF, move back
                    _stream.Position--;
                }
                break;
            }

            if (ch == '\n')
                break;

            sb.Append(ch);
        }

        return sb.Length > 0 || b != -1 ? sb.ToString() : null;
    }
}
