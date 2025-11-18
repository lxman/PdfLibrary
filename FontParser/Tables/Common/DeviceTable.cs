using System;
using FontParser.Reader;

namespace FontParser.Tables.Common
{
    public class DeviceTable
    {
        public ushort StartSize { get; }
        public ushort EndSize { get; }
        public DeltaFormat DeltaFormat { get; }
        public ushort[] DeltaValues { get; }

        public DeviceTable(BigEndianReader reader)
        {
            StartSize = reader.ReadUShort();
            EndSize = reader.ReadUShort();
            DeltaFormat = (DeltaFormat)reader.ReadUShort();

            int deltaCount = EndSize - StartSize;
            DeltaValues = reader.ReadUShortArray(Convert.ToUInt32(deltaCount));
        }
    }
}