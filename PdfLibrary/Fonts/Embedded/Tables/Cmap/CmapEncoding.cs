using PdfLibrary.Fonts.Embedded.Tables.Cmap.SubTables;

namespace PdfLibrary.Fonts.Embedded.Tables.Cmap
{
    /// <summary>
    /// Represents a cmap encoding (platform/encoding + subtable)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
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
