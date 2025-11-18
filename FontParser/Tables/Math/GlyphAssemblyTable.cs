using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Math
{
    public class GlyphAssemblyTable
    {
        public MathValueRecord ItalicCorrection { get; }

        public List<GlyphPartRecord> GlyphPartRecords { get; } = new List<GlyphPartRecord>();

        public GlyphAssemblyTable(BigEndianReader reader)
        {
            ItalicCorrection = new MathValueRecord(reader, reader.Position);

            ushort partCount = reader.ReadUShort();

            for (var i = 0; i < partCount; i++)
            {
                GlyphPartRecords.Add(new GlyphPartRecord(reader));
            }
        }
    }
}