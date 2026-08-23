using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;

namespace PdfLibrary.Editing;

/// <summary>One stream this editor would convert from /LZWDecode to /FlateDecode, with the filter
/// chain it carries today (for reporting -- the chain is re-derived at write time, never trusted from
/// here).</summary>
public sealed record StreamFilterRepairCandidate(int ObjectNumber, IReadOnlyList<string> FilterChain);

/// <summary>One stream carrying a filter PDF/A forbids that this editor will NOT convert, with the
/// user-facing sentence saying why. Deliberately a plain reason string rather than a repair-kind enum:
/// this domain has exactly one repair, so a single-member enum would be dead vocabulary that
/// exhaustiveness tests would then have to carry.</summary>
public sealed record StreamFilterRefusal(int ObjectNumber, string Reason);

/// <summary>Read-only classification of every stream in the document against ISO 19005-2/3 6.1.7.2.</summary>
public sealed record StreamFilterRepairPreview(
    IReadOnlyList<StreamFilterRepairCandidate> Candidates,
    IReadOnlyList<StreamFilterRefusal> Refused);

public sealed partial class PdfDocumentEditor
{
    private const string LzwFilter = "LZWDecode";
    private const string FlateFilter = "FlateDecode";

    /// <summary>The filters ISO 19005-2/3 6.1.7.2 permits outright -- deliberately the SAME set
    /// <c>StreamFiltersRule.Allowed</c> holds, so this editor's refusal set mirrors the detector's
    /// violation set exactly. /Crypt is handled separately (permitted only at Identity), and
    /// /LZWDecode is absent on purpose: it is the one disallowed filter this editor converts.</summary>
    private static readonly HashSet<string> PermittedFilters = new(StringComparer.Ordinal)
    {
        "ASCIIHexDecode", "ASCII85Decode", "FlateDecode", "RunLengthDecode",
        "CCITTFaxDecode", "JBIG2Decode", "DCTDecode", "JPXDecode",
    };

    /// <summary>Every indirect stream in the document. Deliberately the same set
    /// <c>ConformanceContext.CollectStreams</c> walks (the indirect object table, filtered to streams),
    /// so a finding the preflighter raised always has a candidate or a refusal here.</summary>
    private IEnumerable<PdfStream> EnumerateAllStreams()
    {
        _document.MaterializeAllObjects();
        foreach (PdfObject obj in _document.Objects.Values)
            if (obj is PdfStream { IsIndirect: true } stream)
                yield return stream;
    }

    /// <summary>The stream's /Filter as an ordered list of names. A single name yields one entry; an
    /// array yields one per position (resolving indirect entries); a malformed entry is skipped, which
    /// matches StreamFiltersRule's own "not this rule's concern" branch.</summary>
    private IReadOnlyList<string> FilterChainOf(PdfStream stream)
    {
        PdfObject? filterObj = ResolveObject(stream.Dictionary.Get("Filter"));
        if (filterObj is null) return [];

        if (filterObj is not PdfArray array)
            return filterObj is PdfName single ? [single.Value] : [];

        var names = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++)
            if (ResolveObject(array[i]) is PdfName name)
                names.Add(name.Value);
        return names;
    }

    /// <summary>The decode-parms entry aligned with filter position <paramref name="index"/> -- the
    /// same positional rule StreamFiltersRule.ParmsAt uses.</summary>
    private PdfObject? DecodeParmsAt(PdfStream stream, int index)
    {
        PdfObject? parmsObj = ResolveObject(stream.Dictionary.Get("DecodeParms"));
        return parmsObj switch
        {
            PdfArray arr => index < arr.Count ? ResolveObject(arr[index]) : null,
            _ => index == 0 ? parmsObj : null,
        };
    }

    // The default Crypt filter name is Identity (ISO 32000-1 7.4.10), so absent or Name-less decode
    // parameters mean Identity; only an explicit non-Identity name is a violation. Mirrors
    // StreamFiltersRule.IsIdentityCrypt exactly.
    private static bool IsIdentityCrypt(PdfObject? parms) => parms switch
    {
        null => true,
        PdfDictionary d => d.Get("Name") is not PdfName n || n.Value == "Identity",
        _ => true,
    };

    /// <summary>The one classifier both <see cref="PreviewStreamFilterRepairs"/> and a future write
    /// side use, so the preview and the write can never disagree about what would happen to a given
    /// stream. Returns true when the stream is convertible; otherwise appends a refusal to
    /// <paramref name="refusals"/> IF the stream is actually in violation, and returns false. A
    /// conforming stream produces neither.
    ///
    /// <para>The three outcomes are exhaustive over what <c>StreamFiltersRule.Check</c> can raise:
    /// convertible (LZW that decodes), refused (a disallowed non-LZW filter, or LZW whose data will not
    /// decode), or not in violation. That exhaustiveness is the property image-dictionary had to be
    /// CORRECTED into having -- a violation that produced neither a candidate nor a refusal read as
    /// "nothing wrong" to a caller checking only those two lists.</para></summary>
    private bool ClassifyStreamFilters(PdfStream stream, List<StreamFilterRefusal> refusals)
    {
        IReadOnlyList<string> chain = FilterChainOf(stream);
        if (chain.Count == 0) return false;

        var disallowed = new List<string>();
        for (var i = 0; i < chain.Count; i++)
        {
            string name = chain[i];
            bool ok = PermittedFilters.Contains(name)
                      || (name == "Crypt" && IsIdentityCrypt(DecodeParmsAt(stream, i)));
            if (!ok) disallowed.Add(name);
        }

        if (disallowed.Count == 0) return false;

        // Anything other than LZW cannot be fixed by re-encoding: a non-Identity /Crypt needs the
        // document's security handler, and an unknown filter has no decoder at all. Name every one of
        // them, not just the first -- one stream can carry more than one offending filter, and telling
        // the user about one while silently dropping the other is the Minor the image-dictionary
        // whole-branch review raised against exactly this shape.
        List<string> notLzw = [.. disallowed.Where(n => n != LzwFilter).Distinct(StringComparer.Ordinal)];
        if (notLzw.Count > 0)
        {
            refusals.Add(new StreamFilterRefusal(
                stream.ObjectNumber,
                $"This stream uses {string.Join(", ", notLzw.Select(n => "/" + n))}, which PDF/A does "
                + "not permit and Pellucid cannot convert: unlike /LZWDecode, there is no equivalent "
                + "permitted encoding to rewrite it as."));
            return false;
        }

        // LZW only. Prove the data actually decodes BEFORE promising a conversion -- a corrupt stream
        // must be refused with a reason, not discovered at save time when the write is already underway.
        try
        {
            _ = stream.GetDecodedData(_document.Decryptor);
        }
        catch (Exception ex)
        {
            refusals.Add(new StreamFilterRefusal(
                stream.ObjectNumber,
                "This stream declares /LZWDecode but its data could not be decoded, so Pellucid cannot "
                + $"safely re-encode it: {ex.Message}"));
            return false;
        }

        return true;
    }

    /// <summary>Read-only preview of every 6.1.7.2 stream-filter defect this editor would repair right
    /// now, without writing anything. Calling it twice returns the same answer; there is no idempotency
    /// guard to trip because nothing here is ever written. This is what a Pellucid domain's
    /// <c>Propose</c> calls -- <c>Propose</c> must never call a mutating write counterpart, which a
    /// sibling domain once did and had graded Critical.</summary>
    public StreamFilterRepairPreview PreviewStreamFilterRepairs()
    {
        var candidates = new List<StreamFilterRepairCandidate>();
        var refusals = new List<StreamFilterRefusal>();

        foreach (PdfStream stream in EnumerateAllStreams())
            if (ClassifyStreamFilters(stream, refusals))
                candidates.Add(new StreamFilterRepairCandidate(stream.ObjectNumber, FilterChainOf(stream)));

        return new StreamFilterRepairPreview(candidates, refusals);
    }
}
