using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Common
{
    public class SbitLineMetrics
    {
        public sbyte Ascender { get; }

        public sbyte Descender { get; }

        public byte WidthMax { get; }

        public sbyte CaretSlopeNumerator { get; }

        public sbyte CaretSlopeDenominator { get; }

        public sbyte CaretOffset { get; }

        public sbyte MinOriginSB { get; }

        public sbyte MinAdvanceSB { get; }

        public sbyte MaxBeforeBL { get; }

        public sbyte MinAfterBL { get; }

        public sbyte Pad1 { get; }

        public sbyte Pad2 { get; }

        public SbitLineMetrics(BigEndianReader reader)
        {
            Ascender = reader.ReadSByte();
            Descender = reader.ReadSByte();
            WidthMax = reader.ReadByte();
            CaretSlopeNumerator = reader.ReadSByte();
            CaretSlopeDenominator = reader.ReadSByte();
            CaretOffset = reader.ReadSByte();
            MinOriginSB = reader.ReadSByte();
            MinAdvanceSB = reader.ReadSByte();
            MaxBeforeBL = reader.ReadSByte();
            MinAfterBL = reader.ReadSByte();
            Pad1 = reader.ReadSByte();
            Pad2 = reader.ReadSByte();
        }
    }
}