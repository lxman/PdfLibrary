namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Extracts and parses embedded fonts from PDF font descriptors
    /// Supports TrueType (FontFile2) fonts for glyph name extraction
    /// Used as fallback when ToUnicode CMap is missing or incomplete
    /// </summary>
    public class EmbeddedFontExtractor
    {
        private readonly Dictionary<int, string>? _glyphNames;
        private readonly bool _isValid;

        /// <summary>
        /// Extract embedded font from font descriptor and parse glyph names
        /// </summary>
        /// <param name="fontDescriptor">PDF font descriptor containing embedded font</param>
        public EmbeddedFontExtractor(PdfFontDescriptor? fontDescriptor)
        {
            if (fontDescriptor == null)
            {
                _isValid = false;
                return;
            }

            // Try to extract FontFile2 (TrueType) stream
            byte[]? fontData = fontDescriptor.GetFontFile2();
            if (fontData != null)
            {
                // Parse TrueType font for glyph names
                _glyphNames = TrueTypeParser.GetAllGlyphNames(fontData);
                _isValid = _glyphNames is { Count: > 0 };
                return;
            }

            // TODO: Add FontFile3 (CFF/OpenType) support in future
            // TODO: Add FontFile (Type1) support in future

            _isValid = false;
        }

        /// <summary>
        /// Check if embedded font was successfully extracted and parsed
        /// </summary>
        public bool IsValid => _isValid;

        /// <summary>
        /// Get glyph name for a character code (CID)
        /// </summary>
        /// <param name="charCode">Character code from PDF content stream</param>
        /// <returns>Glyph name (e.g., "fi", "Aacute"), or null if not found</returns>
        public string? GetGlyphName(int charCode)
        {
            if (!_isValid || _glyphNames == null)
                return null;

            // For Type0 fonts, character code typically maps directly to glyph ID
            return _glyphNames.TryGetValue(charCode, out string? glyphName) ? glyphName : null;
        }

        /// <summary>
        /// Get Unicode string for a character code using embedded font fallback
        /// </summary>
        /// <param name="charCode">Character code from PDF content stream</param>
        /// <returns>Unicode string (e.g., "fi" for fi ligature), or null if not found</returns>
        public string? GetUnicodeFromGlyphName(int charCode)
        {
            string? glyphName = GetGlyphName(charCode);
            if (glyphName == null)
                return null;

            // Map glyph name to Unicode using Adobe Glyph List
            return AdobeGlyphList.GetUnicode(glyphName);
        }

        /// <summary>
        /// Get all glyph names from embedded font
        /// </summary>
        /// <returns>Dictionary of glyph ID → glyph name, or null if no font extracted</returns>
        public IReadOnlyDictionary<int, string>? GetAllGlyphNames()
        {
            return _glyphNames;
        }

        /// <summary>
        /// Get diagnostic information about extracted font
        /// </summary>
        public string GetDiagnosticInfo()
        {
            if (!_isValid)
                return "No embedded font found or parsing failed";

            int glyphCount = _glyphNames?.Count ?? 0;
            return $"TrueType font with {glyphCount} glyphs extracted";
        }
    }
}
