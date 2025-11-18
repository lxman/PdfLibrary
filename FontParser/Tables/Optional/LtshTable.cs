using FontParser.Reader;

namespace FontParser.Tables.Optional
{
    public class LtshTable : IFontTable
    {
        public static string Tag => "LTSH";

        public ushort Version { get; private set; }

        public byte[] YPels { get; private set; } = null!;

        private readonly BigEndianReader _reader;

        public LtshTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numGlyphs: From the 'maxp' table.
        public void Process(ushort numGlyphs)
        {
            Version = _reader.ReadUShort();
            YPels = _reader.ReadBytes(numGlyphs);
        }
    }
}