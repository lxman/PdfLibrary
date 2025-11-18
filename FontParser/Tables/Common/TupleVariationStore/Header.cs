using System;
using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Common.TupleVariationStore
{
    public class Header
    {
        public ushort? MajorVersion { get; }

        public ushort? MinorVersion { get; }

        public bool HasSharedPointNumbers { get; }

        public List<TupleVariationHeader> TupleVariationHeaders { get; } = new List<TupleVariationHeader>();

        public Header(BigEndianReader reader, ushort axisCount, bool isCvar)
        {
            long startOfTable = reader.Position;
            if (isCvar)
            {
                MajorVersion = reader.ReadUShort();
                MinorVersion = reader.ReadUShort();
            }
            ushort tupleVariationCount = reader.ReadUShort();
            HasSharedPointNumbers = Convert.ToBoolean(tupleVariationCount & 0x8000);
            int actualTupleVariationCount = tupleVariationCount & 0x0FFF;
            ushort dataOffset = reader.ReadUShort();
            for (var i = 0; i < actualTupleVariationCount; i++)
            {
                TupleVariationHeaders.Add(new TupleVariationHeader(reader, axisCount, startOfTable + dataOffset));
            }
        }
    }
}