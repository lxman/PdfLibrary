using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Proprietary.Aat.Bdat.GlyphBitmap;

namespace FontParser.Tables.Proprietary.Aat.Bdat
{
    public class BdatTable : IFontTable
    {
        public static string Tag => "bdat";

        public uint Version { get; }

        public List<IGlyphBitmap> GlyphBitmaps { get; } = new List<IGlyphBitmap>();

        public BdatTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUInt32();
        }
    }
}