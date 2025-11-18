using System.Text;

namespace PdfLibrary.Fonts.Embedded.Tables.Name
{
    /// <summary>
    /// Name record from TrueType 'name' table
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class NameRecord
    {
        public static long RecordSize => 12;

        public PlatformId PlatformId { get; }

        public Enum EncodingId { get; }

        public ushort LanguageId { get; }

        public string NameId { get; }

        public string? Name { get; set; }

        private readonly ushort _length;
        private readonly ushort _offset;

        public NameRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            PlatformId = (PlatformId)reader.ReadUShort();
            switch (PlatformId)
            {
                case PlatformId.Unicode:
                    EncodingId = (UnicodeEncodingId)reader.ReadUShort();
                    break;

                case PlatformId.Macintosh:
                    EncodingId = (MacintoshEncodingId)reader.ReadUShort();
                    break;

                case PlatformId.Iso:
                    EncodingId = (IsoEncodingId)reader.ReadUShort();
                    break;

                case PlatformId.Windows:
                    EncodingId = (WindowsEncodingId)reader.ReadUShort();
                    break;

                case PlatformId.Custom:
                    _ = reader.ReadUShort();
                    EncodingId = (UnicodeEncodingId)0; // Dummy value
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            LanguageId = reader.ReadUShort();
            NameId = NameIdTranslator.Translate(reader.ReadUShort());
            _length = reader.ReadUShort();
            _offset = reader.ReadUShort();
        }

        public void Process(BigEndianReader reader, ushort offset)
        {
            reader.Seek(offset + _offset);
            if (EncodingId.ToString().ToLower().Contains("unicode"))
            {
                Name = Encoding.BigEndianUnicode.GetString(reader.ReadBytes(_length));
                return;
            }
            Name = Encoding.ASCII.GetString(reader.ReadBytes(_length));
        }
    }
}
