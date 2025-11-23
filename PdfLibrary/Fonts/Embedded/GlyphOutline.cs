namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Represents a complete glyph outline with contours and metrics.
    /// Used for vector rendering of embedded font glyphs.
    /// </summary>
    public class GlyphOutline
    {
        /// <summary>
        /// Glyph ID (index in font's glyph table)
        /// </summary>
        public int GlyphId { get; }

        /// <summary>
        /// List of contours that make up this glyph.
        /// Simple glyphs have 1+ contours, composite glyphs reference other glyphs.
        /// </summary>
        public List<GlyphContour> Contours { get; }

        /// <summary>
        /// Glyph metrics (advance width, left side bearing, bounding box)
        /// </summary>
        public GlyphMetrics Metrics { get; }

        /// <summary>
        /// True if this is a composite glyph (references other glyphs)
        /// </summary>
        public bool IsComposite { get; }

        /// <summary>
        /// For composite glyphs, list of referenced glyph IDs
        /// </summary>
        public List<int> ComponentGlyphIds { get; }

        public GlyphOutline(
            int glyphId,
            List<GlyphContour> contours,
            GlyphMetrics metrics,
            bool isComposite = false,
            List<int>? componentGlyphIds = null)
        {
            GlyphId = glyphId;
            Contours = contours;
            Metrics = metrics;
            IsComposite = isComposite;
            ComponentGlyphIds = componentGlyphIds ?? [];
        }

        /// <summary>
        /// Check if glyph is empty (space character, etc.)
        /// </summary>
        public bool IsEmpty => Contours.Count == 0 && !IsComposite;

        /// <summary>
        /// Total number of points across all contours
        /// </summary>
        public int TotalPoints => Contours.Sum(c => c.Points.Count);

        public override string ToString()
        {
            if (IsEmpty)
                return $"Glyph {GlyphId}: Empty (advance={Metrics.AdvanceWidth})";

            if (IsComposite)
                return $"Glyph {GlyphId}: Composite with {ComponentGlyphIds.Count} components";

            return $"Glyph {GlyphId}: {Contours.Count} contours, {TotalPoints} points, advance={Metrics.AdvanceWidth}";
        }
    }
}
