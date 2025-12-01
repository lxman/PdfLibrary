using System.Diagnostics;
using System.Text;
using Logging;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Parsing;
using PdfLibrary.Security;

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
    internal PdfXrefTable XrefTable { get; }

    /// <summary>
    /// Gets the trailer dictionary
    /// </summary>
    internal PdfTrailer Trailer { get; }

    /// <summary>
    /// Gets all indirect objects in the document
    /// </summary>
    internal IReadOnlyDictionary<int, PdfObject> Objects => _objects;

    /// <summary>
    /// Gets the decryptor for encrypted documents, or null if not encrypted.
    /// </summary>
    internal PdfDecryptor? Decryptor { get; private set; }

    /// <summary>
    /// Gets whether the document is encrypted.
    /// </summary>
    public bool IsEncrypted => Decryptor is not null;

    /// <summary>
    /// Gets the document permissions for encrypted documents.
    /// Returns full permissions for unencrypted documents.
    /// </summary>
    public PdfPermissions Permissions => Decryptor?.Permissions ?? PdfPermissions.AllPermissions;

    /// <summary>
    /// Adds an indirect object to the document
    /// </summary>
    internal void AddObject(int objectNumber, int generationNumber, PdfObject obj)
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
    internal PdfObject? GetObject(int objectNumber)
    {
        // Check if already loaded
        if (_objects.TryGetValue(objectNumber, out var cachedObj))
            return cachedObj;

        // Check if this object exists in the xref table
        var entry = XrefTable.Entries.FirstOrDefault(e => e.ObjectNumber == objectNumber);
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
        if (_stream is not null && entry.EntryType == PdfXrefEntryType.Uncompressed)
        {
            try
            {
                // Save the current stream position
                var savedPosition = _stream.Position;

                // Seek to object position
                _stream.Position = entry.ByteOffset;

                // Create a new parser for this object
                var parser = new PdfParser(_stream);

                // Set the reference resolver so this object can also resolve its dependencies
                parser.SetReferenceResolver(reference => GetObject(reference.ObjectNumber));

                // Read the object
                var obj = parser.ReadObject();
                if (obj is not null)
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
        var indexInStream = entry.GenerationNumber;

        // Get the object stream (this will recursively call GetObject, but object streams
        // themselves must be uncompressed, so no infinite recursion)
        var objStreamObj = GetObject(objectStreamNumber);
        if (objStreamObj is not PdfStream objStream)
            throw new PdfParseException($"Object stream {objectStreamNumber} not found or not a stream");

        // Verify this is an object stream
        if (!objStream.Dictionary.TryGetValue(new PdfName("Type"), out var typeObj) ||
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
        if (!objStream.Dictionary.TryGetValue(new PdfName("N"), out var nObj) ||
            nObj is not PdfInteger nInt)
        {
            throw new PdfParseException("Object stream missing /N entry");
        }
        var objectCount = nInt.Value;

        // Get /First (offset to first object's data)
        if (!objStream.Dictionary.TryGetValue(new PdfName("First"), out var firstObj) ||
            firstObj is not PdfInteger firstInt)
        {
            throw new PdfParseException("Object stream missing /First entry");
        }
        var firstOffset = firstInt.Value;

        // Decode the stream (decryption is needed for encrypted documents)
        var decodedData = objStream.GetDecodedData(Decryptor);

        // Parse the first section: pairs of (objectNumber offset)
        using var headerStream = new MemoryStream(decodedData, 0, firstOffset);
        var headerParser = new PdfParser(headerStream);

        var objectNumbers = new List<int>();
        var offsets = new List<int>();

        for (var i = 0; i < objectCount; i++)
        {
            // Read object number
            var objNumObj = headerParser.ReadObject();
            if (objNumObj is not PdfInteger objNumInt)
                throw new PdfParseException($"Invalid object number in object stream header at index {i}");
            objectNumbers.Add(objNumInt.Value);

            // Read offset
            var offsetObj = headerParser.ReadObject();
            if (offsetObj is not PdfInteger offsetInt)
                throw new PdfParseException($"Invalid offset in object stream header at index {i}");
            offsets.Add(offsetInt.Value);
        }

        // Parse the second section: actual objects
        for (var i = 0; i < objectCount; i++)
        {
            var objectNumber = objectNumbers[i];
            var offset = offsets[i] + firstOffset;

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

            var obj = objectParser.ReadObject();
            if (obj is not null)
            {
                // Add to cache (generation number is 0 for compressed objects)
                AddObject(objectNumber, 0, obj);
            }
        }
    }

    /// <summary>
    /// Resolves an indirect reference to its object
    /// </summary>
    internal PdfObject? ResolveReference(PdfIndirectReference reference)
    {
        return reference is null
            ? throw new ArgumentNullException(nameof(reference))
            : GetObject(reference.ObjectNumber);
    }

    /// <summary>
    /// Gets the document catalog (Root object)
    /// </summary>
    internal PdfCatalog? GetCatalog()
    {
        if (Trailer.Root is null)
            return null;

        var catalog = ResolveReference(Trailer.Root);
        return catalog is not PdfDictionary catalogDict
            ? null
            : new PdfCatalog(catalogDict, this);
    }

    /// <summary>
    /// Gets the document information dictionary
    /// </summary>
    internal PdfDictionary? GetInfo()
    {
        if (Trailer.Info is null)
            return null;

        var info = ResolveReference(Trailer.Info);
        return info as PdfDictionary;
    }

    /// <summary>
    /// Gets the page count
    /// </summary>
    public int GetPageCount()
    {
        var catalog = GetCatalog();
        if (catalog is null)
            return 0;

        var pageTree = catalog.GetPageTree();
        return pageTree?.Count ?? 0;
    }

    /// <summary>
    /// Gets all pages in the document
    /// </summary>
    public List<PdfPage> GetPages()
    {
        var catalog = GetCatalog();
        if (catalog is null)
            return [];

        var pageTree = catalog.GetPageTree();
        return pageTree?.GetPages() ?? [];
    }

    /// <summary>
    /// Gets a specific page by index (0-based)
    /// </summary>
    public PdfPage? GetPage(int index)
    {
        var catalog = GetCatalog();

        var pageTree = catalog?.GetPageTree();
        return pageTree?.GetPage(index);
    }

    /// <summary>
    /// Gets a range of pages (0-based indices, inclusive)
    /// </summary>
    /// <param name="startIndex">Starting page index (0-based)</param>
    /// <param name="endIndex">Ending page index (0-based, inclusive)</param>
    /// <returns>List of pages in the range</returns>
    public List<PdfPage> GetPages(int startIndex, int endIndex)
    {
        var allPages = GetPages();
        if (allPages.Count == 0)
            return [];

        startIndex = Math.Max(0, startIndex);
        endIndex = Math.Min(allPages.Count - 1, endIndex);

        if (startIndex > endIndex)
            return [];

        return allPages.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
    }

    /// <summary>
    /// Gets the first page of the document
    /// </summary>
    public PdfPage? FirstPage => GetPage(0);

    /// <summary>
    /// Gets the last page of the document
    /// </summary>
    public PdfPage? LastPage
    {
        get
        {
            var count = GetPageCount();
            return count > 0 ? GetPage(count - 1) : null;
        }
    }

    /// <summary>
    /// Gets the total number of pages
    /// </summary>
    public int PageCount => GetPageCount();

    /// <summary>
    /// Provides enumerable access to all pages
    /// </summary>
    public IEnumerable<PdfPage> Pages => GetPages();

    /// <summary>
    /// Extracts all text from all pages in the document
    /// </summary>
    /// <param name="separator">Separator to insert between pages (default: double newline)</param>
    /// <returns>All extracted text concatenated</returns>
    public string ExtractAllText(string separator = "\n\n")
    {
        var pages = GetPages();
        if (pages.Count == 0)
            return string.Empty;

        var texts = new List<string>();
        foreach (var page in pages)
        {
            var text = page.ExtractText();
            if (!string.IsNullOrWhiteSpace(text))
                texts.Add(text);
        }

        return string.Join(separator, texts);
    }

    /// <summary>
    /// Gets all images from all pages in the document
    /// </summary>
    /// <returns>List of all images with their page numbers</returns>
    internal List<(PdfImage Image, int PageNumber)> GetAllImages()
    {
        var result = new List<(PdfImage, int)>();
        var pages = GetPages();

        for (var i = 0; i < pages.Count; i++)
        {
            var pageImages = pages[i].GetImages();
            foreach (var image in pageImages)
            {
                result.Add((image, i + 1)); // 1-based page number
            }
        }

        return result;
    }

    /// <summary>
    /// Loads a PDF document from a file
    /// </summary>
    public static PdfDocument Load(string filePath)
    {
        return Load(filePath, password: "");
    }

    /// <summary>
    /// Loads a PDF document from a file with a password
    /// </summary>
    /// <param name="filePath">Path to the PDF file</param>
    /// <param name="password">Password for encrypted documents (empty string for no password)</param>
    public static PdfDocument Load(string filePath, string password)
    {
        if (string.IsNullOrEmpty(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"PDF file not found: {filePath}", filePath);

        var stream = File.OpenRead(filePath);
        return Load(stream, password, leaveOpen: false);  // We own this stream, so don't leave it open
    }

    /// <summary>
    /// Loads a PDF document from a stream
    /// </summary>
    /// <param name="stream">Stream containing PDF data</param>
    /// <param name="leaveOpen">If false, the stream will be disposed when the document is disposed</param>
    public static PdfDocument Load(Stream stream, bool leaveOpen = false)
    {
        return Load(stream, password: "", leaveOpen);
    }

    /// <summary>
    /// Loads a PDF document from a stream with a password
    /// </summary>
    /// <param name="stream">Stream containing PDF data</param>
    /// <param name="password">Password for encrypted documents (empty string for no password)</param>
    /// <param name="leaveOpen">If false, the stream will be disposed when the document is disposed</param>
    public static PdfDocument Load(Stream stream, string password, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable", nameof(stream));

        if (!stream.CanSeek)
            throw new ArgumentException("Stream must be seekable", nameof(stream));

        var totalStopwatch = Stopwatch.StartNew();
        var phaseStopwatch = new Stopwatch();

        var document = new PdfDocument();
        if (!leaveOpen)
            document._stream = stream;

        try
        {
            // Parse PDF header
            phaseStopwatch.Restart();
            stream.Position = 0;
            (document.Version, var headerOffset) = ParseHeader(stream);
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Header parsed in {phaseStopwatch.ElapsedMilliseconds}ms");

            // Find startxref position
            phaseStopwatch.Restart();
            var startxref = PdfTrailerParser.FindStartXref(stream);
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Found startxref in {phaseStopwatch.ElapsedMilliseconds}ms");

            // Adjust startxref by header offset (PDF 2.0 files may have data before the header)
            var actualXrefPosition = startxref + headerOffset;

            // Parse cross-reference table(s) - follow /Prev chain for incremental updates
            phaseStopwatch.Restart();
            var xrefPosition = actualXrefPosition;
            var xrefChainDepth = 0;
            const int maxXrefChainDepth = 100; // Prevent infinite loops

            while (xrefPosition >= 0 && xrefChainDepth < maxXrefChainDepth)
            {
                stream.Position = xrefPosition;
                var xrefParser = new PdfXrefParser(stream, document);
                var xrefResult = xrefParser.Parse();

                // Add entries to document xref table
                // Note: Later entries (higher chain depth) take precedence over earlier ones
                // for the same object number (incremental updates)
                foreach (var entry in xrefResult.Table.Entries)
                {
                    // Check if this object already exists in the table (O(1) dictionary lookup)
                    if (!document.XrefTable.Contains(entry.ObjectNumber))
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
                        foreach (var kvp in xrefResult.TrailerDictionary)
                        {
                            document.Trailer.Dictionary[kvp.Key] = kvp.Value;
                        }
                        currentTrailer = xrefResult.TrailerDictionary;
                    }
                    else
                    {
                        // Traditional xref table - trailer follows separately
                        var trailerParser = new PdfTrailerParser(stream);
                        (var trailer, _) = trailerParser.Parse();

                        foreach (var kvp in trailer.Dictionary)
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
                        (var trailer, _) = trailerParser.Parse();
                        currentTrailer = trailer.Dictionary;
                    }
                }

                long prevXrefPosition = -1;
                if (currentTrailer is not null &&
                    currentTrailer.TryGetValue(new PdfName("Prev"), out var prevObj) &&
                    prevObj is PdfInteger prevInt)
                {
                    prevXrefPosition = prevInt.Value + headerOffset;
                }

                xrefPosition = prevXrefPosition;
                xrefChainDepth++;
            }

            var xrefEntryCount = document.XrefTable.Entries.Count;
            var compressedCount = document.XrefTable.Entries.Count(e => e.EntryType == PdfXrefEntryType.Compressed);
            var uncompressedCount = document.XrefTable.Entries.Count(e => e.IsInUse && e.EntryType != PdfXrefEntryType.Compressed);
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Xref parsed in {phaseStopwatch.ElapsedMilliseconds}ms ({xrefEntryCount} entries: {uncompressedCount} uncompressed, {compressedCount} compressed, chain depth {xrefChainDepth})");

            // Check for encryption and initialize decryptor
            if (document.Trailer.Encrypt is not null)
            {
                phaseStopwatch.Restart();
                InitializeDecryption(document, stream, headerOffset, password);
                PdfLogger.Log(LogCategory.Timings, $"PDF Load: Encryption initialized in {phaseStopwatch.ElapsedMilliseconds}ms (method: {document.Decryptor?.Method})");
            }

            // Load uncompressed indirect objects
            // Compressed objects (type 2) will be loaded on-demand from object streams
            phaseStopwatch.Restart();
            var loadedObjects = 0;
            foreach (var entry in document.XrefTable.Entries)
            {
                if (!entry.IsInUse)
                    continue;

                // Skip compressed objects - they'll be loaded from object streams on-demand
                if (entry.EntryType == PdfXrefEntryType.Compressed)
                    continue;

                try
                {
                    // Seek to object position (adjust for header offset)
                    var targetPosition = entry.ByteOffset + headerOffset;
                    stream.Position = targetPosition;

                    // Try to find the object header, with error recovery for malformed xref offsets
                    // Some PDFs have xref entries that point a few bytes before the actual object
                    var actualPosition = FindObjectHeader(stream, entry.ObjectNumber, entry.GenerationNumber, targetPosition);
                    if (actualPosition is null)
                    {
                        PdfLogger.Log(LogCategory.Melville, $"Could not find object {entry.ObjectNumber} {entry.GenerationNumber} at or near offset {entry.ByteOffset}");
                        continue;
                    }

                    stream.Position = actualPosition.Value;

                    // Create a new parser for each object to ensure the lexer buffer is synchronized
                    var parser = new PdfParser(stream);

                    // Set the reference resolver so streams can resolve indirect Length references
                    parser.SetReferenceResolver(reference => document.GetObject(reference.ObjectNumber));

                    // Read the indirect object
                    var obj = parser.ReadObject();
                    if (obj is not null)
                    {
                        document.AddObject(entry.ObjectNumber, entry.GenerationNumber, obj);
                        loadedObjects++;
                    }
                }
                catch (PdfParseException ex)
                {
                    throw new PdfParseException(
                        $"Error parsing object {entry.ObjectNumber} at byte offset {entry.ByteOffset}: {ex.Message}",
                        ex);
                }
            }
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Loaded {loadedObjects} uncompressed objects in {phaseStopwatch.ElapsedMilliseconds}ms");

            totalStopwatch.Stop();
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Total load time {totalStopwatch.ElapsedMilliseconds}ms (file size: {stream.Length / 1024}KB)");

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
        var bytesRead = stream.Read(buffer, 0, buffer.Length);

        // Search for %PDF- marker
        const string pdfMarker = "%PDF-";
        var headerPosition = -1;

        for (var i = 0; i <= bytesRead - pdfMarker.Length; i++)
        {
            var found = !pdfMarker.Where((t, j) => buffer[i + j] != (byte)t).Any();

            if (!found) continue;
            headerPosition = i;
            break;
        }

        if (headerPosition == -1)
            throw new PdfParseException("Invalid PDF: missing %PDF- header in first 1024 bytes");

        // Extract the version line (read until CR, LF, or end of buffer)
        var lineEnd = headerPosition;
        while (lineEnd < bytesRead && buffer[lineEnd] != 0x0A && buffer[lineEnd] != 0x0D)
            lineEnd++;

        var headerLine = Encoding.ASCII.GetString(buffer, headerPosition, lineEnd - headerPosition);

        try
        {
            var version = PdfVersion.Parse(headerLine);
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

        using var stream = File.Create(filePath);
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

    /// <summary>
    /// Finds the actual position of an object header, handling malformed xref offsets.
    /// Some PDFs have xref entries that point a few bytes before the actual "N G obj" header.
    /// This method scans forward up to 64 bytes to find the correct position.
    /// </summary>
    /// <param name="stream">The PDF stream positioned at the approximate object location</param>
    /// <param name="expectedObjectNumber">The expected object number from the xref</param>
    /// <param name="expectedGenerationNumber">The expected generation number from the xref</param>
    /// <param name="targetPosition">The original target position from xref</param>
    /// <returns>The actual position of the object header, or null if not found</returns>
    private static long? FindObjectHeader(Stream stream, int expectedObjectNumber, int expectedGenerationNumber, long targetPosition)
    {
        const int maxScanBytes = 64;
        var buffer = new byte[maxScanBytes];

        // Read ahead to search for the object header pattern
        var bytesRead = stream.Read(buffer, 0, maxScanBytes);
        if (bytesRead == 0)
            return null;

        // Build the expected pattern: "N G obj"
        var expectedPattern = $"{expectedObjectNumber} {expectedGenerationNumber} obj";
        var patternBytes = System.Text.Encoding.ASCII.GetBytes(expectedPattern);

        // Search for the pattern in the buffer
        for (var i = 0; i <= bytesRead - patternBytes.Length; i++)
        {
            var match = true;
            for (var j = 0; j < patternBytes.Length; j++)
            {
                if (buffer[i + j] != patternBytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                // Found the pattern - return the actual position
                return targetPosition + i;
            }
        }

        // Pattern not found in the scan range
        return null;
    }

    /// <summary>
    /// Initializes decryption for an encrypted PDF document.
    /// </summary>
    /// <param name="document">The document being loaded</param>
    /// <param name="stream">The PDF stream</param>
    /// <param name="headerOffset">Offset to the PDF header</param>
    /// <param name="password">User or owner password</param>
    private static void InitializeDecryption(PdfDocument document, Stream stream, long headerOffset, string password)
    {
        // Get the /Encrypt dictionary reference
        var encryptRef = document.Trailer.Encrypt;
        if (encryptRef is null)
            return;

        // Find the encryption dictionary object in the xref
        var encryptEntry = document.XrefTable.Entries
            .FirstOrDefault(e => e.ObjectNumber == encryptRef.ObjectNumber && e.IsInUse);

        if (encryptEntry is null)
            throw new PdfSecurityException($"Encryption dictionary object {encryptRef.ObjectNumber} not found in xref");

        // Load the encryption dictionary (this object is NOT encrypted)
        stream.Position = encryptEntry.ByteOffset + headerOffset;
        var parser = new PdfParser(stream);
        var encryptObj = parser.ReadObject();

        if (encryptObj is not PdfDictionary encryptDict)
            throw new PdfSecurityException("Encryption dictionary is not a dictionary");

        // Get document ID from trailer
        byte[] documentId = [];
        if (document.Trailer.Dictionary.TryGetValue(new PdfName("ID"), out var idObj) &&
            idObj is PdfArray idArray && idArray.Count > 0 &&
            idArray[0] is PdfString idString)
        {
            documentId = idString.Bytes;
        }

        // Create the decryptor (will verify password)
        try
        {
            document.Decryptor = new PdfDecryptor(encryptDict, documentId, password);
            var passwordType = document.Decryptor.IsUserPassword ? "user" : "owner";
            PdfLogger.Log(LogCategory.PdfTool, $"PDF decryption initialized ({passwordType} password, {document.Decryptor.Method})");
        }
        catch (PdfSecurityException ex)
        {
            PdfLogger.Log(LogCategory.PdfTool, $"PDF decryption failed: {ex.Message}");
            throw;
        }
    }
}
