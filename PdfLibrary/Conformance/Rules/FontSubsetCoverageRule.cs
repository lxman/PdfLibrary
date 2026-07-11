using System.Text.RegularExpressions;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;
using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Conformance.Rules;

/// <summary>
/// Subset CharSet/CIDSet completeness, shared by PDF/A-2 (ISO 19005-2, 6.2.11.4.2) and PDF/UA-1
/// (ISO 14289-1, 7.21.4.2) — identical sub-numbers, so one profile-aware rule serves both (like
/// <see cref="FontDictionaryRule"/> and <see cref="FontProgramRule"/>, which both list this clause as out of
/// scope; this rule fills that gap). Two checks, each firing ONLY for an embedded <b>subset</b> font
/// (BaseFont matches <c>^[A-Z]{6}\+</c>) whose descriptor carries the relevant entry:
/// <list type="number">
///   <item><b>test 1 — simple Type1 /CharSet:</b> the FontDescriptor <c>/CharSet</c> string must list the
///     names of exactly the glyphs present in the embedded program (a classic Type1 <c>/FontFile</c> or a
///     Type1C <c>/FontFile3</c> CFF), ignoring <c>.notdef</c>.</item>
///   <item><b>test 2 — CID /CIDSet:</b> the FontDescriptor <c>/CIDSet</c> bitmap must identify exactly the
///     CIDs present in the embedded program (a CID-keyed CFF <c>/FontFile3</c> via its charset, or a
///     CIDFontType2 TrueType <c>/FontFile2</c> via its CIDToGIDMap / metric count), ignoring CID 0.</item>
/// </list>
/// <para>
/// The comparison replicates veraPDF's model exactly (its <c>getcharSetListsAllGlyphs</c> /
/// <c>getcidSetListsAllGlyphs</c>): a <b>bidirectional</b> match — every declared entry must be in the
/// program AND every program entry must be declared — because the reference validator flags both a missing
/// glyph (a program glyph absent from the declaration) and a surplus one (a declared glyph absent from the
/// program). PDF/X-4 is excluded (ISO 15930-7 carries no such constraint — same reasoning as the sibling
/// font rules). Every check is FP-safe: a non-subset font, a font with no CharSet/CIDSet, a non-embedded
/// font, or a program that will not parse (or whose glyph set cannot be enumerated) is skipped, never
/// guessed at. One finding per font.
/// </para>
/// </summary>
internal sealed class FontSubsetCoverageRule : IConformanceRule
{
    public string RuleId => "font-subset-coverage";

    // Shared with PDF/UA-1 (7.21); PDF/X-4 is intentionally excluded.
    public ConformanceProfile AppliesToProfiles => ConformanceProfile.AllPdfA | ConformanceProfile.PdfUA1;

    // A subset font tags its BaseFont with a six-uppercase-letter prefix and a '+' (ISO 32000-1 9.6.4).
    private static readonly Regex SubsetPrefix = new("^[A-Z]{6}\\+", RegexOptions.Compiled);

    public IEnumerable<Finding> Check(ConformanceContext context)
    {
        foreach (PdfDictionary font in context.ReferencedFonts)
        {
            // The Type0 wrapper drives the CID check (its descendant CIDFont carries the /CIDSet); the
            // descendant's own entry in ReferencedFonts is a CIDFontType0/2 subtype and is skipped here.
            switch (context.ResolveName(font.Get("Subtype")))
            {
                case "Type1":
                    foreach (Finding f in CheckType1(context, font))
                        yield return f;
                    break;
                case "Type0":
                    foreach (Finding f in CheckCid(context, font))
                        yield return f;
                    break;
            }
        }
    }

    // ── test 1 — simple Type1 /CharSet completeness (6.2.11.4.2 / 7.21.4.2-1) ────────────────────────────
    private IEnumerable<Finding> CheckType1(ConformanceContext context, PdfDictionary fontDict)
    {
        if (!IsSubset(context, fontDict))
            yield break; // !subsetName → pass
        if (context.Resolve(fontDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
            yield break;
        if (context.Resolve(descriptor.Get("CharSet")) is not PdfString charSet)
            yield break; // CharSet == null → pass

        // containsFontFile == false → pass. veraPDF's containsFontFile is "font program present AND parsed",
        // so a null/invalid program (or one whose glyph names cannot be enumerated) is skipped — FP-safe.
        if (PdfFont.Create(fontDict, context.Document)?.GetEmbeddedMetrics() is not { IsValid: true } metrics)
            yield break;
        if (metrics.EnumerateProgramGlyphNames() is not { } programGlyphs)
            yield break;

        if (!GlyphNamesAgree(ParseNameSet(charSet.Value), programGlyphs))
        {
            yield return Make(context, fontDict,
                "The Type1 font's /CharSet does not list all glyphs present in the embedded subset program.");
        }
    }

    // ── test 2 — CID /CIDSet completeness (6.2.11.4.2 / 7.21.4.2-2) ──────────────────────────────────────
    private IEnumerable<Finding> CheckCid(ConformanceContext context, PdfDictionary type0Dict)
    {
        if (PdfFont.Create(type0Dict, context.Document) is not Type0Font type0)
            yield break;
        if (type0.DescendantCidFontDictionary is not { } cidDict)
            yield break;
        if (!IsSubset(context, type0Dict))
            yield break; // !subsetName → pass (the Type0 and its CIDFont share the BaseFont)
        if (context.Resolve(cidDict.Get("FontDescriptor")) is not PdfDictionary descriptor)
            yield break;
        if (context.Resolve(descriptor.Get("CIDSet")) is not PdfStream cidSetStream)
            yield break; // containsCIDSet == false → pass

        if (type0.GetEmbeddedMetrics() is not { IsValid: true } metrics)
            yield break; // containsFontFile == false → pass (FP-safe)

        IReadOnlySet<int> cidSet = DecodeCidSet(cidSetStream.GetDecodedData(context.Document.Decryptor));

        // The set of CIDs the program contains, and the "is CID i in the program" predicate — computed the
        // way veraPDF's font-program parser does, per descendant subtype.
        IReadOnlySet<int>? programCids;
        Func<int, bool> containsCid;
        switch (context.ResolveName(cidDict.Get("Subtype")))
        {
            case "CIDFontType0":
                // CID-keyed CFF: the charset maps GID→CID, so its CIDs are exactly the program's.
                if (metrics.EnumerateProgramCids() is not { } cffCids)
                    yield break; // not a CID-keyed CFF we can enumerate → FP-safe skip
                programCids = cffCids;
                containsCid = cffCids.Contains;
                break;
            case "CIDFontType2":
                (programCids, containsCid) = BuildTrueTypeCidSet(context, cidDict, metrics);
                if (programCids is null)
                    yield break;
                break;
            default:
                yield break;
        }

        if (!CidsAgree(programCids, cidSet, containsCid))
        {
            yield return Make(context, cidDict,
                "The CID font's /CIDSet does not identify all glyphs present in the embedded subset program.");
        }
    }

    /// <summary>
    /// The CIDs a CIDFontType2 TrueType program contains, and its containment predicate, matching veraPDF's
    /// CIDFontType2Program: with an Identity CIDToGIDMap the CIDs are <c>[0, numberOfHMetrics)</c> and a CID
    /// is contained iff it is non-zero and below the glyph count; with a custom CIDToGIDMap stream the CIDs
    /// come from the mapping (each in-range CID whose GID is below the glyph count).
    /// </summary>
    private static (IReadOnlySet<int>? Cids, Func<int, bool> Contains) BuildTrueTypeCidSet(
        ConformanceContext context, PdfDictionary cidDict, EmbeddedFontMetrics metrics)
    {
        int numGlyphs = metrics.NumGlyphs;
        if (context.Resolve(cidDict.Get("CIDToGIDMap")) is PdfStream mapStream)
        {
            byte[] data = mapStream.GetDecodedData(context.Document.Decryptor);
            int mappingSize = data.Length / 2;
            int Gid(int cid) => cid >= 0 && cid < mappingSize ? (data[cid * 2] << 8) | data[cid * 2 + 1] : 0;
            bool Contains(int cid) => cid >= 1 && cid < mappingSize && Gid(cid) < numGlyphs;
            var cids = new HashSet<int>();
            for (int cid = 0; cid < mappingSize; cid++)
                if (Contains(cid))
                    cids.Add(cid);
            return (cids, Contains);
        }

        // Identity CIDToGIDMap (an explicit /Identity name or none): CID == GID.
        var identity = new HashSet<int>();
        for (int cid = 0; cid < metrics.NumberOfHMetrics; cid++)
            identity.Add(cid);
        return (identity, cid => cid != 0 && cid < numGlyphs);
    }

    // ── comparisons (replicating veraPDF's model exactly) ───────────────────────────────────────────────

    /// <summary>
    /// True when the declared /CharSet names and the program's glyph names describe the same set (ignoring
    /// <c>.notdef</c>) — veraPDF's <c>getcharSetListsAllGlyphs</c>: a size guard (the program may carry one
    /// extra name, its <c>.notdef</c>) plus bidirectional containment. Returns false ⇒ the rule flags.
    /// </summary>
    private static bool GlyphNamesAgree(IReadOnlySet<string> declared, IReadOnlySet<string> program)
    {
        if (!(declared.Count == program.Count || declared.Count == program.Count - 1))
            return false;
        foreach (string name in declared)
            if (name != ".notdef" && !program.Contains(name))
                return false;
        foreach (string name in program)
            if (name != ".notdef" && !declared.Contains(name))
                return false;
        return true;
    }

    /// <summary>
    /// True when the declared /CIDSet bits and the program's CIDs describe the same set (ignoring CID 0) —
    /// veraPDF's <c>getcidSetListsAllGlyphs</c>: every program CID must have its bit set, and every set bit
    /// must correspond to a CID the program contains. Returns false ⇒ the rule flags.
    /// </summary>
    private static bool CidsAgree(IReadOnlySet<int> programCids, IReadOnlySet<int> cidSet, Func<int, bool> containsCid)
    {
        foreach (int cid in programCids)
            if (cid != 0 && !cidSet.Contains(cid))
                return false;
        foreach (int cid in cidSet)
            if (cid != 0 && !containsCid(cid))
                return false;
        return true;
    }

    // ── parsing helpers ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a /CharSet string — a run of PDF name tokens (e.g. <c>/slash/C/space</c>) — into the set of
    /// glyph names, matching veraPDF (which tokenises it as PDF name objects and collects each). A name runs
    /// from a <c>/</c> to the next PDF whitespace or delimiter.
    /// </summary>
    private static HashSet<string> ParseNameSet(string charSet)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        int i = 0;
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

    /// <summary>
    /// Decodes a /CIDSet stream into the set of CIDs it identifies: bit <c>i</c> (MSB-first within each byte)
    /// set ⇒ CID <c>i</c> is present.
    /// </summary>
    private static HashSet<int> DecodeCidSet(byte[] bytes)
    {
        var set = new HashSet<int>();
        for (int i = 0; i < bytes.Length; i++)
            for (int bit = 0; bit < 8; bit++)
                if ((bytes[i] & (0x80 >> bit)) != 0)
                    set.Add(i * 8 + bit);
        return set;
    }

    private static bool IsSubset(ConformanceContext context, PdfDictionary fontDict) =>
        context.ResolveName(fontDict.Get("BaseFont")) is { } baseFont && SubsetPrefix.IsMatch(baseFont);

    private Finding Make(ConformanceContext context, PdfDictionary offender, string message) =>
        new()
        {
            RuleId = RuleId,
            Severity = FindingSeverity.Error,
            Clause = ConformanceClauses.For(context.Target,
                context.Target == ConformanceProfile.PdfUA1 ? "7.21.4.2" : "6.2.11.4.2"),
            Message = message,
            ObjectNumber = offender.IsIndirect ? offender.ObjectNumber : null,
        };
}
