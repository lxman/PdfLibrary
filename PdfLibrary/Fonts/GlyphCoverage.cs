using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// Which codes a font draws would be lost if a given program were embedded for it.
///
/// <para>This matters more at embed time than at render time. A glyph missing from a render is
/// transient — reopening on a better-equipped machine fixes it. A glyph missing from an EMBEDDED
/// program is <c>.notdef</c> baked into the file permanently.</para>
///
/// <para>Only codes whose Unicode is derivable are probed. A code the encoding cannot resolve is
/// not reported: there is nothing to check it against, and a guess would be a false alarm that
/// blocks a legitimate fix.</para>
/// </summary>
internal static class GlyphCoverage
{
    public static IReadOnlyList<int> UncoveredCodes(
        PdfFont font, EmbeddedFontMetrics candidate, int firstChar, int lastChar)
    {
        var missing = new List<int>();
        int lo = Math.Max(0, firstChar);
        int hi = Math.Min(255, lastChar);

        for (int code = lo; code <= hi; code++)
        {
            string? glyphName = font.Encoding?.GetGlyphName(code);
            if (string.IsNullOrEmpty(glyphName) || glyphName == ".notdef") continue;

            string? uni = GlyphList.GetUnicode(glyphName);
            if (string.IsNullOrEmpty(uni)) continue;

            int cp = char.ConvertToUtf32(uni, 0);
            if (cp is <= 0 or > 0xFFFF) continue;   // BMP probe only

            (ushort gid, _) = candidate.TestCmapLookup((ushort)cp);
            if (gid == 0) missing.Add(code);
        }

        return missing;
    }
}
