using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintVarTransform : IPaintTable
    {
        public byte Format => 13;

        public IPaintTable SubTable { get; }

        public VarAffine2X3 Transform { get; }

        public PaintVarTransform(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint paintOffset = reader.ReadUInt24();
            uint transformOffset = reader.ReadUInt24();
            SubTable = PaintTableFactory.CreatePaintTable(reader, start + paintOffset);
            reader.Seek(start + transformOffset);
            Transform = new VarAffine2X3(reader);
        }
    }
}