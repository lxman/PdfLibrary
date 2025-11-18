using System.Collections.Generic;
using FontParser.Reader;
using FontParser.Tables.Bitmap.Common;

namespace FontParser.Tables.Bitmap.Eblc
{
    public class EblcTable : IFontTable
    {
        public static string Tag => "EBLC";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public List<BitmapSize> BitmapSizes { get; } = new List<BitmapSize>();

        public EblcTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();

            uint numSizes = reader.ReadUInt32();
            for (var i = 0; i < numSizes; i++)
            {
                long bitmapSizeTableOffset = reader.Position;
                BitmapSizes.Add(new BitmapSize(reader));
                reader.Seek(bitmapSizeTableOffset + 48);
            }
            BitmapSizes.ForEach(bs => bs.LoadIndexSubtableList(reader));
        }
    }
}