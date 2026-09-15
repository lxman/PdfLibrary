using PdfLibrary.Fonts.Embedded;

namespace PdfLibrary.Fonts;

/// <summary>
/// Resolves a simple TrueType PDF character code to the embedded program glyph selected by the
/// PDF encoding. Rendering, conformance, and remediation must share this decision: a disagreement
/// can make a visible glyph disappear while the font checks inspect a different one or skip it.
/// </summary>
internal static class TrueTypeGlyphResolver
{
    public static ushort Resolve(PdfFont font, EmbeddedFontMetrics metrics, int code)
    {
        if (code is < 0 or > ushort.MaxValue)
            return 0;

        // A symbolic PDF font backed by a Windows-Symbol cmap uses the conventional F000+code
        // mapping. Its PDF /Encoding is not authoritative.
        if (font.GetDescriptor()?.IsSymbolic == true && metrics.HasSymbolCmapEncoding())
            return metrics.GetGlyphIdBySymbolCode((ushort)code);

        // For a non-symbolic simple font, the PDF encoding maps the byte to a glyph name. AGL maps
        // that name to Unicode, and EmbeddedFontMetrics then selects the proper cmap key space
        // (Unicode, MacRoman, etc.). Feeding the raw PDF byte directly to a legacy cmap is wrong
        // above ASCII whenever the two encodings differ.
        string? glyphName = font.Encoding?.GetGlyphName(code);
        string? unicode = glyphName is null ? null : GlyphList.GetUnicode(glyphName);
        if (!string.IsNullOrEmpty(unicode))
        {
            int codePoint = char.ConvertToUtf32(unicode, 0);
            ushort encodedGlyph = metrics.GetGlyphIdByUnicode(codePoint);
            if (encodedGlyph != 0)
                return encodedGlyph;
        }

        // Preserve the established fallback for malformed/legacy fonts whose cmap really is keyed
        // by the raw PDF byte or whose /Encoding cannot be interpreted.
        return metrics.GetGlyphId((ushort)code);
    }
}
