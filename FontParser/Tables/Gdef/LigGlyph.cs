using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Gdef
{
    public class LigGlyph
    {
        public ushort CaretCount { get; }

        public ushort[] CaretValue { get; }

        public List<CaretValueFormatTable> CaretValueFormatTables { get; } = new List<CaretValueFormatTable>();

        public LigGlyph(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            CaretCount = reader.ReadUShort();
            CaretValue = new ushort[CaretCount];
            for (var i = 0; i < CaretCount; i++)
            {
                CaretValue[i] = reader.ReadUShort();
            }

            foreach (ushort pointer in CaretValue)
            {
                CaretValueFormatTables.Add(new CaretValueFormatTable(data[pointer..]));
            }
        }
    }
}