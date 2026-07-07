using System.Linq;
using ICCSharp.Profile;
using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Conformance;

/// <summary>A parsed /OutputIntents entry: its subtype and destination ICC profile.</summary>
internal readonly record struct OutputIntentInfo(
    string? Subtype,                    // /S value, e.g. "GTS_PDFA1"
    PdfIndirectReference? ProfileRef,   // the indirect ref of /DestOutputProfile, if indirect
    PdfStream? Profile);                // the resolved /DestOutputProfile stream, if any

/// <summary>The colour family of an ICC profile's data colour space, as relevant to device-colour matching.</summary>
internal enum OutputIntentColour { None, Gray, Rgb, Cmyk, Other }

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
    private IReadOnlyList<PdfDictionary>? _fontDictionaries;
    private OutputIntentColour? _outputIntentColour;

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

    /// <summary>Every font dictionary (/Type /Font) in the document, materialized once and cached.</summary>
    public IReadOnlyList<PdfDictionary> FontDictionaries => _fontDictionaries ??= CollectFonts();

    /// <summary>The colour family of the file's PDF/A output-intent ICC profile (None if there is no
    /// output intent with a parseable destination profile). Cached.</summary>
    public OutputIntentColour OutputIntentColourFamily => _outputIntentColour ??= ComputeOutputIntentColour();

    /// <summary>
    /// Resolves an indirect reference to its referenced object; returns <paramref name="obj"/>
    /// unchanged when it is already a direct object (or null).
    /// </summary>
    public PdfObject? Resolve(PdfObject? obj) =>
        obj is PdfIndirectReference reference ? Document.ResolveReference(reference) : obj;

    private IReadOnlyList<PdfStream> CollectStreams()
    {
        Document.MaterializeAllObjects();
        return Document.Objects.Values.OfType<PdfStream>().ToList();
    }

    private IReadOnlyList<PdfDictionary> CollectFonts()
    {
        Document.MaterializeAllObjects();
        return Document.Objects.Values
            .OfType<PdfDictionary>()
            .Where(d => d.Get("Type") is PdfName { Value: "Font" })
            .ToList();
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
