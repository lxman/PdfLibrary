using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FontParser.Reader;

namespace FontParser.Tables.Cpal
{
    public class CpalTable : IFontTable
    {
        public static string Tag => "CPAL";

        public ushort Version { get; }

        public List<Color> Colors { get; } = new List<Color>();

        public List<PaletteType>? PaletteTypeArray { get; }

        public List<ushort>? PaletteLabelArray { get; }

        public List<ushort>? PaletteEntryLabelArray { get; }

        public CpalTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            Version = reader.ReadUShort();
            ushort numPaletteEntries = reader.ReadUShort();
            ushort numPalettes = reader.ReadUShort();
            ushort numColorRecords = reader.ReadUShort();
            uint offsetFirstColorRecord = reader.ReadUInt32();
            var paletteOffsets = new ushort[numPalettes];
            for (var i = 0; i < numPalettes; i++)
            {
                paletteOffsets[i] = reader.ReadUShort();
            }

            reader.Seek(offsetFirstColorRecord);
            for (var i = 0; i < numColorRecords; i++)
            {
                byte blue = reader.ReadByte();
                byte green = reader.ReadByte();
                byte red = reader.ReadByte();
                byte alpha = reader.ReadByte();
                Colors.Add(Color.FromArgb(alpha, red, green, blue));
            }

            if (Version == 0) return;
            uint offsetPaletteTypeArray = reader.ReadUInt32();
            uint offsetPaletteLabelArray = reader.ReadUInt32();
            uint offsetPaletteEntryLabelArray = reader.ReadUInt32();

            if (numPalettes > 0)
            {
                reader.Seek(offsetPaletteTypeArray);
                PaletteTypeArray = new List<PaletteType>();
                for (var i = 0; i < numPalettes; i++)
                {
                    PaletteTypeArray.Add((PaletteType)reader.ReadUInt32());
                }
            }

            if (numPalettes > 0)
            {
                reader.Seek(offsetPaletteLabelArray);
                PaletteLabelArray = reader.ReadUShortArray(numPalettes).ToList();
            }

            if (numPaletteEntries == 0) return;
            reader.Seek(offsetPaletteEntryLabelArray);
            PaletteEntryLabelArray = reader.ReadUShortArray(numPaletteEntries).ToList();
        }
    }
}