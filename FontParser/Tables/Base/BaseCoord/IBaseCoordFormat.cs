namespace FontParser.Tables.Base.BaseCoord
{
    public interface IBaseCoordFormat
    {
        public ushort BaseCoordFormat { get; }

        public short Coordinate { get; }
    }
}