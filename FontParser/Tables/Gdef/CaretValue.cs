using FontParser.Reader;

namespace FontParser.Tables.Gdef
{
    public class CaretValue
    {
        public ushort Format { get; }

        public short? Coordinate { get; }

        public ushort? CaretValuePointIndex { get; }

        public CaretValue(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Format = reader.ReadUShort();
            switch (Format)
            {
                case 1:
                    Coordinate = reader.ReadShort();
                    break;

                case 2:
                    CaretValuePointIndex = reader.ReadUShort();
                    break;
            }
        }
    }
}