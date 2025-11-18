using FontParser.Reader;

namespace FontParser.Tables.Optional.Hdmx
{
    public class HdmxRecord
    {
        public byte PixelSize { get; }

        public byte MaxWidth { get; }

        public byte[] Widths { get; }

        public HdmxRecord(BigEndianReader reader, ushort numGlyphs)
        {
            PixelSize = reader.ReadByte();
            MaxWidth = reader.ReadByte();
            Widths = reader.ReadBytes(numGlyphs);
        }
    }
}