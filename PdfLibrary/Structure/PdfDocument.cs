using System.Diagnostics;
using System.Text;
using Logging;
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
public partial class PdfDocument : IDisposable
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
    /// Drops the decryptor so the document is treated as unencrypted. Callers must have already
    /// decrypted every stream's data in place (strings are decrypted at parse time); afterwards
    /// <see cref="IsEncrypted"/> is false and stream access no longer attempts decryption.
    /// </summary>
    internal void ClearDecryptor() => Decryptor = null;

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
        if (_stream is not null && entry.EntryType == PdfXrefEntryType.Uncompressed)
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

                // Set the decryptor if the document is encrypted
                if (Decryptor is not null)
                    parser.SetDecryptor(Decryptor);

                // Read the object
                PdfObject? obj = parser.ReadObject();
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
        ExtractObjectsFromStream(objStream, objectStreamNumber);
    }

    /// <summary>
    /// Extracts all compressed objects from an object stream
    /// Object stream format (ISO 32000-1 section 7.5.7):
    /// - First section: N pairs of (objectNumber byteOffset)
    /// - Second section: The actual object data starting at /First offset
    /// <paramref name="streamNumber"/> is this object stream's own object number: an object is only cached
    /// from here when the (merged, newest-wins) cross-reference table actually assigns it to this stream.
    /// In an incrementally-updated file the same object number can appear in several revisions' object
    /// streams; loading a superseded copy from an older stream would clobber the current one, so entries the
    /// xref maps elsewhere (a newer stream, an uncompressed object, or nothing) are skipped.
    /// </summary>
    private void ExtractObjectsFromStream(PdfStream objStream, int streamNumber)
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

        // Decode the stream (decryption is needed for encrypted documents)
        byte[] decodedData = objStream.GetDecodedData(Decryptor);

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
                length = offsets[i + 1] + firstOffset - offset;
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
            if (obj is null)
                continue;

            // Only cache this object if the merged xref actually assigns it to THIS stream. An older
            // revision's stream may still physically contain a superseded copy; caching it would overwrite
            // the current object (whose xref entry points at a newer stream or an uncompressed offset).
            PdfXrefEntry? owner = XrefTable.GetEntry(objectNumber);
            if (owner is { EntryType: PdfXrefEntryType.Compressed } && owner.ByteOffset == streamNumber)
                AddObject(objectNumber, 0, obj); // generation number is 0 for compressed objects
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

        PdfObject? catalog = ResolveReference(Trailer.Root);
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

        PdfObject? info = ResolveReference(Trailer.Info);
        return info as PdfDictionary;
    }

    /// <summary>
    /// Forces every in-use object referenced by the xref to be loaded into the object cache.
    /// Load() is lazy (object streams and on-demand objects), so a full rewrite must call this first.
    /// </summary>
    internal void MaterializeAllObjects()
    {
        // ToList() first: GetObject for a compressed entry adds extracted objects to _objects,
        // so snapshot the xref entries before iterating.
        foreach (PdfXrefEntry entry in XrefTable.Entries.Where(e => e.IsInUse).ToList())
            _ = GetObject(entry.ObjectNumber);
    }

    /// <summary>
    /// Gets the page count
    /// </summary>
    public int GetPageCount()
    {
        PdfCatalog? catalog = GetCatalog();
        if (catalog is null)
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
        if (catalog is null)
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
    /// Gets a range of pages (0-based indices, inclusive)
    /// </summary>
    /// <param name="startIndex">Starting page index (0-based)</param>
    /// <param name="endIndex">Ending page index (0-based, inclusive)</param>
    /// <returns>List of pages in the range</returns>
    public List<PdfPage> GetPages(int startIndex, int endIndex)
    {
        List<PdfPage> allPages = GetPages();
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
            int count = GetPageCount();
            return count > 0 ? GetPage(count - 1) : null;
        }
    }

    /// <summary>
    /// Reads the document's Tagged-PDF logical structure — the accessibility tag tree (ISO 32000-1, 14.7) —
    /// as a read-only <see cref="Document.TagTree"/>: structure elements with their role-mapped types,
    /// accessibility attributes (Alt/ActualText/Lang/…), page, and own text. Returns an empty tree when the
    /// document has no <c>/StructTreeRoot</c>.
    /// </summary>
    public Document.TagTree GetTagTree() => Document.TagTreeBuilder.Build(this);

    /// <summary>
    /// Reads the document's <c>/OutputIntents</c> array (ISO 32000-1, 14.11.5) as read-only
    /// <see cref="Document.OutputIntentDescriptor"/>s: each intent's subtype, output-condition metadata,
    /// and — when present — the embedded destination ICC profile (bytes and colour-space family). Returns
    /// an empty list when the document has no <c>/OutputIntents</c>.
    /// </summary>
    public IReadOnlyList<Document.OutputIntentDescriptor> GetOutputIntents() => Document.OutputIntentReader.Read(this);

    /// <summary>
    /// Reads the document's embedded files — the catalog's <c>/Names /EmbeddedFiles</c> name tree plus
    /// catalog-level <c>/AF</c> associated files (ISO 32000-2, 7.11.4 / 14.13) — as read-only
    /// <see cref="Document.EmbeddedFileDescriptor"/>s: names, file names, description, MIME subtype,
    /// associated-file relationship, and the decoded file bytes. Returns an empty list when the
    /// document embeds no files.
    /// </summary>
    public IReadOnlyList<Document.EmbeddedFileDescriptor> GetEmbeddedFiles() =>
        Document.EmbeddedFileReader.Read(this);

    /// <summary>The distinct named Separation/DeviceN colorants used on a page (Soft-Proof SP-1/SP-6b) — the
    /// plate list + per-colorant tint ramps for the ink-separations preview. Walks the page's reachable
    /// resource graph (its <c>/Resources/ColorSpace</c> plus image XObject colour spaces and, recursively,
    /// form-XObject and tiling/shading-pattern resources), unwrapping Indexed bases; content streams are not
    /// parsed. Returns an empty list when the page declares no such colorants.</summary>
    public IReadOnlyList<Document.PageColorant> GetPageColorants(int pageIndex) =>
        Document.PageColorantReader.Read(this, pageIndex);

    /// <summary>
    /// Reads the document's interactive form (ISO 32000-1, 12.7) — the <c>/AcroForm</c> entry of the
    /// catalog — as a read-only <see cref="Document.AcroFormInfo"/>. Returns null when the document has no
    /// <c>/AcroForm</c>.
    /// </summary>
    public Document.AcroFormInfo? GetAcroForm() => Document.AcroFormReader.Read(this);

    /// <summary>
    /// True iff the document catalog references an <c>/AcroForm</c> dictionary with at least one declared
    /// field. A cheap existence check that walks only the catalog (not a full terminal-field recursion).
    /// </summary>
    public bool HasForm => GetAcroForm() is { HasFields: true };

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
        List<PdfPage> pages = GetPages();
        if (pages.Count == 0)
            return string.Empty;

        var texts = new List<string>();
        foreach (PdfPage page in pages)
        {
            string text = page.ExtractText();
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
        List<PdfPage> pages = GetPages();

        for (var i = 0; i < pages.Count; i++)
        {
            List<PdfImage> pageImages = pages[i].GetImages();
            foreach (PdfImage image in pageImages)
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

        FileStream stream = File.OpenRead(filePath);
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
            (document.Version, long headerOffset) = ParseHeader(stream);
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Header parsed in {phaseStopwatch.ElapsedMilliseconds}ms");

            // Find startxref position
            phaseStopwatch.Restart();
            long startxref = PdfTrailerParser.FindStartXref(stream);
            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Found startxref in {phaseStopwatch.ElapsedMilliseconds}ms");

            // Adjust startxref by header offset (PDF 2.0 files may have data before the header)
            long actualXrefPosition = startxref + headerOffset;

            // Validate that the startxref offset actually points to an xref table or stream.
            // Some corrupt/legacy PDFs have a wrong startxref; fall back to scanning.
            actualXrefPosition = ValidateOrRecoverXrefPosition(stream, actualXrefPosition);

            // Parse cross-reference table(s) - follow /Prev chain for incremental updates
            phaseStopwatch.Restart();
            var rebuilt = false;
            try
            {
                ParseXrefChain(stream, document, actualXrefPosition, headerOffset);
            }
            catch (PdfParseException)
            {
                document.XrefTable.Clear();
                RebuildXrefFromScan(stream, document, actualXrefPosition);
                rebuilt = true;
            }

            PdfLogger.Log(LogCategory.Timings, $"PDF Load: Xref {(rebuilt ? "rebuilt" : "parsed")} in {phaseStopwatch.ElapsedMilliseconds}ms ({document.XrefTable.Entries.Count} entries)");

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
            int loadedObjects;
            try
            {
                loadedObjects = LoadUncompressedObjects(stream, document, headerOffset, rebuilt);
            }
            catch (PdfParseException) when (!rebuilt)
            {
                // Object loading failed with the parsed xref — offsets are bad, rebuild
                document.XrefTable.Clear();
                document._objects.Clear();
                RebuildXrefFromScan(stream, document, actualXrefPosition);
                loadedObjects = LoadUncompressedObjects(stream, document, headerOffset, true);
                rebuilt = true;
                PdfLogger.Log(LogCategory.PdfTool, $"Xref rebuild after object-load failure: {loadedObjects} objects loaded");
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
    private static int LoadUncompressedObjects(Stream stream, PdfDocument document, long headerOffset, bool tolerant)
    {
        var loaded = 0;
        foreach (PdfXrefEntry entry in document.XrefTable.Entries)
        {
            if (!entry.IsInUse || entry.EntryType == PdfXrefEntryType.Compressed)
                continue;

            try
            {
                long targetPosition = entry.ByteOffset + headerOffset;
                stream.Position = targetPosition;

                long? actualPosition = FindObjectHeader(stream, entry.ObjectNumber, entry.GenerationNumber, targetPosition);
                if (actualPosition is null)
                    continue;

                stream.Position = actualPosition.Value;
                var parser = new PdfParser(stream);
                parser.SetReferenceResolver(reference => document.GetObject(reference.ObjectNumber));
                if (document.Decryptor is not null)
                    parser.SetDecryptor(document.Decryptor);

                PdfObject? obj = parser.ReadObject();
                if (obj is not null)
                {
                    document.AddObject(entry.ObjectNumber, entry.GenerationNumber, obj);
                    loaded++;
                }
            }
            catch (PdfParseException) when (tolerant)
            {
                // In tolerant mode (rebuild), skip objects that fail to parse
            }
            catch (PdfParseException ex) when (!tolerant)
            {
                throw new PdfParseException(
                    $"Error parsing object {entry.ObjectNumber} at byte offset {entry.ByteOffset}: {ex.Message}", ex);
            }
        }
        return loaded;
    }

    private static void ParseXrefChain(Stream stream, PdfDocument document, long actualXrefPosition, long headerOffset)
    {
        long xrefPosition = actualXrefPosition;
        var xrefChainDepth = 0;
        const int maxXrefChainDepth = 100;

        while (xrefPosition >= 0 && xrefChainDepth < maxXrefChainDepth)
        {
            stream.Position = xrefPosition;
            var xrefParser = new PdfXrefParser(stream, document);
            PdfXrefParseResult xrefResult = xrefParser.Parse();

            // Object numbers already fixed by a newer cross-reference section — captured before this
            // section's own entries are merged so that this section's /XRefStm (below) may still override
            // this section's classic table, while nothing overrides a newer section.
            var fixedByNewerSection = new HashSet<int>(document.XrefTable.Entries.Select(e => e.ObjectNumber));

            foreach (PdfXrefEntry entry in xrefResult.Table.Entries)
            {
                if (!document.XrefTable.Contains(entry.ObjectNumber))
                    document.XrefTable.Add(entry);
            }

            PdfDictionary? currentTrailer = null;

            if (xrefChainDepth == 0)
            {
                document.Trailer.Dictionary.Clear();

                if (xrefResult is { IsXRefStream: true, TrailerDictionary: not null })
                {
                    foreach (KeyValuePair<PdfName, PdfObject> kvp in xrefResult.TrailerDictionary)
                        document.Trailer.Dictionary[kvp.Key] = kvp.Value;
                    currentTrailer = xrefResult.TrailerDictionary;
                }
                else
                {
                    var trailerParser = new PdfTrailerParser(stream);
                    (PdfTrailer trailer, _) = trailerParser.Parse();
                    foreach (KeyValuePair<PdfName, PdfObject> kvp in trailer.Dictionary)
                        document.Trailer.Dictionary[kvp.Key] = kvp.Value;
                    currentTrailer = trailer.Dictionary;
                }
            }
            else
            {
                if (xrefResult is { IsXRefStream: true, TrailerDictionary: not null })
                    currentTrailer = xrefResult.TrailerDictionary;
                else if (!xrefResult.IsXRefStream)
                {
                    var trailerParser = new PdfTrailerParser(stream);
                    (PdfTrailer trailer, _) = trailerParser.Parse();
                    currentTrailer = trailer.Dictionary;
                }
            }

            // Hybrid-reference files (ISO 32000-1:2008, 7.5.8.4): a classic trailer may carry an /XRefStm
            // entry pointing to a cross-reference stream whose compressed-object (type 2) entries index
            // objects the classic table marks free so that pre-1.5 readers skip them. Merge that stream so
            // those objects resolve. Its entries supersede this section's classic table but never a newer
            // section (guarded by fixedByNewerSection). A malformed stream is ignored — the classic /Prev
            // chain that old readers depend on must still complete.
            if (!xrefResult.IsXRefStream && currentTrailer is not null &&
                currentTrailer.TryGetValue(new PdfName("XRefStm"), out PdfObject xrefStmObj) &&
                xrefStmObj is PdfInteger xrefStmInt)
            {
                long xrefStmPosition = xrefStmInt.Value + headerOffset;
                if (xrefStmPosition >= 0 && xrefStmPosition < stream.Length)
                {
                    try
                    {
                        stream.Position = xrefStmPosition;
                        var xrefStmParser = new PdfXrefParser(stream, document);
                        PdfXrefParseResult xrefStmResult = xrefStmParser.Parse();
                        foreach (PdfXrefEntry entry in xrefStmResult.Table.Entries)
                        {
                            if (!fixedByNewerSection.Contains(entry.ObjectNumber))
                                document.XrefTable.Add(entry); // overwrites this section's classic free marker
                        }
                    }
                    catch (PdfParseException)
                    {
                        // Ignore a broken hybrid stream; the classic entries and /Prev chain still stand.
                    }
                }
            }

            long prevXrefPosition = -1;
            if (currentTrailer is not null &&
                currentTrailer.TryGetValue(new PdfName("Prev"), out PdfObject prevObj) &&
                prevObj is PdfInteger prevInt)
            {
                prevXrefPosition = prevInt.Value + headerOffset;
            }

            xrefPosition = prevXrefPosition;
            xrefChainDepth++;
        }
    }

    private static void RebuildXrefFromScan(Stream stream, PdfDocument document, long xrefPosition)
    {
        PdfLogger.Log(LogCategory.PdfTool, "Rebuilding xref by scanning for object markers");

        var data = new byte[stream.Length];
        stream.Position = 0;
        _ = stream.Read(data, 0, data.Length);

        // Build a set of stream-data ranges to skip (avoids false "N 0 obj" inside encoded data)
        var skipRanges = new List<(int start, int end)>();
        var streamKw = "stream"u8;
        var endstreamKw = "endstream"u8;
        for (var s = 0; s < data.Length - endstreamKw.Length; s++)
        {
            if (data.AsSpan(s, streamKw.Length).SequenceEqual(streamKw)
                && (s + streamKw.Length < data.Length)
                && (data[s + streamKw.Length] == 0x0A || data[s + streamKw.Length] == 0x0D))
            {
                int dataStart = s + streamKw.Length;
                if (data[dataStart] == 0x0D && dataStart + 1 < data.Length && data[dataStart + 1] == 0x0A)
                    dataStart += 2;
                else
                    dataStart++;

                int esPos = -1;
                for (int e = dataStart; e <= data.Length - endstreamKw.Length; e++)
                {
                    if (data.AsSpan(e, endstreamKw.Length).SequenceEqual(endstreamKw))
                    {
                        esPos = e;
                        break;
                    }
                }
                if (esPos > 0)
                {
                    skipRanges.Add((dataStart, esPos));
                    s = esPos + endstreamKw.Length;
                }
            }
        }

        // Scan for "N 0 obj" patterns, skipping stream data ranges
        var skipIndex = 0;
        for (var i = 0; i < data.Length - 10; i++)
        {
            // Skip past stream data
            while (skipIndex < skipRanges.Count && i >= skipRanges[skipIndex].end)
                skipIndex++;
            if (skipIndex < skipRanges.Count && i >= skipRanges[skipIndex].start && i < skipRanges[skipIndex].end)
            {
                i = skipRanges[skipIndex].end;
                continue;
            }

            if (!char.IsDigit((char)data[i])) continue;

            int numStart = i;
            while (i < data.Length && char.IsDigit((char)data[i])) i++;
            if (i >= data.Length || data[i] != (byte)' ') continue;
            i++;
            if (i >= data.Length || data[i] != (byte)'0') continue;
            i++;
            if (i >= data.Length || data[i] != (byte)' ') continue;
            i++;
            if (i + 2 >= data.Length) continue;
            if (data[i] != (byte)'o' || data[i + 1] != (byte)'b' || data[i + 2] != (byte)'j') continue;

            if (numStart > 0)
            {
                byte prev = data[numStart - 1];
                if (prev != (byte)'\n' && prev != (byte)'\r' && prev != (byte)' ' && prev != (byte)'\t')
                    continue;
            }

            int objNum = int.Parse(Encoding.ASCII.GetString(data, numStart, i - 3 - numStart));
            if (!document.XrefTable.Contains(objNum))
            {
                document.XrefTable.Add(new PdfXrefEntry(objNum, numStart, 0, true));
            }
        }

        // Parse the trailer from the xref location (the traditional xref + trailer should still be parseable)
        document.Trailer.Dictionary.Clear();
        try
        {
            stream.Position = xrefPosition;
            var xrefParser = new PdfXrefParser(stream, document);
            xrefParser.Parse(); // ignore the xref table entries, just advance past them
            var trailerParser = new PdfTrailerParser(stream);
            (PdfTrailer trailer, _) = trailerParser.Parse();
            foreach (KeyValuePair<PdfName, PdfObject> kvp in trailer.Dictionary)
                document.Trailer.Dictionary[kvp.Key] = kvp.Value;
        }
        catch
        {
            // If trailer parse fails, scan for "trailer" keyword
            int trailerPos = FindLastOccurrence(data, "trailer"u8);
            if (trailerPos >= 0)
            {
                stream.Position = trailerPos;
                var trailerParser = new PdfTrailerParser(stream);
                (PdfTrailer trailer, _) = trailerParser.Parse();
                foreach (KeyValuePair<PdfName, PdfObject> kvp in trailer.Dictionary)
                    document.Trailer.Dictionary[kvp.Key] = kvp.Value;
            }
        }

        PdfLogger.Log(LogCategory.PdfTool, $"Xref rebuild found {document.XrefTable.Entries.Count} objects");
    }

    private static int FindLastOccurrence(byte[] data, ReadOnlySpan<byte> pattern)
    {
        for (int i = data.Length - pattern.Length; i >= 0; i--)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return i;
        }
        return -1;
    }

    private static long ValidateOrRecoverXrefPosition(Stream stream, long candidate)
    {
        if (candidate >= 0 && candidate < stream.Length)
        {
            stream.Position = candidate;
            Span<byte> peek = stackalloc byte[4];
            int read = stream.Read(peek);
            if (read >= 4)
            {
                // Traditional xref starts with "xref"; xref stream starts with a digit (obj number)
                if ((peek[0] == (byte)'x' && peek[1] == (byte)'r' && peek[2] == (byte)'e' && peek[3] == (byte)'f')
                    || char.IsDigit((char)peek[0]))
                    return candidate;
            }
        }

        // startxref offset is invalid — scan backward from EOF for the "xref" keyword
        const int scanSize = 65536;
        long scanStart = Math.Max(0, stream.Length - scanSize);
        var bufLen = (int)(stream.Length - scanStart);
        var buf = new byte[bufLen];
        stream.Position = scanStart;
        _ = stream.Read(buf, 0, bufLen);

        for (int i = bufLen - 4; i >= 0; i--)
        {
            if (buf[i] == (byte)'x' && buf[i + 1] == (byte)'r' && buf[i + 2] == (byte)'e' && buf[i + 3] == (byte)'f'
                && (i == 0 || buf[i - 1] == (byte)'\n' || buf[i - 1] == (byte)'\r'))
            {
                long recovered = scanStart + i;
                PdfLogger.Log(LogCategory.PdfTool,
                    $"startxref offset {candidate} invalid — recovered xref at {recovered}");
                return recovered;
            }
        }

        return candidate;
    }

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

        // Atomic: write to a temp file and rename into place, so a failed or interrupted
        // save never leaves a truncated file where the previous one was.
        AtomicFileWriter.Write(filePath, Save);
    }

    /// <summary>
    /// Saves the document to a stream
    /// </summary>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Stream must be writable", nameof(stream));

        PdfDocumentSerializer.Write(this, stream);
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
        int bytesRead = stream.Read(buffer, 0, maxScanBytes);
        if (bytesRead == 0)
            return null;

        // Build the expected pattern: "N G obj"
        var expectedPattern = $"{expectedObjectNumber} {expectedGenerationNumber} obj";
        byte[] patternBytes = Encoding.ASCII.GetBytes(expectedPattern);

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
        PdfIndirectReference? encryptRef = document.Trailer.Encrypt;
        if (encryptRef is null)
            return;

        // Find the encryption dictionary object in the xref
        PdfXrefEntry? encryptEntry = document.XrefTable.Entries
            .FirstOrDefault(e => e.ObjectNumber == encryptRef.ObjectNumber && e.IsInUse);

        if (encryptEntry is null)
            throw new PdfSecurityException($"Encryption dictionary object {encryptRef.ObjectNumber} not found in xref");

        // Load the encryption dictionary (this object is NOT encrypted)
        stream.Position = encryptEntry.ByteOffset + headerOffset;
        var parser = new PdfParser(stream);
        PdfObject? encryptObj = parser.ReadObject();

        if (encryptObj is not PdfDictionary encryptDict)
            throw new PdfSecurityException("Encryption dictionary is not a dictionary");

        // Get document ID from trailer
        byte[] documentId = [];
        if (document.Trailer.Dictionary.TryGetValue(new PdfName("ID"), out PdfObject idObj) &&
            idObj is PdfArray { Count: > 0 } idArray &&
            idArray[0] is PdfString idString)
        {
            documentId = idString.Bytes;
        }

        // Create the decryptor (will verify password)
        try
        {
            document.Decryptor = new PdfDecryptor(encryptDict, documentId, password);
            string passwordType = document.Decryptor.IsUserPassword ? "user" : "owner";
            PdfLogger.Log(LogCategory.PdfTool, $"PDF decryption initialized ({passwordType} password, {document.Decryptor.Method})");
        }
        catch (PdfSecurityException ex)
        {
            PdfLogger.Log(LogCategory.PdfTool, $"PDF decryption failed: {ex.Message}");
            throw;
        }
    }
}
