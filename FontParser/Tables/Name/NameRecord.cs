using System;
using System.Text;
using FontParser.Models;
using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Name
{
    public class NameRecord
    {
        public static long RecordSize => 12;

        public PlatformId PlatformId { get; }

        public Enum EncodingId { get; }

        public string LanguageId { get; }

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
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
            LanguageId = Language.Ids[reader.ReadUShort()];
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