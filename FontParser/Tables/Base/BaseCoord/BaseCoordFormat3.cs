using FontParser.Reader;

namespace FontParser.Tables.Base.BaseCoord
{
    public class BaseCoordFormat3 : IBaseCoordFormat
    {
        public ushort BaseCoordFormat => 3;

        public short Coordinate { get; }

        public ushort DeviceOffset { get; }

        public BaseCoordFormat3(BigEndianReader reader)
        {
            Coordinate = reader.ReadShort();
            DeviceOffset = reader.ReadUShort();
        }
    }
}