using System.Text;
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
    /// Automatically loads objects on-demand (both compressed and uncompressed)
    /// </summary>
    public PdfObject? GetObject(int objectNumber)
    {
        // Check if already loaded
        if (_objects.TryGetValue(objectNumber, out PdfObject? cachedObj))
            return cachedObj;

        // Check if this object exists in the xref table
        PdfXrefEntry? entry = XrefTable.Entries.FirstOrDefault(e => e.ObjectNumber == objectNumber);
        if (entry is not { IsInUse: true })
            return null;

        // Load compressed objects from object streams
        if (entry.EntryType == PdfXrefEntryType.Compressed)
        {
            LoadCompressedObject(entry);
            return _objects.GetValueOrDefault(objectNumber);
        }

        // Load uncompressed objects on-demand
        // This is needed when one object references another that hasn't been loaded yet
        // (e.g., a stream with /Length pointing to an indirect object)
        if (_stream != null && entry.EntryType == PdfXrefEntryType.Uncompressed)
        {
            try
            {
                // Save the current stream position
                long savedPosition = _stream.Position;

                // Seek to object position
                _stream.Position = entry.ByteOffset;

                // Create a new parser for this object
                var parser = new PdfParser(_stream);

                // Set the reference resolver so this object can also resolve its dependencies
                parser.SetReferenceResolver(reference => GetObject(reference.ObjectNumber));

                // Read the object
                PdfObject? obj = parser.ReadObject();
                if (obj != null)
                {
                    AddObject(entry.ObjectNumber, entry.GenerationNumber, obj);
                }

                // Restore stream position
                _stream.Position = savedPosition;

                return obj;
            }
            catch (Exception ex)
            {
                throw new PdfParseException(
                    $"Error loading object {objectNumber} on-demand at offset {entry.ByteOffset}: {ex.Message}",
                    ex);
            }
        }

        return null;
    }

    /// <summary>
    /// Loads a compressed object from its object stream
    /// ISO 32000-1 section 7.5.7: Object streams contain compressed objects
    /// </summary>
    private void LoadCompressedObject(PdfXrefEntry entry)
    {
        // For type 2 entries:
        // - ByteOffset = object stream number
        // - GenerationNumber = index within the object stream
        var objectStreamNumber = (int)entry.ByteOffset;
        int indexInStream = entry.GenerationNumber;

        // Get the object stream (this will recursively call GetObject, but object streams
        // themselves must be uncompressed, so no infinite recursion)
        PdfObject? objStreamObj = GetObject(objectStreamNumber);
        if (objStreamObj is not PdfStream objStream)
            throw new PdfParseException($"Object stream {objectStreamNumber} not found or not a stream");

        // Verify this is an object stream
        if (!objStream.Dictionary.TryGetValue(new PdfName("Type"), out PdfObject typeObj) ||
            typeObj is not PdfName { Value: "ObjStm" })
        {
            throw new PdfParseException($"Object {objectStreamNumber} is not an object stream (/Type /ObjStm missing)");
        }

        // Extract all objects from this stream (not just the one we need)
        // This is more efficient as we'll likely need other objects from the same stream
        ExtractObjectsFromStream(objStream);
    }

    /// <summary>
    /// Extracts all compressed objects from an object stream
    /// Object stream format (ISO 32000-1 section 7.5.7):
    /// - First section: N pairs of (objectNumber byteOffset)
    /// - Second section: The actual object data starting at /First offset
    /// </summary>
    private void ExtractObjectsFromStream(PdfStream objStream)
    {
        // Get /N (number of compressed objects)
        if (!objStream.Dictionary.TryGetValue(new PdfName("N"), out PdfObject nObj) ||
            nObj is not PdfInteger nInt)
        {
            throw new PdfParseException("Object stream missing /N entry");
        }
        int objectCount = nInt.Value;

        // Get /First (offset to first object's data)
        if (!objStream.Dictionary.TryGetValue(new PdfName("First"), out PdfObject firstObj) ||
            firstObj is not PdfInteger firstInt)
        {
            throw new PdfParseException("Object stream missing /First entry");
        }
        int firstOffset = firstInt.Value;

        // Decode the stream
        byte[] decodedData = objStream.GetDecodedData();

        // Parse the first section: pairs of (objectNumber offset)
        using var headerStream = new MemoryStream(decodedData, 0, firstOffset);
        var headerParser = new PdfParser(headerStream);

        var objectNumbers = new List<int>();
        var offsets = new List<int>();

        for (var i = 0; i < objectCount; i++)
        {
            // Read object number
            PdfObject? objNumObj = headerParser.ReadObject();
            if (objNumObj is not PdfInteger objNumInt)
                throw new PdfParseException($"Invalid object number in object stream header at index {i}");
            objectNumbers.Add(objNumInt.Value);

            // Read offset
            PdfObject? offsetObj = headerParser.ReadObject();
            if (offsetObj is not PdfInteger offsetInt)
                throw new PdfParseException($"Invalid offset in object stream header at index {i}");
            offsets.Add(offsetInt.Value);
        }

        // Parse the second section: actual objects
        for (var i = 0; i < objectCount; i++)
        {
            int objectNumber = objectNumbers[i];
            int offset = offsets[i] + firstOffset;

            // Determine the length of this object's data
            int length;
            if (i + 1 < objectCount)
            {
                // Length is up to the next object
                length = (offsets[i + 1] + firstOffset) - offset;
            }
            else
            {
                // Last object - goes to end of stream
                length = decodedData.Length - offset;
            }

            // Parse the object
            using var objectStream = new MemoryStream(decodedData, offset, length);
            var objectParser = new PdfParser(objectStream);
            objectParser.SetReferenceResolver(reference => GetObject(reference.ObjectNumber));

            PdfObject? obj = objectParser.ReadObject();
            if (obj != null)
            {
                // Add to cache (generation number is 0 for compressed objects)
                AddObject(objectNumber, 0, obj);
            }
        }
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
        return catalog is not PdfDictionary catalogDict
            ? null
            : new PdfCatalog(catalogDict, this);
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
        return Load(stream, leaveOpen: false);  // We own this stream, so don't leave it open
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

            // Parse cross-reference table(s) - follow /Prev chain for incremental updates
            long xrefPosition = actualXrefPosition;
            var xrefChainDepth = 0;
            const int maxXrefChainDepth = 100; // Prevent infinite loops

            while (xrefPosition >= 0 && xrefChainDepth < maxXrefChainDepth)
            {
                stream.Position = xrefPosition;
                var xrefParser = new PdfXrefParser(stream, document);
                PdfXrefParseResult xrefResult = xrefParser.Parse();

                // Add entries to document xref table
                // Note: Later entries (higher chain depth) take precedence over earlier ones
                // for the same object number (incremental updates)
                foreach (PdfXrefEntry entry in xrefResult.Table.Entries)
                {
                    // Check if this object already exists in the table
                    if (document.XrefTable.Entries.All(e => e.ObjectNumber != entry.ObjectNumber))
                    {
                        document.XrefTable.Add(entry);
                    }
                }

                // Save trailer from the most recent xref (first one in chain)
                // Also get the current trailer for checking /Prev
                PdfDictionary? currentTrailer = null;

                if (xrefChainDepth == 0)
                {
                    document.Trailer.Dictionary.Clear();

                    if (xrefResult is { IsXRefStream: true, TrailerDictionary: not null })
                    {
                        // Cross-reference stream - trailer is embedded in the stream dictionary
                        foreach (KeyValuePair<PdfName, PdfObject> kvp in xrefResult.TrailerDictionary)
                        {
                            document.Trailer.Dictionary[kvp.Key] = kvp.Value;
                        }
                        currentTrailer = xrefResult.TrailerDictionary;
                    }
                    else
                    {
                        // Traditional xref table - trailer follows separately
                        var trailerParser = new PdfTrailerParser(stream);
                        (PdfTrailer trailer, _) = trailerParser.Parse();

                        foreach (KeyValuePair<PdfName, PdfObject> kvp in trailer.Dictionary)
                        {
                            document.Trailer.Dictionary[kvp.Key] = kvp.Value;
                        }
                        currentTrailer = trailer.Dictionary;
                    }
                }
                else
                {
                    // For subsequent xrefs in the chain, get the trailer to check for /Prev
                    if (xrefResult is { IsXRefStream: true, TrailerDictionary: not null })
                    {
                        currentTrailer = xrefResult.TrailerDictionary;
                    }
                    else if (!xrefResult.IsXRefStream)
                    {
                        // For traditional xref, read the trailer
                        var trailerParser = new PdfTrailerParser(stream);
                        (PdfTrailer trailer, _) = trailerParser.Parse();
                        currentTrailer = trailer.Dictionary;
                    }
                }

                long prevXrefPosition = -1;
                if (currentTrailer != null &&
                    currentTrailer.TryGetValue(new PdfName("Prev"), out PdfObject prevObj) &&
                    prevObj is PdfInteger prevInt)
                {
                    prevXrefPosition = prevInt.Value + headerOffset;
                }

                xrefPosition = prevXrefPosition;
                xrefChainDepth++;
            }

            // Load uncompressed indirect objects
            // Compressed objects (type 2) will be loaded on-demand from object streams
            foreach (PdfXrefEntry entry in document.XrefTable.Entries)
            {
                if (!entry.IsInUse)
                    continue;

                // Skip compressed objects - they'll be loaded from object streams on-demand
                if (entry.EntryType == PdfXrefEntryType.Compressed)
                    continue;

                try
                {
                    // Seek to object position (adjust for header offset)
                    stream.Position = entry.ByteOffset + headerOffset;

                    // Create a new parser for each object to ensure the lexer buffer is synchronized
                    var parser = new PdfParser(stream);

                    // Set the reference resolver so streams can resolve indirect Length references
                    parser.SetReferenceResolver(reference => document.GetObject(reference.ObjectNumber));

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
        var buffer = new byte[Math.Min(1024, stream.Length)];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        // Search for %PDF- marker
        const string pdfMarker = "%PDF-";
        int headerPosition = -1;

        for (var i = 0; i <= bytesRead - pdfMarker.Length; i++)
        {
            bool found = !pdfMarker.Where((t, j) => buffer[i + j] != (byte)t).Any();

            if (!found) continue;
            headerPosition = i;
            break;
        }

        if (headerPosition == -1)
            throw new PdfParseException("Invalid PDF: missing %PDF- header in first 1024 bytes");

        // Extract the version line (read until CR, LF, or end of buffer)
        int lineEnd = headerPosition;
        while (lineEnd < bytesRead && buffer[lineEnd] != 0x0A && buffer[lineEnd] != 0x0D)
            lineEnd++;

        string headerLine = Encoding.ASCII.GetString(buffer, headerPosition, lineEnd - headerPosition);

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
