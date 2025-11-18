using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Mvar
{
    public class MvarTable : IFontTable
    {
        public static string Tag => "MVAR";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort ValueRecordSize { get; }

        public ushort ValueRecordCount { get; }

        public ushort ItemVariationStoreOffset { get; }

        public List<ValueRecord> ValueRecords { get; } = new List<ValueRecord>();

        public MvarTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            _ = reader.ReadUShort();
            ValueRecordSize = reader.ReadUShort();
            ValueRecordCount = reader.ReadUShort();
            ItemVariationStoreOffset = reader.ReadUShort();
            for (var i = 0; i < ValueRecordCount; i++)
            {
                if (ValueRecordSize > 8)
                {
                    _ = reader.ReadBytes(ValueRecordSize - 8);
                }
                ValueRecords.Add(new ValueRecord(reader));
            }
        }
    }
}