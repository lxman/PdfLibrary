using System;
using System.Text;

namespace PdfLibrary.Fonts.Embedded.Tables.TtTables
{
    /// <summary>
    /// 'loca' table - Index to location
    /// Provides offsets into the 'glyf' table for each glyph
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class LocaTable
    {
        public static string Tag => "loca";

        public uint[] Offsets { get; private set; } = null!;

        private readonly BigEndianReader _reader;

        public LocaTable(byte[] data)
        {
            _reader = new BigEndianReader(data);
        }

        /// <summary>
        /// Process the loca table data
        /// </summary>
        /// <param name="numGlyphs">Number of glyphs from maxp table</param>
        /// <param name="isShort">True for short format (from head.indexToLocFormat)</param>
        public void Process(int numGlyphs, bool isShort)
        {
            Offsets = new uint[numGlyphs + 1];
            for (var i = 0; i < numGlyphs + 1; i++)
            {
                if (isShort)
                {
                    // Short format: Actual offset is value * 2
                    Offsets[i] = Convert.ToUInt32(_reader.ReadUShort() * 2);
                }
                else
                {
                    // Long format: Direct 32-bit offsets
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
