using System;
using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.TtTables
{
    public class LocaTable : IFontTable
    {
        public static string Tag => "loca";

        public uint[] Offsets { get; private set; } = null!;

        private readonly BigEndianReader _reader;

        public LocaTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        // numGlyphs from maxp table
        // isShort from head table
        public void Process(int numGlyphs, bool isShort)
        {
            Offsets = new uint[numGlyphs + 1];
            for (var i = 0; i < numGlyphs + 1; i++)
            {
                if (isShort)
                {
                    Offsets[i] = Convert.ToUInt32(_reader.ReadUShort() * 2);
                }
                else
                {
                    Offsets[i] = _reader.ReadUInt32();
                }
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Loca Table");
            for (var i = 0; i < Offsets.Length; i++)
            {
                sb.AppendLine($"Offset {i}: {Offsets[i]}");
            }
            return sb.ToString();
        }
    }
}