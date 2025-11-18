using FontParser.Reader;

namespace FontParser.Tables.Gdef
{
    public class CaretValueFormatTable
    {
        public ushort Format { get; }

        public short? Coordinate { get; }

        public ushort? CaretValuePointIndex { get; }

        public ushort? DeviceTableOffset { get; }

        public CaretValueFormatTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Format = reader.ReadUShort();
            switch (Format)
            {
                case 1:
                    Coordinate = reader.ReadShort();
                    break;

                case 2:
                    Coordinate = reader.ReadShort();
                    CaretValuePointIndex = reader.ReadUShort();
                    break;

                case 3:
                    Coordinate = reader.ReadShort();
                    DeviceTableOffset = reader.ReadUShort();
                    break;

                default:
                    throw new System.Exception($"Unknown format: {Format}");
            }
        }
    }
}