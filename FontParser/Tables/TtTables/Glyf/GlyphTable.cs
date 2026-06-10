using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.TtTables.Glyf
{
    public class GlyphTable : IFontTable
    {
        public static string Tag => "glyf";

        public List<GlyphData> Glyphs { get; private set; } = new List<GlyphData>();

        private readonly BigEndianReader _reader;

        public GlyphTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numGlyphs from maxp table
        // offsets from loca table
        public void Process(int numGlyphs, LocaTable offsets)
        {
            var compositeOffsets = new List<int>();
            for (var i = 0; i < numGlyphs; i++)
            {
                _reader.Seek(offsets.Offsets[i]);

                uint length = offsets.Offsets[i + 1] - offsets.Offsets[i];
                if (length == 0) continue;
                var glyphHeader = new GlyphHeader(_reader.ReadBytes(GlyphHeader.RecordSize));

                if (glyphHeader.NumberOfContours >= 0)
                {
                    Glyphs.Add(new GlyphData(i, glyphHeader, new SimpleGlyph(_reader, glyphHeader)));
                }
                else
                {
                    compositeOffsets.Add(i);
                }
            }
            compositeOffsets.ForEach(i =>
            {
                _reader.Seek(offsets.Offsets[i]);
                var glyphHeader = new GlyphHeader(_reader.ReadBytes(GlyphHeader.RecordSize));
                Glyphs.Add(new GlyphData(i, glyphHeader, new CompositeGlyph(_reader, glyphHeader)));
            });
            Glyphs = Glyphs.OrderBy(g => g.Index).ToList();
        }

        public GlyphData? GetGlyphData(int index)
        {
            return Glyphs.FirstOrDefault(g => g.Index == index);
        }
    }
}