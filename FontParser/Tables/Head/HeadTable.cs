using System;
using System.Text;
using FontParser.Extensions;
using FontParser.Reader;

namespace FontParser.Tables.Head
{
    public class HeadTable : IFontTable
    {
        public static string Tag => "head";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public string Version => $"{MajorVersion}.{MinorVersion}";

        public float FontRevision { get; }

        public uint CheckSumAdjustment { get; }

        public uint MagicNumber { get; }

        public HeadFlags Flags { get; }

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
            Created = reader.ReadLongDateTime().ToDateTime();
            Modified = reader.ReadLongDateTime().ToDateTime();
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

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.AppendLine("HeadTable");
            builder.AppendLine($"Version: {Version}");
            builder.AppendLine($"FontRevision: {FontRevision}");
            builder.AppendLine($"CheckSumAdjustment: {CheckSumAdjustment}");
            builder.AppendLine($"MagicNumber: {MagicNumber}");
            builder.AppendLine($"Flags: {Flags}");
            builder.AppendLine($"UnitsPerEm: {UnitsPerEm}");
            builder.AppendLine($"Created: {Created}");
            builder.AppendLine($"Modified: {Modified}");
            builder.AppendLine($"XMin: {XMin}");
            builder.AppendLine($"YMin: {YMin}");
            builder.AppendLine($"XMax: {XMax}");
            builder.AppendLine($"YMax: {YMax}");
            builder.AppendLine($"MacStyle: {MacStyle}");
            builder.AppendLine($"LowestRecPPEM: {LowestRecPpem}");
            builder.AppendLine($"FontDirectionHint: {FontDirectionHint}");
            builder.AppendLine($"IndexToLocFormat: {IndexToLocFormat}");
            builder.AppendLine($"GlyphDataFormat: {GlyphDataFormat}");
            return builder.ToString();
        }
    }
}