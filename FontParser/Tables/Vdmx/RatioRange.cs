using FontParser.Reader;

namespace FontParser.Tables.Vdmx
{
    public class RatioRange
    {
        public byte BCharSet { get; }

        public byte XRatio { get; }

        public byte YStartRatio { get; }

        public byte YEndRatio { get; }

        public RatioRange(BigEndianReader reader)
        {
            BCharSet = reader.ReadByte();
            XRatio = reader.ReadByte();
            YStartRatio = reader.ReadByte();
            YEndRatio = reader.ReadByte();
        }
    }
}