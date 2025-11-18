using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class VarColorStop
    {
        public float StopOffset { get; }

        public ushort PaletteIndex { get; }

        public float Alpha { get; }

        public uint VarIndexBase { get; }

        public VarColorStop(BigEndianReader reader)
        {
            StopOffset = reader.ReadF2Dot14();
            PaletteIndex = reader.ReadUShort();
            Alpha = reader.ReadF2Dot14();
            VarIndexBase = reader.ReadUInt32();
        }
    }
}