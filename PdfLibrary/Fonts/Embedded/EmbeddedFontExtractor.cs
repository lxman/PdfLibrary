namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Extracts glyph names from an embedded <c>/FontFile2</c> (TrueType) post table, as a
    /// last-resort code→Unicode fallback for a <see cref="Type0Font"/> whose <c>/ToUnicode</c> CMap
    /// is missing or incomplete.
    /// <para>
    /// Scope is deliberately narrow, and narrower than it looks: this is constructed only from
    /// <c>Type0Font.LoadEmbeddedFont()</c> with the descendant CIDFont's descriptor, so
    /// <c>/FontFile2</c> is the only font-file flavour that both appears here and carries glyph
    /// names. See the constructor for why <c>/FontFile3</c> and <c>/FontFile</c> are not simply
    /// unimplemented. This affects text extraction (copy/search) only — glyph RENDERING resolves
    /// CFF/Type1 independently via <c>GlyphOutline</c> and does not use this class.
    /// </para>
    /// </summary>
    internal class EmbeddedFontExtractor
    {
        private readonly IReadOnlyDictionary<int, string>? _glyphNames;
        private readonly bool _isValid;

        /// <summary>
        /// Extract embedded font from font descriptor and parse glyph names
        /// </summary>
        /// <param name="fontDescriptor">PDF font descriptor containing embedded font</param>
        public EmbeddedFontExtractor(PdfFontDescriptor? fontDescriptor)
        {
            if (fontDescriptor is null)
            {
                _isValid = false;
                return;
            }

            // Try to extract FontFile2 (TrueType) stream
            byte[]? fontData = fontDescriptor.GetFontFile2();
            if (fontData is not null)
            {
                // Parse the embedded sfnt and read post-table glyph names via FontParser.
                try { _glyphNames = new FontParser.SfntFont(fontData).Post?.GlyphNames; }
                catch { _glyphNames = null; }
                _isValid = _glyphNames is { Count: > 0 };
                return;
            }

            // NOT a TODO — the two obvious "missing" cases are mostly unreachable here, and the
            // TODOs that used to sit on this line sent a backlog sweep scoping work that cannot pay
            // off. This type is constructed from ONE place: Type0Font.LoadEmbeddedFont(), using the
            // DESCENDANT CIDFont's descriptor. So the descriptor is always a CIDFont's, and per
            // ISO 32000-2 §9.7.4.2 a CIDFont carries only:
            //
            //   CIDFontType2 -> /FontFile2 (TrueType)  — handled above, via post-table names.
            //   CIDFontType0 -> /FontFile3, Subtype /CIDFontType0C or /OpenType.
            //
            // A CID-keyed CFF HAS NO GLYPH NAMES: its charset maps GID -> CID, not GID -> SID.
            // That is the point of CID-keying (see FontParser Type1Table.IsCid). There is nothing
            // to extract, so /FontFile3 support would return null here anyway for conformant files.
            //
            // /FontFile (Type1) is only valid on a SIMPLE font descriptor, never on a CIDFont, so
            // it should not reach this constructor at all.
            //
            // The name-keyed CFF that does carry usable names is Subtype /Type1C, which appears on
            // SIMPLE font descriptors — and simple fonts deliberately do not use this class: their
            // text extraction reads glyph names from the PDF's own /Encoding and /Differences, so
            // the font program never needs cracking.
            //
            // The one genuinely reachable gap is a NAME-keyed CFF wrongly embedded as
            // /CIDFontType0C by a broken producer. Adding that needs a GID->name accessor on
            // FontParser's ICharset (today an empty marker interface) plus range expansion for
            // charset formats 1 and 2. Gated on evidence that such files exist in practice — do not
            // build it speculatively.
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
            if (!_isValid || _glyphNames is null)
                return null;

            // For Type0 fonts, character code typically maps directly to glyph ID
            return _glyphNames.GetValueOrDefault(charCode);
        }

        /// <summary>
        /// Get Unicode string for a character code using embedded font fallback
        /// </summary>
        /// <param name="charCode">Character code from PDF content stream</param>
        /// <returns>Unicode string (e.g., "fi" for fi ligature), or null if not found</returns>
        public string? GetUnicodeFromGlyphName(int charCode)
        {
            string? glyphName = GetGlyphName(charCode);
            if (glyphName is null)
                return null;

            // Map glyph name to Unicode using the Adobe Glyph List
            return GlyphList.GetUnicode(glyphName);
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
