using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;
using FontParser.Tables.WOFF2.GlyfReconstruct;

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

        // TODO: figure out how to reconstruct the indexes
        public void Woff2Reconstruct(List<List<IGlyphInfo>> glyphs)
        {
            var index = 0;
            glyphs.ForEach(g =>
            {
                g.ForEach(gi =>
                {
                    var header = new GlyphHeader(Array.Empty<byte>());
                    IGlyphSpec spec;
                    switch (gi)
                    {
                        case CompositeGlyphInfo compositeGlyphInfo:
                            header.Woff2Reconstruct(
                                -1,
                                compositeGlyphInfo.XMin,
                                compositeGlyphInfo.YMin,
                                compositeGlyphInfo.XMax,
                                compositeGlyphInfo.YMax);
                            var compositeGlyph = new CompositeGlyph(new BigEndianReader(Array.Empty<byte>()), header, true);
                            compositeGlyph.Woff2Reconstruct(compositeGlyphInfo);
                            spec = compositeGlyph;
                            break;

                        case SimpleGlyphInfo simpleGlyphInfo:
                            header.Woff2Reconstruct(
                                Convert.ToInt16(simpleGlyphInfo.EndPointsOfContours.Count),
                                simpleGlyphInfo.XMin,
                                simpleGlyphInfo.YMin,
                                simpleGlyphInfo.XMax,
                                simpleGlyphInfo.YMax);
                            var simpleGlyph = new SimpleGlyph(new BigEndianReader(Array.Empty<byte>()), header, true);
                            simpleGlyph.Woff2Reconstruct(simpleGlyphInfo);
                            spec = simpleGlyph;
                            break;

                        default:
                            throw new ArgumentOutOfRangeException(nameof(gi));
                    }

                    Glyphs.Add(new GlyphData(index++, header, spec));
                });
            });
        }

        public GlyphData? GetGlyphData(int index)
        {
            return Glyphs.FirstOrDefault(g => g.Index == index);
        }
    }
}