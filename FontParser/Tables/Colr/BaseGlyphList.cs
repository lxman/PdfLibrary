using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class BaseGlyphList
    {
        public List<BaseGlyphPaintRecord> BaseGlyphPaintRecords { get; } = new List<BaseGlyphPaintRecord>();

        public BaseGlyphList(BigEndianReader reader)
        {
            long start = reader.Position;
            uint baseGlyphCount = reader.ReadUInt32();
            for (var i = 0; i < baseGlyphCount; i++)
            {
                BaseGlyphPaintRecords.Add(new BaseGlyphPaintRecord(reader, start));
            }
        }
    }
}