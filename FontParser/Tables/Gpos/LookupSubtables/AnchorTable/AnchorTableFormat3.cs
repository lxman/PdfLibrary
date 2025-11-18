using FontParser.Reader;
using FontParser.Tables.Common;

namespace FontParser.Tables.Gpos.LookupSubtables.AnchorTable
{
    public class AnchorTableFormat3 : IAnchorTable
    {
        public short X { get; }

        public short Y { get; }

        public DeviceTable? DeviceX { get; }

        public DeviceTable? DeviceY { get; }

        public AnchorTableFormat3(BigEndianReader reader)
        {
            // The tables can be a DeviceTable if this is a non-variable font
            // or a VariationIndexTable if this is a variable font.
            // I'm disabling the table creation for now until I can determine which it is.
            long startOfTable = reader.Position;
            _ = reader.ReadUShort();
            X = reader.ReadShort();
            Y = reader.ReadShort();
            ushort xDeviceOffset = reader.ReadUShort();
            ushort yDeviceOffset = reader.ReadUShort();
            return;
            long before = reader.Position;
            if (xDeviceOffset != 0)
            {
                reader.Seek(startOfTable + xDeviceOffset);
                DeviceX = new DeviceTable(reader);
            }

            if (yDeviceOffset == 0)
            {
                reader.Seek(before);
                return;
            }
            reader.Seek(startOfTable + yDeviceOffset);
            DeviceY = new DeviceTable(reader);
            reader.Seek(before);
        }
    }
}