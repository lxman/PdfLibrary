using FontParser.Reader;

namespace FontParser.Tables.Gdef
{
    public class GdefHeader
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort? GlyphClassDefOffset { get; }

        public ushort? AttachListOffset { get; }

        public ushort? LigCaretListOffset { get; }

        public ushort? MarkAttachClassDefOffset { get; }

        public ushort? MarkGlyphSetsDefOffset { get; }

        public ushort? ItemVarStoreOffset { get; }

        public GdefHeader(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            GlyphClassDefOffset = reader.ReadUShort();
            AttachListOffset = reader.ReadUShort();
            LigCaretListOffset = reader.ReadUShort();
            MarkAttachClassDefOffset = reader.ReadUShort();
            if (MinorVersion >= 2)
            {
                MarkGlyphSetsDefOffset = reader.ReadUShort();
            }

            if (MinorVersion > 2)
            {
                ItemVarStoreOffset = reader.ReadUShort();
            }
        }
    }
}