using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintSolid : IPaintTable
    {
        public byte Format => 2;

        public ushort PaletteIndex { get; }

        public float Alpha { get; }

        public PaintSolid(BigEndianReader reader)
        {
            PaletteIndex = reader.ReadUShort();
            Alpha = reader.ReadF2Dot14();
        }
    }
}