using System.Text;
using PdfLibrary.Structure;

namespace PdfLibrary.Parsing;

/// <summary>
/// Parses PDF cross-reference tables (ISO 32000-1:2008 section 7.5.4)
/// </summary>
public class PdfXrefParser(Stream stream)
{
    private readonly Stream _stream = stream ?? throw new ArgumentNullException(nameof(stream));

    /// <summary>
    /// Parses a cross-reference table starting at the current stream position
    /// </summary>
    public PdfXrefTable Parse()
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
        string offsetStr = line.Substring(0, 10).Trim();
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
