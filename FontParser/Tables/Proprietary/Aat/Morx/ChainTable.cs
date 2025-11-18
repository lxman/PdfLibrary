using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Morx
{
    public class ChainTable
    {
        public uint DefaultFlags { get; }

        public uint ChainLength { get; }

        public uint NFeatures { get; }

        public uint NSubtables { get; }

        public ChainTable(BigEndianReader reader)
        {
            DefaultFlags = reader.ReadUInt32();
            ChainLength = reader.ReadUInt32();
            NFeatures = reader.ReadUInt32();
            NSubtables = reader.ReadUInt32();
        }
    }
}