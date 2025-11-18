using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Proprietary.Pclt
{
    public class PcltTable : IFontTable
    {
        public static string Tag => "PCLT";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public string Typeface { get; }

        public string CharacterComplement { get; }

        public string Filename { get; }

        public PcltTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            uint fontNumber = reader.ReadUShort();
            ushort pitch = reader.ReadUShort();
            ushort xHeight = reader.ReadUShort();
            ushort style = reader.ReadUShort();
            ushort typeFamily = reader.ReadUShort();
            ushort capHeight = reader.ReadUShort();
            ushort symbolSet = reader.ReadUShort();
            byte[] typeface = reader.ReadBytes(16);
            Typeface = Encoding.ASCII.GetString(typeface);
            byte[] characterComplement = reader.ReadBytes(8);
            CharacterComplement = Encoding.ASCII.GetString(characterComplement);
            byte[] filename = reader.ReadBytes(6);
            Filename = Encoding.ASCII.GetString(filename);
            sbyte strokeWeight = reader.ReadSByte();
            sbyte widthType = reader.ReadSByte();
            byte serifStyle = reader.ReadByte();
            byte reserved = reader.ReadByte();
        }
    }
}