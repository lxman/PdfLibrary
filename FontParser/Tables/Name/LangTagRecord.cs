using System.Text;
using FontParser.Reader;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace FontParser.Tables.Name
{
    public class LangTagRecord
    {
        public static long RecordSize => 4;

        public string LanguageTag { get; private set; }

        private readonly ushort _length;
        private readonly ushort _offset;

        public LangTagRecord(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            _length = reader.ReadUShort();
            _offset = reader.ReadUShort();
        }

        public void Process(BigEndianReader reader, ushort offset)
        {
            reader.Seek(offset + _offset);
            LanguageTag = Encoding.ASCII.GetString(reader.ReadBytes(_length));
        }
    }
}