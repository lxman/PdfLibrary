using FontParser.Tables.Cmap.SubTables;

namespace FontParser.Tables.Cmap
{
    public class CmapEncoding
    {
        public EncodingRecord Encoding { get; set; }

        public ICmapSubtable SubTable { get; set; }

        public CmapEncoding(EncodingRecord encoding, ICmapSubtable subTable)
        {
            Encoding = encoding;
            SubTable = subTable;
        }
    }
}
