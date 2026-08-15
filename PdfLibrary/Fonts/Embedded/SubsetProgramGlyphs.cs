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

    /// <summary>The CIDs a <c>/CIDSet</c> stream DECLARES: bit <c>i</c> (MSB-first within each byte)
    /// set ⇒ CID <c>i</c> is declared present.
    ///
    /// <para>Shared with <c>FontSubsetCoverageRule</c> for the same reason the program enumerations
    /// above are: F-3's repair must decide "is this declared entry surplus?" against exactly the
    /// declaration the rule read, or it can decline a font the rule never faulted — or, worse,
    /// rewrite one it did.</para></summary>
    public static HashSet<int> DeclaredCids(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var set = new HashSet<int>();
        for (var i = 0; i < bytes.Length; i++)
            for (var bit = 0; bit < 8; bit++)
                if ((bytes[i] & (0x80 >> bit)) != 0)
                    set.Add(i * 8 + bit);
        return set;
    }

    /// <summary>The glyph names a <c>/CharSet</c> string DECLARES — a run of PDF name tokens (e.g.
    /// <c>/slash/C/space</c>), matching veraPDF, which tokenises it as PDF name objects and collects
    /// each. A name runs from a <c>/</c> to the next PDF whitespace or delimiter. Shared with
    /// <c>FontSubsetCoverageRule</c> for the reason given on <see cref="DeclaredCids"/>.</summary>
    public static HashSet<string> DeclaredGlyphNames(string charSet)
    {
        ArgumentNullException.ThrowIfNull(charSet);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var i = 0;
        while (i < charSet.Length)
        {
            if (charSet[i] != '/')
            {
                i++;
                continue;
            }
            int start = ++i;
            while (i < charSet.Length && !IsNameDelimiter(charSet[i]))
                i++;
            if (i > start)
                names.Add(charSet.Substring(start, i - start));
        }
        return names;
    }

    private static bool IsNameDelimiter(char c) =>
        c is '/' or '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '%'
        || c is ' ' or '\t' or '\r' or '\n' or '\f' or '\0';

    private static PdfObject? Resolve(PdfDocument document, PdfObject? obj) =>
        obj is PdfIndirectReference reference ? document.GetObject(reference.ObjectNumber) : obj;
}
