using FontParser.Reader;
using FontParser.Tables.Common;
using FontParser.Tables.Common.ClassDefinition;

namespace FontParser.Tables.Gdef
{
    public class GdefTable : IFontTable
    {
        public static string Tag => "GDEF";

        public readonly GdefHeader Header;

        public readonly IClassDefinition? GlyphClassDef;

        public readonly AttachListTable? AttachListTable;

        public readonly LigCaretListTable? LigCaretListTable;

        public readonly IClassDefinition? MarkAttachClassDef;

        public readonly MarkGlyphSetsTable? MarkGlyphSetsTable;

        public readonly VariationIndexTable? ItemVarStore;

        public GdefTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Header = new GdefHeader(reader);
            if (Header.GlyphClassDefOffset.HasValue && Header.GlyphClassDefOffset > 0)
            {
                reader.Seek(Header.GlyphClassDefOffset ?? 0);
                byte[] format = reader.PeekBytes(2);
                if (format[1] == 1)
                {
                    GlyphClassDef = new ClassDefinitionFormat1(reader);
                }
                else
                {
                    GlyphClassDef = new ClassDefinitionFormat2(reader);
                }
            }

            if (Header.AttachListOffset.HasValue && Header.AttachListOffset > 0)
            {
                reader.Seek(Header.AttachListOffset ?? 0);
                AttachListTable = new AttachListTable(reader);
            }

            if (Header.LigCaretListOffset.HasValue && Header.LigCaretListOffset > 0)
            {
                reader.Seek(Header.LigCaretListOffset ?? 0);
                LigCaretListTable = new LigCaretListTable(reader);
            }

            if (Header.MarkAttachClassDefOffset.HasValue && Header.MarkAttachClassDefOffset > 0)
            {
                reader.Seek(Header.MarkAttachClassDefOffset ?? 0);
                byte[] format = reader.PeekBytes(2);
                if (format[1] == 1)
                {
                    MarkAttachClassDef = new ClassDefinitionFormat1(reader);
                }
                else
                {
                    MarkAttachClassDef = new ClassDefinitionFormat2(reader);
                }
            }

            if (Header.MarkGlyphSetsDefOffset.HasValue && Header.MarkGlyphSetsDefOffset > 0)
            {
                reader.Seek(Header.MarkGlyphSetsDefOffset ?? 0);
                MarkGlyphSetsTable = new MarkGlyphSetsTable(reader);
            }

            if (!Header.ItemVarStoreOffset.HasValue || !(Header.ItemVarStoreOffset > 0)) return;
            reader.Seek(Header.ItemVarStoreOffset ?? 0);
            ItemVarStore = new VariationIndexTable(reader);
        }
    }
}