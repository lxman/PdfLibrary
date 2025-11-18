using FontParser.Reader;

namespace FontParser.Tables.Colr
{
    public class BaseGlyphRecord
    {
        public ushort GlyphId { get; }

        public ushort FirstLayerIndex { get; }

        public ushort NumLayers { get; }

        public BaseGlyphRecord(BigEndianReader reader)
        {
            GlyphId = reader.ReadUShort();
            FirstLayerIndex = reader.ReadUShort();
            NumLayers = reader.ReadUShort();
        }
    }
}