using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Aat.Bloc
{
    public class BlocTable : IFontTable
    {
        public static string Tag => "bloc";

        public uint Version { get; }

        public List<BitmapSizeTable> BitmapSizeTables { get; } = new List<BitmapSizeTable>();

        public BlocTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Version = reader.ReadUInt32();
            uint numBitmapSizeTables = reader.ReadUInt32();
            for (var i = 0; i < numBitmapSizeTables; i++)
            {
                BitmapSizeTables.Add(new BitmapSizeTable(reader));
            }

            var indexSubtables = new List<IndexSubtableArray>();
            BitmapSizeTables.ForEach(st =>
            {
                reader.Seek(st.IndexSubTableArrayOffset);
                for (var i = 0; i < st.NumberOfIndexSubTables; i++)
                {
                    indexSubtables.Add(new IndexSubtableArray(reader));
                }
            });
        }
    }
}