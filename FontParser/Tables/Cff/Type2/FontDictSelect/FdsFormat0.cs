using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Cff.Type2.FontDictSelect
{
    public class FdsFormat0 : IFdSelect
    {
        public byte Format { get; }

        public List<byte> FdIndex { get; } = new List<byte>();

        public FdsFormat0(BigEndianReader reader, ushort numGlyphs)
        {
            Format = reader.ReadByte();
            FdIndex.AddRange(reader.ReadBytes(numGlyphs));
        }
    }
}