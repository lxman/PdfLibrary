using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarSolid : IPaintTable
    {
        public byte Format => 3;

        public ushort PaletteIndex { get; }

        public float Alpha { get; }

        public uint VarIndexBase { get; }

        public PaintVarSolid(BigEndianReader reader)
        {
            PaletteIndex = reader.ReadUShort();
            Alpha = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();
        }
    }
}