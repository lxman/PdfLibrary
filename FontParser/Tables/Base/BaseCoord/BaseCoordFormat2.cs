using FontParser.Reader;

namespace FontParser.Tables.Base.BaseCoord
{
    public class BaseCoordFormat2 : IBaseCoordFormat
    {
        public ushort BaseCoordFormat => 2;

        public short Coordinate { get; }

        public ushort ReferenceGlyph { get; }

        public ushort BaseCoordPoint { get; }

        public BaseCoordFormat2(BigEndianReader reader)
        {
            Coordinate = reader.ReadShort();
            ReferenceGlyph = reader.ReadUShort();
            BaseCoordPoint = reader.ReadUShort();
        }
    }
}