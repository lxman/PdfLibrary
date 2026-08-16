using System;
using System.Globalization;
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

    /// <summary>U+FFFD, GlyphList's <c>.notdef</c> marker. Shared so callers deriving a value from a
    /// glyph name (not just this class's own boolean check) can apply the same exclusion.</summary>
    internal const char ReplacementChar = (char)0xFFFD;

    /// <summary>True when <paramref name="code"/> has — or may plausibly have — a Unicode mapping in
    /// <paramref name="font"/>. Returns false only on positive evidence that no mapping exists.</summary>
    public static bool HasReliableUnicode(ConformanceContext context, PdfFont font, int code)
    {
        if (font.ToUnicode?.Lookup(code) is not null)
            return true;

        // Type0 (composite) fonts: only an Identity-ordered CID font has no derivable CID-to-Unicode mapping
        // without /ToUnicode. A registered Adobe collection (Japan1/Korea1/GB1/CNS1) carries a mapping through
        // Adobe's cid2unicode tables — which are bundled for extraction (AdobeCidToUnicode); this rule stays conservative regardless.
        if (font is Type0Font type0)
            return !IsIdentityOrdering(context, type0);

        // Simple font: a code with no PDF-level glyph name may still be mapped by the embedded font
        // program's built-in encoding. For a symbolic Type1/CFF font this engine DOES read that
        // encoding (Type1Font.LoadEncoding / EmbeddedFontMetrics.GetCffGlyphNameByCharCode /
        // GetType1GlyphNameByCharCode, ISO 32000-1 9.6.6.2), so a null name here means the built-in
        // encoding already had nothing to say either; other shapes (TrueType symbolic cmaps,
        // non-symbolic gaps) still fall outside what this engine models, so an empty/.notdef name
        // stays conservatively non-failing rather than a positive failure.
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
    // SYNTAX ONLY — deliberately does not check whether the hex digits form a valid Unicode scalar
    // value. This is what HasReliableUnicode consults, and this rule is conservative BY DESIGN: it
    // "flags only positive evidence of no mapping" (see the class doc), so a name that merely LOOKS
    // like the convention gets the benefit of the doubt even if its code point turns out to be a
    // surrogate or out of range — rejecting it here would be an unrequested tightening of a rule
    // whose whole point is to avoid false positives.
    private static bool IsUnicodeGlyphName(string name)
    {
        if (name.Length is < 5 or > 7 || name[0] != 'u')
            return false;
        for (int i = 1; i < name.Length; i++)
            if (!Uri.IsHexDigit(name[i]))
                return false;
        return true;
    }

    /// <summary>
    /// The "uXXXXXX" convention: a literal 'u' followed by 4–6 hex digits, taken directly as the
    /// Unicode scalar value ("uniXXXX" is a different convention, already resolved by
    /// <see cref="GlyphList.GetUnicode"/>). Null when <paramref name="name"/> does not match the
    /// syntax, OR the hex digits do not form a valid Unicode scalar value (a surrogate code point
    /// D800–DFFF, or a value above 10FFFF — e.g. <c>uD800</c>, <c>uDFFF</c>, <c>u110000</c>,
    /// <c>uFFFFFF</c>).
    ///
    /// <para><b>Deliberately STRICTER than <see cref="IsUnicodeGlyphName"/>/<see cref="HasReliableUnicode"/>.</b>
    /// The two ask different questions: <c>HasReliableUnicode</c> asks "is this positive evidence of
    /// no mapping" (syntax alone answers that — a name that merely fits the pattern earns the
    /// conservative rule's benefit of the doubt), while this method asks "do I have an actual value I
    /// can propose" (syntax fitting the pattern is not enough if the resulting code point cannot
    /// exist — <see cref="char.ConvertFromUtf32(int)"/> would throw). A syntactically-valid-but-out-of-range
    /// name such as <c>uD800</c> therefore returns <c>true</c> from <c>IsUnicodeGlyphName</c> but
    /// <c>null</c> from here — an EARLIER version of this method collapsed the two into one, which
    /// silently tightened the conformance rule (four such names newly failed <c>pdfa2u-tounicode</c>
    /// with no fixture ever exercising the difference); they must stay separate, sharing only the
    /// syntactic prefix check.</para>
    /// </summary>
    internal static string? UnicodeGlyphNameValue(string name)
    {
        if (!IsUnicodeGlyphName(name))
            return null;

        if (!int.TryParse(name.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
            return null;

        try
        {
            return char.ConvertFromUtf32(codePoint);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // a syntactically-valid hex run that is not a valid Unicode scalar value
        }
    }
}
