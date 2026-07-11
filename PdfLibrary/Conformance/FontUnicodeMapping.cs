using System;
using PdfLibrary.Core.Primitives;
using PdfLibrary.Fonts;

namespace PdfLibrary.Conformance;

/// <summary>
/// Decides whether a font maps a character code to Unicode by one of the mechanisms PDF/A-2u accepts
/// (ISO 19005-2, 6.2.11.7.2): a <c>/ToUnicode</c> CMap entry, or — for a simple font — an encoding glyph
/// name that resolves to Unicode through the Adobe Glyph List or the <c>uniXXXX</c>/<c>uXXXXXX</c>
/// convention. It is deliberately <b>conservative</b>: it flags only positive evidence of no mapping, and
/// gives the benefit of the doubt to mappings that live in machinery this engine does not model — a
/// simple font's embedded program encoding, and the Adobe CID-to-Unicode tables of a registered CID
/// collection. This keeps the rule free of false positives at the cost of some recall (it does not, for
/// example, tell a Adobe-Japan1 CID font whose glyphs happen not to round-trip from a conformant one).
/// </summary>
internal static class FontUnicodeMapping
{
    private const char NullChar = (char)0x0000;         // U+0000
    private const char ByteOrderMark = (char)0xFEFF;    // U+FEFF (BOM / ZWNBSP)
    private const char NonCharacterFffe = (char)0xFFFE; // U+FFFE (a non-character)
    private const char NotACharacter = (char)0xFFFF;    // U+FFFF (a non-character)
    private const char ReplacementChar = (char)0xFFFD;  // U+FFFD (GlyphList's .notdef marker)

    /// <summary>True when <paramref name="code"/> has — or may plausibly have — a Unicode mapping in
    /// <paramref name="font"/>. Returns false only on positive evidence that no mapping exists.</summary>
    public static bool HasReliableUnicode(ConformanceContext context, PdfFont font, int code)
    {
        if (font.ToUnicode?.Lookup(code) is not null)
            return true;

        // Type0 (composite) fonts: only an Identity-ordered CID font has no derivable CID-to-Unicode mapping
        // without /ToUnicode. A registered Adobe collection (Japan1/Korea1/GB1/CNS1) carries a mapping through
        // Adobe's cid2unicode tables — which we do not bundle — so we do not flag it (benefit of the doubt).
        if (font is Type0Font type0)
            return !IsIdentityOrdering(context, type0);

        // Simple font: a code with no PDF-level glyph name may still be mapped by the embedded font program's
        // built-in encoding (which we do not read), so an empty/.notdef name is not treated as a failure.
        string? glyphName = font.Encoding?.GetGlyphName(code);
        if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef")
            return true;

        // A real glyph name is positive evidence: it maps to Unicode iff it is an AGL or uXXXX name.
        if (GlyphList.GetUnicode(glyphName) is { } unicode && !unicode.Contains(ReplacementChar))
            return true;

        return IsUnicodeGlyphName(glyphName);
    }

    /// <summary>The <c>/ToUnicode</c> value for the code, or null when the font has no entry for it.</summary>
    public static string? ToUnicodeValue(PdfFont font, int code) => font.ToUnicode?.Lookup(code);

    /// <summary>
    /// A <c>/ToUnicode</c> value PDF/A-2u forbids (ISO 19005-2, 6.2.11.7.2, second requirement): empty, or
    /// mapping to U+0000, U+FEFF, U+FFFE or U+FFFF.
    /// </summary>
    public static bool IsForbiddenUnicodeValue(string value) =>
        value.Length == 0
        || value.Contains(NullChar)
        || value.Contains(ByteOrderMark)
        || value.Contains(NonCharacterFffe)
        || value.Contains(NotACharacter);

    /// <summary>The code points a PDF/UA-1 <c>/ToUnicode</c> value must not contain (ISO 14289-1, 7.21.7,
    /// test 2): U+0000, U+FFFE, U+FEFF. This set is deliberately distinct from PDF/A-2u's
    /// (<see cref="IsForbiddenUnicodeValue"/>) — it excludes U+FFFF and does not fault an empty value
    /// (an unmapped glyph is the text-to-Unicode rule's concern, matching veraPDF's <c>toUnicode == null</c>
    /// short-circuit).</summary>
    public static readonly char[] PdfUa1ForbiddenCodePoints = [NullChar, NonCharacterFffe, ByteOrderMark];

    /// <summary>
    /// True when <paramref name="value"/> contains any of the <paramref name="forbidden"/> code points
    /// anywhere in the string. This is veraPDF's substring/<c>indexOf</c> semantics: a multi-code-point
    /// value (e.g. a ligature) is forbidden if <b>any</b> of its code points is forbidden. The caller
    /// supplies the profile-specific set (e.g. <see cref="PdfUa1ForbiddenCodePoints"/>), so this does not
    /// touch the PDF/A-2u behaviour of <see cref="IsForbiddenUnicodeValue"/>.
    /// </summary>
    public static bool ContainsForbiddenCodePoint(string value, ReadOnlySpan<char> forbidden)
    {
        foreach (char c in forbidden)
            if (value.Contains(c))
                return true;
        return false;
    }

    /// <summary>True when the composite font's descendant CIDFont uses the Adobe-Identity ordering, whose
    /// CIDs carry no inherent Unicode mapping. A missing/unreadable CIDSystemInfo is treated as non-Identity
    /// (not flagged) so an ambiguous font is never a false positive.</summary>
    private static bool IsIdentityOrdering(ConformanceContext context, Type0Font font)
    {
        if (font.DescendantCidFontDictionary is not { } cidFont
            || context.Resolve(cidFont.Get("CIDSystemInfo")) is not PdfDictionary systemInfo)
            return false;
        return (context.Resolve(systemInfo.Get("Ordering")) as PdfString)?.Value == "Identity";
    }

    // The "uXXXXXX" convention: 'u' followed by 4–6 hex digits. ("uniXXXX" is already resolved by GlyphList.)
    private static bool IsUnicodeGlyphName(string name)
    {
        if (name.Length is < 5 or > 7 || name[0] != 'u')
            return false;
        for (int i = 1; i < name.Length; i++)
            if (!Uri.IsHexDigit(name[i]))
                return false;
        return true;
    }
}
