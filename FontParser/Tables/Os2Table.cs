using System.Text;
using FontParser.Reader;
using FontParser.Tables.Proprietary.Panose;

namespace FontParser.Tables
{
    public class Os2Table : IFontTable
    {
        public static string Tag => "OS/2";

        public ushort Version { get; }

        public short XAvgCharWidth { get; }

        public UsWeightClass UsWeightClass { get; }

        public UsWidthClass UsWidthClass { get; }

        public FsType FsType { get; }

        public short YSubscriptXSize { get; }

        public short YSubscriptYSize { get; }

        public short YSubscriptXOffset { get; }

        public short YSubscriptYOffset { get; }

        public short YSuperscriptXSize { get; }

        public short YSuperscriptYSize { get; }

        public short YSuperscriptXOffset { get; }

        public short YSuperscriptYOffset { get; }

        public short YStrikeoutSize { get; }

        public short YStrikeoutPosition { get; }

        public short SFamilyClass { get; }

        public PanoseInterpreter Panose { get; }

        public uint UlUnicodeRange1 { get; }

        public uint UlUnicodeRange2 { get; }

        public uint UlUnicodeRange3 { get; }

        public uint UlUnicodeRange4 { get; }

        public string AchVendId { get; }

        public ushort FsSelection { get; }

        public ushort UsFirstCharIndex { get; }

        public ushort UsLastCharIndex { get; }

        public short STypoAscender { get; }

        public short STypoDescender { get; }

        public short STypoLineGap { get; }

        public short SWinAscent { get; }

        public short SWinDescent { get; }

        public uint? UlCodePageRange1 { get; }

        public uint? UlCodePageRange2 { get; }

        public short? SxHeight { get; }

        public short? SCapHeight { get; }

        public ushort? UsDefaultChar { get; }

        public ushort? UsBreakChar { get; }

        public ushort? UsMaxContext { get; }

        public ushort? UsLowerOpticalPointSize { get; }

        public ushort? UsUpperOpticalPointSize { get; }

        public Os2Table(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUShort();
            XAvgCharWidth = reader.ReadShort();
            UsWeightClass = (UsWeightClass)reader.ReadUShort();
            UsWidthClass = (UsWidthClass)reader.ReadUShort();
            FsType = (FsType)reader.ReadUShort();
            YSubscriptXSize = reader.ReadShort();
            YSubscriptYSize = reader.ReadShort();
            YSubscriptXOffset = reader.ReadShort();
            YSubscriptYOffset = reader.ReadShort();
            YSuperscriptXSize = reader.ReadShort();
            YSuperscriptYSize = reader.ReadShort();
            YSuperscriptXOffset = reader.ReadShort();
            YSuperscriptYOffset = reader.ReadShort();
            YStrikeoutSize = reader.ReadShort();
            YStrikeoutPosition = reader.ReadShort();
            SFamilyClass = reader.ReadShort();
            Panose = new PanoseInterpreter(reader.ReadBytes(10));
            UlUnicodeRange1 = reader.ReadUInt32();
            UlUnicodeRange2 = reader.ReadUInt32();
            UlUnicodeRange3 = reader.ReadUInt32();
            UlUnicodeRange4 = reader.ReadUInt32();
            AchVendId = Encoding.ASCII.GetString(data[58..62]);
            FsSelection = reader.ReadUShort();
            UsFirstCharIndex = reader.ReadUShort();
            UsLastCharIndex = reader.ReadUShort();
            STypoAscender = reader.ReadShort();
            STypoDescender = reader.ReadShort();
            STypoLineGap = reader.ReadShort();
            SWinAscent = reader.ReadShort();
            SWinDescent = reader.ReadShort();
            if (Version > 0)
            {
                UlCodePageRange1 = reader.ReadUInt32();
                UlCodePageRange2 = reader.ReadUInt32();
            }
            if (Version > 1)
            {
                SxHeight = reader.ReadShort();
                SCapHeight = reader.ReadShort();
                UsDefaultChar = reader.ReadUShort();
                UsBreakChar = reader.ReadUShort();
                UsMaxContext = reader.ReadUShort();
            }

            if (Version <= 2) return;
            UsLowerOpticalPointSize = reader.ReadUShort();
            UsUpperOpticalPointSize = reader.ReadUShort();
        }
    }
}