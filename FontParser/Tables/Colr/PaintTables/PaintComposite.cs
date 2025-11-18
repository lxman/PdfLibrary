using FontParser.Reader;

namespace FontParser.Tables.Colr.PaintTables
{
    public class PaintComposite : IPaintTable
    {
        public byte Format => 32;

        public IPaintTable SourceTable { get; }

        public IPaintTable Backdrop { get; }

        public CompositeMode CompositeMode { get; }

        public PaintComposite(BigEndianReader reader)
        {
            long start = reader.Position - 1;
            uint sourceTableOffset = reader.ReadUInt24();
            CompositeMode = (CompositeMode)reader.ReadByte();
            uint subTableOffset = reader.ReadUInt24();
            SourceTable = PaintTableFactory.CreatePaintTable(reader, start + sourceTableOffset);
            Backdrop = PaintTableFactory.CreatePaintTable(reader, start + subTableOffset);
        }
    }
}