using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Kern
{
    public class KernSubtableFormat0 : IKernSubtable
    {
        public ushort Version { get; }

        public KernCoverage Coverage { get; }

        public List<KernPair> KernPairs { get; } = new List<KernPair>();

        private readonly BigEndianReader _reader;
        private readonly ushort _nPairs;

        public KernSubtableFormat0(BigEndianReader reader)
        {
            _reader = reader;
            Version = reader.ReadUShort();
            _ = reader.ReadUShort();
            Coverage = (KernCoverage)reader.ReadUShort();
            _nPairs = reader.ReadUShort();
            ushort searchRange = reader.ReadUShort();
            ushort entrySelector = reader.ReadUShort();
            ushort rangeShift = reader.ReadUShort();
            for (var i = 0; i < _nPairs; i++)
            {
                KernPairs.Add(new KernPair(reader));
            }
        }

        public void Process()
        {
        }
    }
}