using System.Collections.Generic;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Bitmap.Cbdt
{
    public class CbdtTable : IFontTable
    {
        public static string Tag => "CBDT";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public List<byte> Data { get; }

        public CbdtTable(byte[] data)
        {
            // TODO: Implement
            using var reader = new BigEndianReader(data);
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            Data = reader.ReadBytes(reader.BytesRemaining).ToList();
        }
    }
}