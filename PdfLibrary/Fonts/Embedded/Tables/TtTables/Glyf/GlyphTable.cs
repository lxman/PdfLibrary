using System.Collections.Generic;
using System.Linq;

namespace PdfLibrary.Fonts.Embedded.Tables.TtTables.Glyf
{
    /// <summary>
    /// 'glyf' table - Glyph data
    /// Contains TrueType glyph outline descriptions
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class GlyphTable
    {
        public static string Tag => "glyf";

        public List<GlyphData> Glyphs { get; private set; } = new List<GlyphData>();

        private readonly BigEndianReader _reader;

        public GlyphTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        /// <summary>
        /// Process the glyf table data using loca table offsets
        /// </summary>
        /// <param name="numGlyphs">Number of glyphs from maxp table</param>
        /// <param name="offsets">Glyph offsets from loca table</param>
        public void Process(int numGlyphs, LocaTable offsets)
        {
            var compositeOffsets = new List<int>();

            // First pass: Process simple glyphs
            for (var i = 0; i < numGlyphs; i++)
            {
                _reader.Seek(offsets.Offsets[i]);

                uint length = offsets.Offsets[i + 1] - offsets.Offsets[i];
                if (length == 0) continue; // Empty glyph

                var glyphHeader = new GlyphHeader(_reader.ReadBytes(GlyphHeader.RecordSize));

                if (glyphHeader.NumberOfContours >= 0)
                {
                    // Simple glyph
                    Glyphs.Add(new GlyphData(i, glyphHeader, new SimpleGlyph(_reader, glyphHeader)));
                }
                else
                {
                    // Composite glyph - defer to second pass
                    compositeOffsets.Add(i);
                }
            }

            // Second pass: Process composite glyphs
            compositeOffsets.ForEach(i =>
            {
                _reader.Seek(offsets.Offsets[i]);
                var glyphHeader = new GlyphHeader(_reader.ReadBytes(GlyphHeader.RecordSize));
                Glyphs.Add(new GlyphData(i, glyphHeader, new CompositeGlyph(_reader, glyphHeader)));
            });

            // Sort by glyph index
            Glyphs = Glyphs.OrderBy(g => g.Index).ToList();
        }

        /// <summary>
        /// Get glyph data by glyph index
        /// </summary>
        public GlyphData? GetGlyphData(int index)
        {
            return Glyphs.FirstOrDefault(g => g.Index == index);
        }
    }
}
