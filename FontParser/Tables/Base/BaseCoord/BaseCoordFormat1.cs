using FontParser.Reader;

namespace FontParser.Tables.Base.BaseCoord
{
    public class BaseCoordFormat1 : IBaseCoordFormat
    {
        public ushort BaseCoordFormat => 1;

        public short Coordinate { get; }

        public BaseCoordFormat1(BigEndianReader reader)
        {
            Coordinate = reader.ReadShort();
        }
    }
}