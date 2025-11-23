using FontParser.Tables.Head;
using FontParser.Tables.Hhea;
using FontParser.Tables.Hmtx;
using FontParser.Tables.TtTables;
using FontParser.Tables.TtTables.Glyf;

namespace PdfLibrary.Fonts.Embedded
{
    /// <summary>
    /// Extracts glyph outlines from TrueType/OpenType fonts for vector rendering.
    /// Provides high-level API over low-level table parsers.
    /// </summary>
    public class GlyphExtractor
    {
        private readonly GlyphTable _glyfTable;
        private readonly LocaTable _locaTable;
        private readonly HmtxTable _hmtxTable;
        private readonly HeadTable _headTable;
        private readonly int _numGlyphs;

        /// <summary>
        /// Create glyph extractor from parsed font tables
        /// </summary>
        public GlyphExtractor(
            byte[] fontData,
            int numGlyphs)
        {
            _numGlyphs = numGlyphs;

            var parser = new TrueTypeParser(fontData);

            // Parse required tables
            byte[]? headData = parser.GetTable("head");
            if (headData == null)
                throw new InvalidOperationException("Font missing required 'head' table");

            _headTable = new HeadTable(headData);

            byte[]? locaData = parser.GetTable("loca");
            if (locaData == null)
                throw new InvalidOperationException("Font missing required 'loca' table");

            _locaTable = new LocaTable(locaData);
            bool isShortFormat = _headTable.IndexToLocFormat == IndexToLocFormat.Offset16;
            _locaTable.Process(_numGlyphs, isShortFormat);

            byte[]? glyfData = parser.GetTable("glyf");
            if (glyfData == null)
                throw new InvalidOperationException("Font missing required 'glyf' table");

            _glyfTable = new GlyphTable(glyfData);
            _glyfTable.Process(_numGlyphs, _locaTable);

            byte[]? hmtxData = parser.GetTable("hmtx");
            if (hmtxData == null)
                throw new InvalidOperationException("Font missing required 'hmtx' table");

            _hmtxTable = new HmtxTable(hmtxData);

            // Get number of horizontal metrics from hhea table
            byte[]? hheaData = parser.GetTable("hhea");
            if (hheaData != null)
            {
                var hheaTable = new HheaTable(hheaData);
                _hmtxTable.Process(hheaTable.NumberOfHMetrics, (ushort)_numGlyphs);
            }
            else
            {
                // Fallback: assume all glyphs have metrics
                _hmtxTable.Process((ushort)_numGlyphs, (ushort)_numGlyphs);
            }
        }

        /// <summary>
        /// Extract complete glyph outline with contours and metrics
        /// </summary>
        /// <param name="glyphId">Glyph ID (0-based index)</param>
        /// <returns>Glyph outline, or null if glyph not found</returns>
        public GlyphOutline? ExtractGlyph(int glyphId)
        {
            if (glyphId < 0 || glyphId >= _numGlyphs)
                return null;

            // Get glyph data from glyf table
            GlyphData? glyphData = _glyfTable.GetGlyphData(glyphId);

            // Get metrics from hmtx table
            GlyphMetrics metrics = GetMetrics(glyphId);

            // Handle empty glyphs (space, etc.)
            if (glyphData == null)
            {
                return new GlyphOutline(
                    glyphId,
                    new List<GlyphContour>(),
                    metrics,
                    isComposite: false
                );
            }

            // Check if composite or simple glyph
            if (glyphData.Header.NumberOfContours < 0)
            {
                // Composite glyph - references other glyphs
                return ExtractCompositeGlyph(glyphId, glyphData, metrics);
            }

            // Simple glyph - has direct contour data
            return ExtractSimpleGlyph(glyphId, glyphData, metrics);
        }

        /// <summary>
        /// Get glyph metrics (advance width, bearings, bounds)
        /// </summary>
        public GlyphMetrics GetMetrics(int glyphId)
        {
            if (glyphId < 0 || glyphId >= _numGlyphs)
            {
                return new GlyphMetrics(0, 0, 0, 0, 0, 0);
            }

            // Get horizontal metrics
            ushort advanceWidth = _hmtxTable.GetAdvanceWidth((ushort)glyphId);
            short lsb = _hmtxTable.GetLeftSideBearing((ushort)glyphId);

            // Get bounding box from glyph data
            GlyphData? glyphData = _glyfTable.GetGlyphData(glyphId);
            if (glyphData != null)
            {
                return new GlyphMetrics(
                    advanceWidth,
                    lsb,
                    glyphData.Header.XMin,
                    glyphData.Header.YMin,
                    glyphData.Header.XMax,
                    glyphData.Header.YMax
                );
            }

            // Empty glyph - no bounds
            return new GlyphMetrics(advanceWidth, lsb, 0, 0, 0, 0);
        }

        /// <summary>
        /// Extract simple glyph (has direct contour data)
        /// </summary>
        private GlyphOutline ExtractSimpleGlyph(int glyphId, GlyphData glyphData, GlyphMetrics metrics)
        {
            if (glyphData.GlyphSpec is not SimpleGlyph simpleGlyph)
            {
                // Unexpected - should be simple glyph
                return new GlyphOutline(glyphId, new List<GlyphContour>(), metrics);
            }

            var contours = new List<GlyphContour>();

            // Process each contour
            var startIndex = 0;
            for (var i = 0; i < simpleGlyph.EndPtsOfContours.Count; i++)
            {
                int endIndex = simpleGlyph.EndPtsOfContours[i];
                var points = new List<ContourPoint>();

                // Extract points for this contour
                for (int j = startIndex; j <= endIndex; j++)
                {
                    SimpleGlyphCoordinate coord = simpleGlyph.Coordinates[j];
                    points.Add(new ContourPoint(
                        coord.Point.X,
                        coord.Point.Y,
                        coord.OnCurve
                    ));
                }

                contours.Add(new GlyphContour(points, isClosed: true));
                startIndex = endIndex + 1;
            }

            return new GlyphOutline(
                glyphId,
                contours,
                metrics,
                isComposite: false
            );
        }

        /// <summary>
        /// Extract composite glyph (references other glyphs)
        /// </summary>
        private GlyphOutline ExtractCompositeGlyph(int glyphId, GlyphData glyphData, GlyphMetrics metrics)
        {
            if (glyphData.GlyphSpec is not CompositeGlyph compositeGlyph)
            {
                // Unexpected - should be composite glyph
                return new GlyphOutline(glyphId, new List<GlyphContour>(), metrics);
            }

            var allContours = new List<GlyphContour>();
            var componentIds = new List<int>();

            // Recursively extract and transform each component
            foreach (CompositeGlyphComponent component in compositeGlyph.Components)
            {
                componentIds.Add(component.GlyphIndex);

                // Recursively extract the component glyph
                GlyphOutline? componentOutline = ExtractGlyph(component.GlyphIndex);
                if (componentOutline == null || componentOutline.IsEmpty)
                    continue;

                // Transform each contour of the component using the transformation matrix
                foreach (GlyphContour contour in componentOutline.Contours)
                {
                    var transformedPoints = new List<ContourPoint>();

                    foreach (ContourPoint point in contour.Points)
                    {
                        // Apply transformation matrix and offset
                        double x = point.X * component.A + point.Y * component.C + component.Argument1;
                        double y = point.X * component.B + point.Y * component.D + component.Argument2;

                        transformedPoints.Add(new ContourPoint(
                            x,
                            y,
                            point.OnCurve
                        ));
                    }

                    allContours.Add(new GlyphContour(transformedPoints, contour.IsClosed));
                }
            }

            return new GlyphOutline(
                glyphId,
                allContours,
                metrics,
                isComposite: true,
                componentGlyphIds: componentIds
            );
        }

        /// <summary>
        /// Get number of glyphs in font
        /// </summary>
        public int GlyphCount => _numGlyphs;

        /// <summary>
        /// Get all glyph IDs that have outlines (non-empty)
        /// </summary>
        public IEnumerable<int> GetNonEmptyGlyphIds()
        {
            return _glyfTable.Glyphs.Select(g => g.Index);
        }
    }
}
