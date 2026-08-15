using PdfLibrary.Core;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Structure;

namespace PdfLibrary.Fonts.Embedded;

/// <summary>The glyphs an embedded subset program actually contains — the truth a <c>/CharSet</c> or
/// <c>/CIDSet</c> declaration is supposed to describe.
///
/// <para>Shared deliberately between <c>FontSubsetCoverageRule</c>, which compares a declaration
/// against this, and F-3's repair, which writes a declaration from it. The rule's comparison is
/// bidirectional, so two independent enumerations that disagree anywhere would produce a repair that
/// writes a declaration the rule still faults: a fix reporting success while the finding stands.
/// One implementation makes that disagreement impossible rather than merely unlikely.</para></summary>
internal static class SubsetProgramGlyphs
{
    /// <summary>The glyph names a Type1 or Type1C program contains, or null when they cannot be
    /// enumerated (which callers must treat as "do not touch this font").</summary>
    public static IReadOnlySet<string>? ProgramGlyphNames(EmbeddedFontMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        return metrics.EnumerateProgramGlyphNames();
    }

    /// <summary>The CIDs a CIDFontType2 TrueType program contains, and its containment predicate,
    /// matching veraPDF's CIDFontType2Program: with an Identity CIDToGIDMap the CIDs are
    /// <c>[0, numberOfHMetrics)</c> and a CID is contained iff it is non-zero and below the glyph
    /// count; with a custom CIDToGIDMap stream the CIDs come from the mapping (each in-range CID whose
    /// GID is below the glyph count).</summary>
    public static (IReadOnlySet<int>? Cids, Func<int, bool> Contains) ProgramCids(
        PdfDocument document, PdfDictionary cidDict, EmbeddedFontMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(cidDict);
        ArgumentNullException.ThrowIfNull(metrics);

        int numGlyphs = metrics.NumGlyphs;
        if (Resolve(document, cidDict.Get("CIDToGIDMap")) is PdfStream mapStream)
        {
            byte[] data = mapStream.GetDecodedData(document.Decryptor);
            int mappingSize = data.Length / 2;
            int Gid(int cid) => cid >= 0 && cid < mappingSize ? (data[cid * 2] << 8) | data[cid * 2 + 1] : 0;
            bool Contains(int cid) => cid >= 1 && cid < mappingSize && Gid(cid) < numGlyphs;
            var cids = new HashSet<int>();
            for (var cid = 0; cid < mappingSize; cid++)
                if (Contains(cid))
                    cids.Add(cid);
            return (cids, Contains);
        }

        var identity = new HashSet<int>();
        for (var cid = 0; cid < metrics.NumberOfHMetrics; cid++)
            identity.Add(cid);
        return (identity, cid => cid != 0 && cid < numGlyphs);
    }

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;
}
