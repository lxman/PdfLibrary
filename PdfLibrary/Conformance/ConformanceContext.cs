using System.Linq;
using ICCSharp.Profile;
using PdfLibrary.Content;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Document;
using PdfLibrary.Fonts;
using PdfLibrary.Metadata;
using PdfLibrary.Structure;
using ConfXmp = PdfLibrary.Conformance.Xmp;

namespace PdfLibrary.Conformance;

/// <summary>A parsed /OutputIntents entry: its subtype and destination ICC profile.</summary>
internal readonly record struct OutputIntentInfo(
    string? Subtype,                    // /S value, e.g. "GTS_PDFA1"
    PdfIndirectReference? ProfileRef,   // the indirect ref of /DestOutputProfile, if indirect
    PdfStream? Profile);                // the resolved /DestOutputProfile stream, if any

/// <summary>The colour family of an ICC profile's data colour space, as relevant to device-colour matching.</summary>
internal enum OutputIntentColour { None, Gray, Rgb, Cmyk, Other }

/// <summary>A font used for text showing and the set of character codes actually drawn with it.</summary>
internal readonly record struct UsedFontCodes(PdfFont Font, IReadOnlyCollection<int> Codes);

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
    private MarkedContentAnalysis? _markedContent;
    private bool _xmpResolved;
    private XmpPacket? _xmp;
    private byte[]? _xmpBytes;
    private IReadOnlyList<ConfXmp.XmpNode>? _xmpTree;
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
    public IReadOnlyList<UsedFontCodes> UsedTextGlyphs => _usedTextGlyphs ??= CollectUsedTextGlyphs();

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
    public IReadOnlyList<ConfXmp.XmpNode> XmpTree { get { EnsureXmp(); return _xmpTree ?? []; } }

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
        _xmpTree = ConfXmp.XmpTreeParser.Parse(_xmpBytes);
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

    private IReadOnlyList<PdfDictionary> CollectReferencedFonts()
    {
        var fonts = new List<PdfDictionary>();
        var fontSeen = new HashSet<int>();      // font object numbers already collected
        var resourceSeen = new HashSet<int>();  // resource dictionaries already walked (cycle guard)
        var streamSeen = new HashSet<int>();    // XObject / pattern streams already walked

        void AddFont(PdfObject? fontObj)
        {
            if (Resolve(fontObj) is not PdfDictionary font)
                return;
            if (font.IsIndirect && !fontSeen.Add(font.ObjectNumber))
                return;

            fonts.Add(font);

            switch (ResolveName(font.Get("Subtype")))
            {
                // A composite font's program lives on its descendant CIDFont — reach it so embedding is checked.
                case "Type0" when Resolve(font.Get("DescendantFonts")) is PdfArray descendants && descendants.Count > 0:
                    AddFont(descendants[0]);
                    break;
                // A Type3 glyph is a content stream drawn through the font's own resources.
                case "Type3" when Resolve(font.Get("Resources")) is PdfDictionary type3Resources:
                    WalkResources(new PdfResources(type3Resources, Document));
                    break;
            }
        }

        void WalkResources(PdfResources? resources)
        {
            if (resources is null)
                return;
            if (resources.Dictionary.IsIndirect && !resourceSeen.Add(resources.Dictionary.ObjectNumber))
                return;

            if (resources.GetFonts() is { } fontDict)
                foreach (PdfObject font in fontDict.Values)
                    AddFont(font);

            if (resources.GetXObjects() is { } xobjects)
                foreach (PdfObject xobject in xobjects.Values)
                    WalkStreamResources(xobject);

            if (resources.GetPatterns() is { } patterns)
                foreach (PdfObject pattern in patterns.Values)
                    WalkStreamResources(pattern); // tiling patterns are streams that carry /Resources

            // An ExtGState /Font entry ([font size]) can be the only reference to a rendered font.
            if (resources.GetExtGStates() is { } extGStates)
                foreach (PdfObject graphicsState in extGStates.Values)
                    if (Resolve(graphicsState) is PdfDictionary gsDict
                        && Resolve(gsDict.Get("Font")) is PdfArray gsFont && gsFont.Count > 0)
                        AddFont(gsFont[0]);
        }

        void WalkStreamResources(PdfObject? streamObj)
        {
            if (Resolve(streamObj) is not PdfStream stream)
                return;
            if (stream.IsIndirect && !streamSeen.Add(stream.ObjectNumber))
                return;
            if (Resolve(stream.Dictionary.Get("Resources")) is PdfDictionary resourceDict)
                WalkResources(new PdfResources(resourceDict, Document));
        }

        void WalkAppearance(PdfObject? apObj)
        {
            if (Resolve(apObj) is not PdfDictionary appearance)
                return;
            foreach (PdfObject state in appearance.Values) // /N, /D, /R
            {
                switch (Resolve(state))
                {
                    case PdfStream:
                        WalkStreamResources(state);
                        break;
                    case PdfDictionary subStates: // per-state appearances (e.g. button on/off)
                        foreach (PdfObject sub in subStates.Values)
                            WalkStreamResources(sub);
                        break;
                }
            }
        }

        // The nearest /Resources up a page's full /Parent chain (page.GetResources() only inherits one
        // level, unlike page.GetMediaBox()), so a font in a grandparent /Pages node is still reached.
        PdfResources? EffectiveResources(PdfDictionary? node)
        {
            var chainSeen = new HashSet<int>();
            while (node is not null)
            {
                if (node.IsIndirect && !chainSeen.Add(node.ObjectNumber))
                    break; // guard a cyclic /Parent chain
                if (Resolve(node.Get("Resources")) is PdfDictionary resourceDict)
                    return new PdfResources(resourceDict, Document);
                node = Resolve(node.Get("Parent")) as PdfDictionary;
            }
            return null;
        }

        foreach (PdfPage page in Pages)
            WalkResources(EffectiveResources(page.Dictionary));
        foreach (PdfDictionary annot in Annotations)
            WalkAppearance(annot.Get("AP"));

        // AcroForm /DR fonts are rendered only when the viewer generates field appearances
        // (/NeedAppearances true); otherwise appearances come from /AP (already walked) and the /DR pool
        // is not necessarily drawn. Including /DR unconditionally would re-introduce the orphan over-report.
        if (Catalog?.GetAcroForm() is { } acroForm
            && Resolve(acroForm.Get("NeedAppearances")) is PdfBoolean { Value: true }
            && Resolve(acroForm.Get("DR")) is PdfDictionary defaultResources)
        {
            WalkResources(new PdfResources(defaultResources, Document));
        }

        return fonts;
    }

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

    private IReadOnlyList<UsedFontCodes> CollectUsedTextGlyphs()
    {
        var merged = new Dictionary<PdfFont, HashSet<int>>(ReferenceEqualityComparer.Instance);
        foreach (PdfPage page in Pages)
        {
            // Concatenate the page's content streams before parsing so an operator split across a stream
            // boundary still parses (ISO 32000-1 7.8.2), matching the renderer's page-content handling.
            var combined = new List<byte>();
            foreach (PdfStream content in page.GetContents())
            {
                combined.AddRange(content.GetDecodedData(Document.Decryptor));
                combined.Add((byte)'\n');
            }

            var collector = new ToUnicodeUsageCollector(page.GetResources(), Document);
            try { collector.ProcessOperators(PdfContentParser.Parse(combined.ToArray())); }
            catch (Exception) { continue; } // unparseable content: skip this page's usage

            foreach ((PdfFont font, HashSet<int> codes) in collector.Result)
            {
                if (!merged.TryGetValue(font, out HashSet<int>? set))
                    merged[font] = set = [];
                set.UnionWith(codes);
            }
        }
        return merged.Select(kv => new UsedFontCodes(kv.Key, kv.Value)).ToList();
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
            var combined = new List<byte>();
            foreach (PdfStream content in pages[i].GetContents())
            {
                combined.AddRange(content.GetDecodedData(Document.Decryptor));
                combined.Add((byte)'\n');
            }

            var collector = new MarkedContentCollector(pages[i].GetResources(), Document);
            try { collector.ProcessOperators(PdfContentParser.Parse(combined.ToArray())); }
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
        foreach (OutputIntentInfo intent in OutputIntents)
        {
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
