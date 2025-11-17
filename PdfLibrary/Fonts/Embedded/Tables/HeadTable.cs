using System.Text;

namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'head' table parser - font header with critical metrics
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class HeadTable
    {
        public static string Tag => "head";

        public ushort MajorVersion { get; }
        public ushort MinorVersion { get; }
        public string Version => $"{MajorVersion}.{MinorVersion}";
        public float FontRevision { get; }
        public uint CheckSumAdjustment { get; }
        public uint MagicNumber { get; }
        public HeadFlags Flags { get; }

        /// <summary>
        /// Units per Em - CRITICAL for all metric calculations
        /// Typically 1000 (PostScript), 1024, or 2048
        /// </summary>
        public ushort UnitsPerEm { get; }

        public DateTime Created { get; }
        public DateTime Modified { get; }
        public short XMin { get; }
        public short YMin { get; }
        public short XMax { get; }
        public short YMax { get; }
        public MacStyle MacStyle { get; }
        public ushort LowestRecPpem { get; }
        public FontDirectionHint FontDirectionHint { get; }

        /// <summary>
        /// Index to location format - needed for parsing 'loca' table
        /// 0 = short offsets (Offset16)
        /// 1 = long offsets (Offset32)
        /// </summary>
        public IndexToLocFormat IndexToLocFormat { get; }

        public short GlyphDataFormat { get; }

        public HeadTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            FontRevision = reader.ReadF16Dot16();
            CheckSumAdjustment = reader.ReadUInt32();
            MagicNumber = reader.ReadUInt32();
            Flags = (HeadFlags)reader.ReadUShort();
            UnitsPerEm = reader.ReadUShort();
            Created = ToDateTime(reader.ReadLongDateTime());
            Modified = ToDateTime(reader.ReadLongDateTime());
            XMin = reader.ReadShort();
            YMin = reader.ReadShort();
            XMax = reader.ReadShort();
            YMax = reader.ReadShort();
            MacStyle = (MacStyle)reader.ReadUShort();
            LowestRecPpem = reader.ReadUShort();
            FontDirectionHint = (FontDirectionHint)reader.ReadShort();
            IndexToLocFormat = (IndexToLocFormat)reader.ReadShort();
            GlyphDataFormat = reader.ReadShort();
        }

        /// <summary>
        /// Convert TrueType long date time to .NET DateTime
        /// TrueType epoch: January 1, 1904 00:00:00 UTC
        /// </summary>
        private static DateTime ToDateTime(long value)
        {
            value = value & 0x00000000FFFFFFFF;
            var dateTime = new DateTime(1904, 1, 1);
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Utc).AddSeconds(value);
        }

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine("HeadTable");
            builder.AppendLine($"Version: {Version}");
            builder.AppendLine($"FontRevision: {FontRevision}");
            builder.AppendLine($"MagicNumber: 0x{MagicNumber:X8}");
            builder.AppendLine($"Flags: {Flags}");
            builder.AppendLine($"UnitsPerEm: {UnitsPerEm}");
            builder.AppendLine($"Created: {Created}");
            builder.AppendLine($"Modified: {Modified}");
            builder.AppendLine($"BBox: ({XMin}, {YMin}) to ({XMax}, {YMax})");
            builder.AppendLine($"MacStyle: {MacStyle}");
            builder.AppendLine($"LowestRecPPEM: {LowestRecPpem}");
            builder.AppendLine($"FontDirectionHint: {FontDirectionHint}");
            builder.AppendLine($"IndexToLocFormat: {IndexToLocFormat}");
            builder.AppendLine($"GlyphDataFormat: {GlyphDataFormat}");
            return builder.ToString();
        }
    }

    #region Enums

    [Flags]
    public enum HeadFlags : ushort
    {
        BaselineAtY0 = 1 << 0,
        LeftSidebearingAtX0 = 1 << 1,
        InstructionsDependOnPointSize = 1 << 2,
        ForcePpemToInteger = 1 << 3,
        InstructionsAlterAdvanceWidth = 1 << 4,
        UseIntegerScaling = 1 << 5,
        InstructionsAlterAdvanceHeight = 1 << 6,
        UseLinearMetrics = 1 << 7,
        UsePpem = 1 << 8,
        UseIntegerPpem = 1 << 9,
        UsePPem = 1 << 10,
        UseIntegerPPem = 1 << 11,
        UseDoubleShift = 1 << 12,
        UseFullHinting = 1 << 13,
        UseGridfit = 1 << 14,
        UseBitmaps = 1 << 15
    }

    [Flags]
    public enum MacStyle : ushort
    {
        Bold = 1 << 0,
        Italic = 1 << 1,
        Underline = 1 << 2,
        Outline = 1 << 3,
        Shadow = 1 << 4,
        Condensed = 1 << 5,
        Extended = 1 << 6
    }

    public enum FontDirectionHint : short
    {
        FullyMixed = 0,
        OnlyStrongLtr = 1,
        StrongLtrAndNeutral = 2,
        OnlyStrongRtl = -1,
        StrongRtlAndNeutral = -2
    }

    public enum IndexToLocFormat
    {
        Offset16,
        Offset32
    }

    // Platform identifiers for NameTable
    public enum PlatformId : ushort
    {
        Unicode = 0,
        Macintosh = 1,
        Iso = 2,
        Windows = 3,
        Custom = 4
    }

    public enum UnicodeEncodingId : ushort
    {
        Unicode1 = 0,
        Unicode11 = 1,
        Iso10646 = 2,
        Unicode20 = 3,
        Unicode21 = 4,
        Unicode22 = 5,
        Unicode30 = 6
    }

    public enum MacintoshEncodingId : ushort
    {
        Roman = 0,
        Japanese = 1,
        ChineseTraditional = 2,
        Korean = 3,
        Arabic = 4,
        Hebrew = 5,
        Greek = 6,
        Russian = 7,
        RSymbol = 8,
        Devanagari = 9,
        Gurmukhi = 10,
        Gujarati = 11,
        Oriya = 12,
        Bengali = 13,
        Tamil = 14,
        Telugu = 15,
        Kannada = 16,
        Malayalam = 17,
        Sinhalese = 18,
        Burmese = 19,
        Khmer = 20,
        Thai = 21,
        Laotian = 22,
        Georgian = 23,
        Armenian = 24,
        ChineseSimplified = 25,
        Tibetan = 26,
        Mongolian = 27,
        Geez = 28,
        Slavic = 29,
        Vietnamese = 30,
        Sindhi = 31,
        Uninterpreted = 32
    }

    public enum IsoEncodingId : ushort
    {
        Ascii7Bit = 0,
        Iso10646 = 1,
        Iso8859_1 = 2
    }

    public enum WindowsEncodingId : ushort
    {
        UnicodeCsm = 0,
        UnicodeBmp = 1,
        ShiftJis = 2,
        Prc = 3,
        Big5 = 4,
        Wansung = 5,
        Johab = 6,
        UnicodeUCS4 = 10
    }

    #endregion
}
