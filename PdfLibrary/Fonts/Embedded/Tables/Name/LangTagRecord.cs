using System.Text;

namespace PdfLibrary.Fonts.Embedded.Tables.Name
{
    /// <summary>
    /// Language tag record from TrueType 'name' table (format 1)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class LangTagRecord
    {
        public static long RecordSize => 4;

        public string LanguageTag { get; private set; } = string.Empty;

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
