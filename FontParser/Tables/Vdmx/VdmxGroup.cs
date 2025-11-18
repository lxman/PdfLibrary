using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Vdmx
{
    public class VdmxGroup
    {
        public byte StartSize { get; }

        public byte EndSize { get; }

        public List<VTable> Records { get; } = new List<VTable>();

        public VdmxGroup(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            ushort recordCount = reader.ReadUShort();
            StartSize = reader.ReadByte();
            EndSize = reader.ReadByte();

            for (var i = 0; i < recordCount; i++)
            {
                Records.Add(new VTable(reader));
            }
        }
    }
}