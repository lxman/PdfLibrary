using System;
using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class DeltaSetIndexMap
    {
        public byte Format { get; }

        public byte EntryFormat { get; }

        public byte[] DeltaValues { get; }

        public DeltaSetIndexMap(BigEndianReader reader)
        {
            Format = reader.ReadByte();
            EntryFormat = reader.ReadByte();
            uint deltaValueCount = Format switch
            {
                0 => reader.ReadUShort(),
                1 => reader.ReadUInt32(),
                _ => throw new ArgumentOutOfRangeException()
            };
            DeltaValues = reader.ReadBytes(deltaValueCount);
        }
    }
}