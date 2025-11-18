using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Kerx.Subtables
{
    public class Format0 : IKerxSubtable
    {
        public uint Length { get; }

        public KerxCoverage Coverage { get; }

        public uint TupleCount { get; }

        public List<KerningPair> KerningPairs { get; } = new List<KerningPair>();

        public uint SearchRange { get; }

        public uint EntrySelector { get; }

        public uint RangeShift { get; }

        public Format0(BigEndianReader reader)
        {
            Length = reader.ReadUInt32();
            Coverage = (KerxCoverage)reader.ReadUInt32();
            TupleCount = reader.ReadUInt32();
            uint numPairs = reader.ReadUInt32();
            SearchRange = reader.ReadUInt32();
            EntrySelector = reader.ReadUInt32();
            RangeShift = reader.ReadUInt32();
            for (var i = 0; i < numPairs; i++)
            {
                KerningPairs.Add(new KerningPair(reader));
            }
        }
    }
}