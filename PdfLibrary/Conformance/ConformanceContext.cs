using System.Linq;
using ICCSharp.Profile;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using PdfLibrary.Xmp;
using ConfXmp = PdfLibrary.Conformance.Xmp;

namespace PdfLibrary.Conformance;

/// <summary>A parsed /OutputIntents entry: its subtype and destination ICC profile.</summary>
internal readonly record struct OutputIntentInfo(
    string? Subtype,                    // /S value, e.g. "GTS_PDFA1"
    PdfIndirectReference? ProfileRef,   // the indirect ref of /DestOutputProfile, if indirect
    PdfStream? Profile);                // the resolved /DestOutputProfile stream, if any

/// <summary>The colour family of an ICC profile's data colour space, as relevant to device-colour matching.</summary>
internal enum OutputIntentColour { None, Gray, Rgb, Cmyk, Other }

/// <summary>
/// A font used for text showing, the full set of character codes actually drawn with it, and the subset of
/// those codes shown outside text rendering mode 3 (invisible). veraPDF exempts render-mode-3 text from
/// glyph-present (6.2.11.4.1 t2) and widths (6.2.11.5) — consumers of those clauses should use
/// <see cref="VisibleCodes"/>; .notdef (6.2.11.8) and ToUnicode ("regardless of rendering mode") stay on
/// <see cref="Codes"/>.
/// </summary>
internal readonly record struct UsedFontCodes(
    PdfFont Font, IReadOnlyCollection<int> Codes, IReadOnlyCollection<int> VisibleCodes);

/// <summary>
/// Per-run state handed to each <see cref="IConformanceRule"/>: the document under inspection, the
/// profile being targeted, the raw source bytes when available, and shared helpers (indirect-reference
/// resolution, object enumeration) so rules do not each re-implement navigation. Rules read from the
/// document and never mutate it.
/// </summary>
internal sealed class ConformanceContext
{
    private IReadOnlyList<PdfStream>? _streams;
    private IReadOnlyList<OutputIntentInfo>? _outputIntents;
    private IReadOnlyList<PdfDictionary>? _referencedFonts;
    private IReadOnlyList<PdfDictionary>? _annotations;
    private IReadOnlyList<PdfDictionary>? _formFields;
    private IReadOnlyList<PdfPage>? _pages;
    private PdfCatalog? _catalog;
    private bool _catalogResolved;
    private OutputIntentColour? _outputIntentColour;
    private IReadOnlyList<TransparencyAnalysis.PageTransparency>? _pageTransparency;
    private IReadOnlyList<UsedFontCodes>? _usedTextGlyphs;
    private IReadOnlyDictionary<PdfDictionary, IReadOnlyList<int>>? _fontPagesUsed;
    private MarkedContentAnalysis? _markedContent;
    private bool _xmpResolved;
    private XmpPacket? _xmp;
    private byte[]? _xmpBytes;
    private IReadOnlyList<XmpNode>? _xmpTree;
    private ConfXmp.XmpExtensionSchemas? _xmpExtensions;

    public ConformanceContext(PdfDocument document, ConformanceProfile target, byte[]? sourceBytes = null)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Target = target;
        SourceBytes = sourceBytes;
    }

    /// <summary>The document being checked.</summary>
    public PdfDocument Document { get; }

    /// <summary>The single profile this run targets.</summary>
    public ConformanceProfile Target { get; }

    /// <summary>
    /// The raw bytes of the source file, or null when the document was inspected in memory (no source
    /// available). Byte-level rules (e.g. post-EOF data) require this and skip gracefully when it is null.
    /// </summary>
    public byte[]? SourceBytes { get; }

    /// <summary>
    /// Every stream object in the document, materialized once and cached. Streams are always indirect,
    /// so enumerating the indirect object table captures them all.
    /// </summary>
    public IReadOnlyList<PdfStream> Streams => _streams ??= CollectStreams();

    /// <summary>The catalog's /OutputIntents, parsed once and cached (empty when absent).</summary>
    public IReadOnlyList<OutputIntentInfo> OutputIntents => _outputIntents ??= ReadOutputIntents();

    /// <summary>
    /// Font dictionaries actually reachable for rendering — walking page resources, Form XObjects, tiling
    /// patterns, annotation appearance streams and Type3 glyph resources (recursively, cycle-guarded), and
    /// following each Type0 font to its descendant CIDFont. Excludes fonts that are present but unreferenced
    /// (e.g. an unused AcroForm /DR font), which PDF/A/X do not require to be embedded. Cached.
    /// </summary>
    public IReadOnlyList<PdfDictionary> ReferencedFonts => _referencedFonts ??= CollectReferencedFonts();

    /// <summary>
    /// Every annotation dictionary reachable from a page's /Annots array, in page order. Cached.
    /// (Widget annotations that are merged with a form field appear here as well as in
    /// <see cref="FormFields"/>.)
    /// </summary>
    public IReadOnlyList<PdfDictionary> Annotations => _annotations ??= CollectAnnotations();

    /// <summary>
    /// Every interactive-form field dictionary, walking the AcroForm /Fields tree through /Kids.
    /// Cycle-guarded on indirect object number. Empty when the document has no AcroForm. Cached.
    /// </summary>
    public IReadOnlyList<PdfDictionary> FormFields => _formFields ??= CollectFormFields();

    /// <summary>The colour family of the file's PDF/A output-intent ICC profile (None if there is no
    /// output intent with a parseable destination profile). Cached.</summary>
    public OutputIntentColour OutputIntentColourFamily => _outputIntentColour ??= ComputeOutputIntentColour();

    /// <summary>
    /// Per-page transparency facts — whether each page hosts a transparent object, whether it defines its
    /// own group blending colour space, and the device families of every reachable transparency group's
    /// blending space. Backs the clause 6.2.10 / 6.2.4.3 blending-colour rules. Walked once and cached.
    /// </summary>
    public IReadOnlyList<TransparencyAnalysis.PageTransparency> PageTransparency =>
        _pageTransparency ??= TransparencyAnalysis.Analyze(this);

    /// <summary>
    /// Every font used for text showing and the character codes drawn with it, walking page content and
    /// Form XObjects. Backs the PDF/A-2u Unicode-mapping rules (which need the codes actually used, not the
    /// codes a font declares). Cached.
    /// </summary>
    public IReadOnlyList<UsedFontCodes> UsedTextGlyphs { get { EnsureUsedTextGlyphs(); return _usedTextGlyphs!; } }

    /// <summary>
    /// A page's content streams, concatenated and parsed ONCE per document. Every rule that reads page
    /// content shares this; before it existed seven of them each rebuilt the same byte array and
    /// re-parsed it, which measured 20.8% of a scan over the gwg-gos print corpus and dominated the
    /// PDF/UA reference files.
    ///
    /// <para>The streams are joined with a newline because ISO 32000-1 7.8.2 makes them one logical
    /// stream whose divisions fall between lexical tokens — concatenating them bare would let the tail
    /// of one and the head of the next lex as a single token, silently losing both operators.</para>
    ///
    /// <para>Returns an empty list for a page with no content, and for content that will not decode or
    /// parse: every caller previously treated those as "nothing to see", and a rule that reports
    /// nothing can never manufacture a false positive.</para>
    /// </summary>
    public IReadOnlyList<PdfOperator> PageContentOperators(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_pageOperators.TryGetValue(page, out IReadOnlyList<PdfOperator>? cached))
            return cached;

        var combined = new List<byte>();
        foreach (PdfStream content in page.GetContents())
        {
            // An undecodable content stream is a different clause's concern; the rest of the page
            // still parses, which is what every call site did before this was shared.
            try { combined.AddRange(content.GetDecodedData(Document.Decryptor)); }
            catch { /* skip this stream */ }
            combined.Add((byte)'\n'); // one logical stream (ISO 32000-1, 7.8.2)
        }

        var sawOutOfRangeInteger = false;
        IReadOnlyList<PdfOperator> operators;
        if (combined.Count == 0)
        {
            operators = [];
        }
        else
        {
            try { operators = PdfContentParser.Parse(combined.ToArray(), out sawOutOfRangeInteger); }
            catch { operators = []; sawOutOfRangeInteger = false; }
        }

        // Cached UNCONDITIONALLY, unlike the operator list below. It is one bool per page, and the
        // operator cache deliberately stops growing past its budget — letting the flag share that fate
        // would make the 6.1.13 check silently depend on how many pages preceded this one.
        _pageOutOfRangeInteger[page] = sawOutOfRangeInteger;

        // Retention is bounded deliberately. Every content rule sweeps all pages independently, so a
        // useful cache must hold the whole document at once — and a long document's parsed operators
        // are far larger than its bytes. Past the budget the cache stops GROWING rather than evicting:
        // eviction would restore the old re-parse cost with the bookkeeping on top, whereas the pages
        // already cached keep paying off for every later rule.
        _cachedOperatorCount += operators.Count;
        if (_cachedOperatorCount <= MaxCachedOperators)
            _pageOperators[page] = operators;

        return operators;
    }

    /// <summary>
    /// The retention ceiling, in parsed operators. Measured at ~118 bytes each (2026-08-20: caching one
    /// dense 12 MB magazine cost +118 MB of peak working set), so this bounds the cache at roughly 30 MB
    /// per document.
    ///
    /// <para>Deliberately not generous. The engine backs an interactive desktop app as well as the batch
    /// CLI, and a preflight that quietly adds a hundred megabytes to opening one file is a bad trade for
    /// a speedup the user cannot see. 250k operators still covers ordinary documents whole — a typical
    /// page runs to hundreds or low thousands of operators — so the cap only engages on the outliers,
    /// which are exactly the documents where unbounded retention would hurt most.</para>
    /// </summary>
    private const int MaxCachedOperators = 250_000;

    private readonly Dictionary<PdfPage, IReadOnlyList<PdfOperator>> _pageOperators =
        new(ReferenceEqualityComparer.Instance);

    private readonly Dictionary<PdfPage, bool> _pageOutOfRangeInteger =
        new(ReferenceEqualityComparer.Instance);

    private int _cachedOperatorCount;

    /// <summary>
    /// Whether this page's content contained an integer literal outside Int32 (ISO 19005-2 6.1.13
    /// test 1). Reported by the parser rather than read off the operands, because typed numeric
    /// operators (<c>Td</c>, <c>Tm</c>, <c>cm</c>, …) rebuild their operands as <see cref="PdfReal"/>
    /// via <c>ToDouble()</c> and discard the original marked integer.
    ///
    /// <para>Riding the parser also means an inline image's binary payload is skipped correctly; a
    /// raw token scan of the same bytes would read a run of ASCII digits inside image data as a huge
    /// integer and manufacture a false positive.</para>
    /// </summary>
    public bool PageContentHasOutOfRangeInteger(PdfPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (_pageOutOfRangeInteger.TryGetValue(page, out bool flag))
            return flag;

        PageContentOperators(page);   // populates the flag as a side effect of the parse
        _pageOutOfRangeInteger.TryGetValue(page, out flag);
        return flag;
    }

    /// <summary>
    /// For each font dictionary actually drawn with — a character shown via a text-showing operator
    /// while it was the selected /Tf font — the indices of the pages it was drawn on. Computed from
    /// the SAME per-page content-stream walk as <see cref="UsedTextGlyphs"/>, captured before that
    /// walk merges codes across pages and discards which page each one came from. A font merely
    /// PRESENT in a page's (or an inherited, or a Form XObject's) resource dictionary but never
    /// selected to draw a character is not "used" on that page: recovering page attribution from
    /// resource-dictionary presence instead of this walk both under-reports (misses a font only
    /// reachable through a Form XObject, tiling pattern or annotation appearance that a page-level
    /// resource scan does not see) and over-reports (counts a font that sits, unused, in a shared
    /// resource dictionary). Cached alongside <see cref="UsedTextGlyphs"/>.
    /// </summary>
    internal IReadOnlyDictionary<PdfDictionary, IReadOnlyList<int>> FontPagesUsed
    {
        get { EnsureUsedTextGlyphs(); return _fontPagesUsed!; }
    }

    /// <summary>
    /// The page-content marked-content facts for the PDF/UA-1 rules — whether any real content is untagged,
    /// whether any artifact and tagged sequences nest, and which MCIDs carry a content-stream
    /// <c>/ActualText</c>. Walked once over all pages (and their Form XObjects) and cached.
    /// </summary>
    public MarkedContentAnalysis MarkedContent => _markedContent ??= AnalyzeMarkedContent();

    /// <summary>
    /// The document's XMP metadata packet, parsed once from the catalog's /Metadata stream and cached
    /// (null when there is no /Metadata). Backs the XMP conformance rules.
    /// </summary>
    public XmpPacket? Xmp { get { EnsureXmp(); return _xmp; } }

    /// <summary>
    /// The raw decoded bytes of the /Metadata stream, cached alongside <see cref="Xmp"/> (null when
    /// there is no /Metadata). Used for signals the lossy packet parser cannot represent — e.g.
    /// detecting a PDF/A extension-schema declaration by scanning for its namespace URI.
    /// </summary>
    public byte[]? XmpMetadataBytes { get { EnsureXmp(); return _xmpBytes; } }

    /// <summary>
    /// The faithful XMP RDF value tree — the top-level XMP properties parsed straight from
    /// <see cref="XmpMetadataBytes"/> with their full struct/array/lang-alt shape preserved (unlike the
    /// lossy <see cref="Xmp"/> packet). Empty when there is no /Metadata or it will not parse. Cached.
    /// Backs the clause 6.6.2.3.1 value-type rules.
    /// </summary>
    public IReadOnlyList<XmpNode> XmpTree { get { EnsureXmp(); return _xmpTree ?? []; } }

    /// <summary>
    /// The PDF/A extension-schema declarations parsed from <see cref="XmpTree"/> — the custom
    /// (namespace, property) → value-type definitions a conformant packet may use. Empty when none are
    /// declared. Cached.
    /// </summary>
    public ConfXmp.XmpExtensionSchemas XmpExtensions
    {
        get { EnsureXmp(); return _xmpExtensions ?? ConfXmp.XmpExtensionSchemas.Empty; }
    }

    private void EnsureXmp()
    {
        if (_xmpResolved) return;
        _xmpResolved = true;
        PdfStream? metadata = Catalog?.GetMetadata();
        if (metadata is null) return;
        _xmpBytes = metadata.GetDecodedData(Document.Decryptor);
        _xmp = XmpPacket.Parse(_xmpBytes);
        _xmpTree = XmpTreeParser.Parse(_xmpBytes);
        _xmpExtensions = ConfXmp.XmpExtensionSchemas.Parse(_xmpTree);
    }

    /// <summary>The document catalog, resolved once and cached (null when the document has none).</summary>
    public PdfCatalog? Catalog
    {
        get
        {
            if (!_catalogResolved)
            {
                _catalog = Document.GetCatalog();
                _catalogResolved = true;
            }
            return _catalog;
        }
    }

    /// <summary>The document's pages, walked once and cached (rules must not each re-walk the page tree).</summary>
    public IReadOnlyList<PdfPage> Pages => _pages ??= Document.GetPages();

    /// <summary>
    /// Resolves an indirect reference to its referenced object; returns <paramref name="obj"/>
    /// unchanged when it is already a direct object (or null).
    /// </summary>
    public PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? Document.ResolveReference(reference) : obj;

    /// <summary>Resolves <paramref name="obj"/> and returns its name value, or null if it is not a name.</summary>
    public string? ResolveName(PdfObject? obj) => (Resolve(obj) as PdfName)?.Value;

    /// <summary>
    /// Enumerates the value objects of a PDF name tree given its root node, walking the /Names + /Kids
    /// structure ITERATIVELY with a node budget. The iterative form and the budget guard against a hostile
    /// tree — a recursive walk over a deep chain of direct /Kids nodes would throw an uncatchable
    /// StackOverflowException, and an unbounded one could spin on a wide/cyclic tree. Values are yielded
    /// unresolved (callers resolve as needed). Shared by the JavaScript-action and embedded-file rules.
    /// </summary>
    public IEnumerable<PdfObject> EnumerateNameTree(PdfObject? rootNode)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<PdfObject?>();
        stack.Push(rootNode);

        for (int budget = 100_000; stack.Count > 0 && budget > 0; budget--)
        {
            if (Resolve(stack.Pop()) is not PdfDictionary node)
                continue;
            if (node.IsIndirect && !visited.Add(node.ObjectNumber))
                continue; // guards indirect-node cycles

            // Leaf: /Names is a flat [key1 value1 key2 value2 …] array — values sit at the odd indices.
            if (Resolve(node.Get("Names")) is PdfArray entries)
                for (int i = 1; i < entries.Count; i += 2)
                    yield return entries[i];

            // Intermediate: descend into /Kids.
            if (Resolve(node.Get("Kids")) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
        }
    }

    private IReadOnlyList<PdfStream> CollectStreams()
    {
        Document.MaterializeAllObjects();
        return Document.Objects.Values.OfType<PdfStream>().ToList();
    }

    private IReadOnlyList<PdfDictionary> CollectReferencedFonts() =>
        ReferencedFontWalker.Collect(Document, Pages, Annotations, Catalog);

    private IReadOnlyList<PdfDictionary> CollectAnnotations()
    {
        var result = new List<PdfDictionary>();
        var seen = new HashSet<int>();
        foreach (PdfPage page in Pages)
        {
            if (page.GetAnnotations() is not { } annots)
                continue;
            foreach (PdfObject entry in annots)
            {
                if (Resolve(entry) is not PdfDictionary annot)
                    continue;
                if (annot.IsIndirect && !seen.Add(annot.ObjectNumber))
                    continue; // an annotation shared across pages is inspected once
                result.Add(annot);
            }
        }
        return result;
    }

    private IReadOnlyList<PdfDictionary> CollectFormFields()
    {
        var result = new List<PdfDictionary>();
        if (Catalog?.GetAcroForm() is not { } acroForm
            || Resolve(acroForm.Get("Fields")) is not PdfArray fields)
        {
            return result;
        }

        var seen = new HashSet<int>();
        var stack = new Stack<PdfObject>(fields);
        while (stack.Count > 0)
        {
            if (Resolve(stack.Pop()) is not PdfDictionary field)
                continue;
            if (field.IsIndirect && !seen.Add(field.ObjectNumber))
                continue; // already visited — guards against a cyclic /Kids graph

            result.Add(field);
            if (Resolve(field.Get("Kids")) is PdfArray kids)
                foreach (PdfObject kid in kids)
                    stack.Push(kid);
        }
        return result;
    }

    /// <summary>Populates both <see cref="_usedTextGlyphs"/> and <see cref="_fontPagesUsed"/> from a
    /// single per-page content-stream walk (see <see cref="FontPagesUsed"/> for why they share one
    /// walk rather than each re-deriving usage a different, less precise way).</summary>
    private void EnsureUsedTextGlyphs()
    {
        if (_usedTextGlyphs is not null)
            return;

        var merged = new Dictionary<PdfFont, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var mergedVisible = new Dictionary<PdfFont, HashSet<int>>(ReferenceEqualityComparer.Instance);
        var pagesByFont = new Dictionary<PdfDictionary, SortedSet<int>>(ReferenceEqualityComparer.Instance);

        for (var i = 0; i < Pages.Count; i++)
        {
            PdfPage page = Pages[i];

            // Concatenate the page's content streams before parsing so an operator split across a stream
            // boundary still parses (ISO 32000-1 7.8.2), matching the renderer's page-content handling.
            var collector = new ToUnicodeUsageCollector(page.GetResources(), Document);
            try { collector.ProcessOperators(PageContentOperators(page)); }
            catch (Exception) { continue; } // unparseable content: skip this page's usage

            foreach ((PdfFont font, HashSet<int> codes) in collector.Result)
            {
                if (!merged.TryGetValue(font, out HashSet<int>? set))
                    merged[font] = set = [];
                set.UnionWith(codes);

                if (codes.Count == 0)
                    continue; // present in collector.Result but nothing actually shown on this page
                if (!pagesByFont.TryGetValue(font.FontDictionary, out SortedSet<int>? pages))
                    pagesByFont[font.FontDictionary] = pages = [];
                pages.Add(i);
            }

            foreach ((PdfFont font, HashSet<int> codes) in collector.VisibleResult)
            {
                if (!mergedVisible.TryGetValue(font, out HashSet<int>? set))
                    mergedVisible[font] = set = [];
                set.UnionWith(codes);
            }
        }

        _usedTextGlyphs = merged.Select(kv => new UsedFontCodes(
            kv.Key,
            kv.Value,
            mergedVisible.TryGetValue(kv.Key, out HashSet<int>? visible) ? visible : [])).ToList();

        var pagesResult = new Dictionary<PdfDictionary, IReadOnlyList<int>>(ReferenceEqualityComparer.Instance);
        foreach (KeyValuePair<PdfDictionary, SortedSet<int>> kv in pagesByFont)
            pagesResult[kv.Key] = kv.Value.ToList();
        _fontPagesUsed = pagesResult;
    }

    private MarkedContentAnalysis AnalyzeMarkedContent()
    {
        IReadOnlyList<PdfPage> pages;
        try { pages = Pages; }
        catch (Exception) { return MarkedContentAnalysis.Empty; } // no navigable page tree

        int untaggedPage = -1, nestingPage = -1;
        var actualTextMcids = new HashSet<int>();

        for (int i = 0; i < pages.Count; i++)
        {
            // Concatenate the page's content streams before parsing so an operator (or a BDC/EMC pair) split
            // across a stream boundary still parses (ISO 32000-1 7.8.2), matching the renderer.
            var collector = new MarkedContentCollector(pages[i].GetResources(), Document);
            try { collector.ProcessOperators(PageContentOperators(pages[i])); }
            catch (Exception) { continue; } // unparseable content: skip this page

            if (collector.HasUntaggedContent && untaggedPage < 0)
                untaggedPage = i;
            if (collector.HasArtifactNesting && nestingPage < 0)
                nestingPage = i;
            actualTextMcids.UnionWith(collector.ActualTextMcids);
        }

        return new MarkedContentAnalysis(
            untaggedPage >= 0, untaggedPage, nestingPage >= 0, nestingPage, actualTextMcids);
    }

    private IReadOnlyList<OutputIntentInfo> ReadOutputIntents()
    {
        var result = new List<OutputIntentInfo>();
        if (Resolve(Document.GetCatalog()?.Dictionary.Get("OutputIntents")) is not PdfArray array)
            return result;

        foreach (PdfObject entry in array)
        {
            if (Resolve(entry) is not PdfDictionary dict)
                continue;
            string? subtype = (Resolve(dict.Get("S")) as PdfName)?.Value;
            PdfObject? destRaw = dict.Get("DestOutputProfile");
            var destRef = destRaw as PdfIndirectReference;
            var destStream = Resolve(destRaw) as PdfStream;
            result.Add(new OutputIntentInfo(subtype, destRef, destStream));
        }
        return result;
    }

    private OutputIntentColour ComputeOutputIntentColour()
    {
        // For a PDF/A target, only a GTS_PDFA1 output intent governs device colour: a GTS_PDFX (PDF/X)
        // intent does not satisfy PDF/A (ISO 19005-2, 6.2.2), so it is skipped here and the device-colour
        // rule fires on any device colour. PDF/X targets are unaffected (they require their own GTS_PDFX).
        bool pdfaTarget = (ConformanceProfile.AllPdfA & Target) != 0;
        foreach (OutputIntentInfo intent in OutputIntents)
        {
            if (pdfaTarget && intent.Subtype != "GTS_PDFA1") continue;
            if (intent.Profile is null) continue;
            try
            {
                ProfileHeader h = IccProfile.Parse(intent.Profile.GetDecodedData(Document.Decryptor)).Header;
                if (h.DataColorSpace == ColorSpaceSignatures.RGB) return OutputIntentColour.Rgb;
                if (h.DataColorSpace == ColorSpaceSignatures.CMYK) return OutputIntentColour.Cmyk;
                if (h.DataColorSpace == ColorSpaceSignatures.Gray) return OutputIntentColour.Gray;
                return OutputIntentColour.Other;
            }
            catch (Exception) { /* try next intent */ }
        }
        return OutputIntentColour.None;
    }
}
