using System;
using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common.IndexSubtables
{
    public class IndexSubtablesFormat5 : IIndexSubtable
    {
        public ushort IndexFormat { get; }

        public ushort ImageFormat { get; }

        public uint ImageDataOffset { get; }

        public BigGlyphMetricsRecord BigMetrics { get; }

        public List<ushort> GlyphIds { get; }

        public IndexSubtablesFormat5(BigEndianReader reader)
        {
            IndexFormat = reader.ReadUShort();
            ImageFormat = reader.ReadUShort();
            ImageDataOffset = reader.ReadUInt32();
            uint numGlyphs = reader.ReadUInt32();
            BigMetrics = new BigGlyphMetricsRecord(reader);
            GlyphIds = reader.ReadUShortArray(Convert.ToUInt32(numGlyphs)).ToList();
        }
    }
}