using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class ColorStop
    {
        public float StopOffset { get; }

        public ushort PaletteIndex { get; }

        public float Alpha { get; }

        public ColorStop(BigEndianReader reader)
        {
            StopOffset = reader.ReadF2Dot14();
            PaletteIndex = reader.ReadUShort();
            Alpha = reader.ReadF2Dot14();
        }
    }
}