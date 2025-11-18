using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class ClipRecord
    {
        public ushort StartGlyphId { get; }

        public ushort EndGlyphId { get; }

        public ClipBox ClipBox { get; }

        public ClipRecord(BigEndianReader reader)
        {
            StartGlyphId = reader.ReadUShort();
            EndGlyphId = reader.ReadUShort();
            ClipBox = new ClipBox(reader);
        }
    }
}