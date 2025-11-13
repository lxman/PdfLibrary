using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Parsing;

namespace PdfLibrary.Structure;

/// <summary>
/// Represents a complete PDF document (ISO 32000-1:2008 section 7.5)
/// Provides access to the document structure, objects, and metadata
/// </summary>
public class PdfDocument : IDisposable
{
    private readonly Dictionary<int, PdfObject> _objects = new();
    private Stream? _stream;
    private bool _disposed;

    /// <summary>
    /// Creates a new PDF document
    /// </summary>
    public PdfDocument()
    {
        Version = PdfVersion.Pdf17;
        XrefTable = new PdfXrefTable();
        Trailer = new PdfTrailer();
    }

    /// <summary>
    /// Gets or sets the PDF version
    /// </summary>
    public PdfVersion Version { get; set; }

    /// <summary>
    /// Gets the cross-reference table
    /// </summary>
    public PdfXrefTable XrefTable { get; }

    /// <summary>
    /// Gets the trailer dictionary
    /// </summary>
    public PdfTrailer Trailer { get; }

    /// <summary>
    /// Gets all indirect objects in the document
    /// </summary>
    public IReadOnlyDictionary<int, PdfObject> Objects => _objects;

    /// <summary>
    /// Adds an indirect object to the document
    /// </summary>
    public void AddObject(int objectNumber, int generationNumber, PdfObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        obj.IsIndirect = true;
        obj.ObjectNumber = objectNumber;
        obj.GenerationNumber = generationNumber;

        _objects[objectNumber] = obj;
    }

    /// <summary>
    /// Gets an object by its object number
    /// </summary>
    public PdfObject? GetObject(int objectNumber)
    {
        return _objects.GetValueOrDefault(objectNumber);
    }

    /// <summary>
    /// Resolves an indirect reference to its object
    /// </summary>
    public PdfObject? ResolveReference(PdfIndirectReference reference)
    {
        return reference == null
            ? throw new ArgumentNullException(nameof(reference))
            : GetObject(reference.ObjectNumber);
    }

    /// <summary>
    /// Gets the document catalog (Root object)
    /// </summary>
    public PdfCatalog? GetCatalog()
    {
        if (Trailer.Root == null)
            return null;

        PdfObject? catalog = ResolveReference(Trailer.Root);
        if (catalog is not PdfDictionary catalogDict)
            return null;

        return new PdfCatalog(catalogDict, this);
    }

    /// <summary>
    /// Gets the document information dictionary
    /// </summary>
    public PdfDictionary? GetInfo()
    {
        if (Trailer.Info == null)
            return null;

        PdfObject? info = ResolveReference(Trailer.Info);
        return info as PdfDictionary;
    }

    /// <summary>
    /// Gets the page count
    /// </summary>
    public int GetPageCount()
    {
        PdfCatalog? catalog = GetCatalog();
        if (catalog == null)
            return 0;

        PdfPageTree? pageTree = catalog.GetPageTree();
        return pageTree?.Count ?? 0;
    }

    /// <summary>
    /// Gets all pages in the document
    /// </summary>
    public List<PdfPage> GetPages()
    {
        PdfCatalog? catalog = GetCatalog();
        if (catalog == null)
            return [];

        PdfPageTree? pageTree = catalog.GetPageTree();
        return pageTree?.GetPages() ?? [];
    }

    /// <summary>
    /// Gets a specific page by index (0-based)
    /// </summary>
    public PdfPage? GetPage(int index)
    {
        PdfCatalog? catalog = GetCatalog();

        PdfPageTree? pageTree = catalog?.GetPageTree();
        return pageTree?.GetPage(index);
    }

    /// <summary>
    /// Loads a PDF document from a file
    /// </summary>
    public static PdfDocument Load(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}", filePath);

        FileStream stream = File.OpenRead(filePath);
        return Load(stream, true);
    }

    /// <summary>
    /// Loads a PDF document from a stream
    /// </summary>
    /// <param name="stream">Stream containing PDF data</param>
    /// <param name="leaveOpen">If false, the stream will be disposed when the document is disposed</param>
    public static PdfDocument Load(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(stream));

        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable", nameof(stream));

        var document = new PdfDocument();
        if (!leaveOpen)
            document._stream = stream;

        try
        {
            // Parse PDF header
            stream.Position = 0;
            (document.Version, long headerOffset) = ParseHeader(stream);

            // Find startxref position
            long startxref = PdfTrailerParser.FindStartXref(stream);

            // Adjust startxref by header offset (PDF 2.0 files may have data before the header)
            long actualXrefPosition = startxref + headerOffset;

            // Parse cross-reference table
            stream.Position = actualXrefPosition;
            var xrefParser = new PdfXrefParser(stream);
            PdfXrefTable xrefTable = xrefParser.Parse();

            foreach (PdfXrefEntry entry in xrefTable.Entries)
            {
                document.XrefTable.Add(entry);
            }

            // Parse trailer
            var trailerParser = new PdfTrailerParser(stream);
            (PdfTrailer trailer, _) = trailerParser.Parse();

            document.Trailer.Dictionary.Clear();
            foreach (KeyValuePair<PdfName, PdfObject> kvp in trailer.Dictionary)
            {
                document.Trailer.Dictionary[kvp.Key] = kvp.Value;
            }

            // Load all indirect objects
            foreach (PdfXrefEntry entry in document.XrefTable.Entries)
            {
                if (!entry.IsInUse)
                    continue;

                try
                {
                    // Seek to object position (adjust for header offset)
                    stream.Position = entry.ByteOffset + headerOffset;

                    // Create a new parser for each object to ensure the lexer buffer is synchronized
                    var parser = new PdfParser(stream);

                    // Read the indirect object
                    PdfObject? obj = parser.ReadObject();
                    if (obj != null)
                    {
                        document.AddObject(entry.ObjectNumber, entry.GenerationNumber, obj);
                    }
                }
                catch (PdfParseException ex)
                {
                    throw new PdfParseException(
                        $"Error parsing object {entry.ObjectNumber} at byte offset {entry.ByteOffset}: {ex.Message}",
                        ex);
                }
            }

            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parses the PDF header to determine the version and header offset
    /// ISO 32000-2:2020 allows the header to appear anywhere within the first 1024 bytes
    /// </summary>
    /// <returns>A tuple containing the PDF version and the byte offset of the header</returns>
    private static (PdfVersion version, long headerOffset) ParseHeader(Stream stream)
    {
        stream.Position = 0;

        // Read first 1024 bytes to search for PDF header
        byte[] buffer = new byte[Math.Min(1024, stream.Length)];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        // Search for %PDF- marker
        const string pdfMarker = "%PDF-";
        int headerPosition = -1;

        for (int i = 0; i <= bytesRead - pdfMarker.Length; i++)
        {
            bool found = true;
            for (int j = 0; j < pdfMarker.Length; j++)
            {
                if (buffer[i + j] != (byte)pdfMarker[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                headerPosition = i;
                break;
            }
        }

        if (headerPosition == -1)
            throw new PdfParseException("Invalid PDF: missing %PDF- header in first 1024 bytes");

        // Extract the version line (read until CR, LF, or end of buffer)
        int lineEnd = headerPosition;
        while (lineEnd < bytesRead && buffer[lineEnd] != 0x0A && buffer[lineEnd] != 0x0D)
            lineEnd++;

        string headerLine = System.Text.Encoding.ASCII.GetString(buffer, headerPosition, lineEnd - headerPosition);

        try
        {
            PdfVersion version = PdfVersion.Parse(headerLine);
            return (version, headerPosition);
        }
        catch (Exception ex)
        {
            throw new PdfParseException($"Invalid PDF version in header: {headerLine}", ex);
        }
    }

    /// <summary>
    /// Saves the document to a file
    /// </summary>
    public void Save(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        using FileStream stream = File.Create(filePath);
        Save(stream);
    }

    /// <summary>
    /// Saves the document to a stream
    /// </summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanWrite)
            throw new ArgumentException("Stream must be writable", nameof(stream));

        using var writer = new StreamWriter(stream, leaveOpen: true);

        // Write header
        writer.WriteLine($"%PDF-{Version}");
        writer.WriteLine("%âãÏÓ"); // Binary marker

        // TODO: Write body (all indirect objects)
        // TODO: Write cross-reference table
        // TODO: Write trailer

        writer.Flush();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _stream?.Dispose();
        _disposed = true;
    }
}
