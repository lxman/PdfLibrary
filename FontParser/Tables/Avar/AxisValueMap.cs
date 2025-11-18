using FontParser.Reader;

namespace FontParser.Tables.Avar
{
    public class AxisValueMap
    {
        public float FromCoordinate { get; }

        public float ToCoordinate { get; }

        public AxisValueMap(BigEndianReader reader)
        {
            FromCoordinate = reader.ReadF2Dot14();
            ToCoordinate = reader.ReadF2Dot14();
        }
    }
}