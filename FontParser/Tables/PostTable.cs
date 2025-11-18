using System.Collections.Generic;
using System.Text;
using FontParser.Reader;

namespace FontParser.Tables
{
    public class PostTable : IFontTable
    {
        public static string Tag => "post";

        public ushort Version1 { get; }

        public ushort Version2 { get; }

        public string ItalicAngle { get; }

        public string UnderlinePosition { get; }

        public string UnderlineThickness { get; }

        public string IsFixedPitch { get; }

        public string MinMemType42 { get; }

        public string MaxMemType42 { get; }

        public uint MinMemType1 { get; }

        public uint MaxMemType1 { get; }

        public ushort NumGlyphs { get; }

        public List<ushort> GlyphNameIndex { get; } = new List<ushort>();

        public List<byte> GlyphNames { get; } = new List<byte>();

        public PostTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version1 = reader.ReadUShort();
            Version2 = reader.ReadUShort();
            ItalicAngle = $"{data[2]}.{data[3]}";
            UnderlinePosition = $"{data[4]}.{data[5]}";
            UnderlineThickness = $"{data[6]}.{data[7]}";
            IsFixedPitch = $"{data[8]}.{data[9]}";
            MinMemType42 = $"{data[10]}.{data[11]}";
            MaxMemType42 = $"{data[12]}.{data[13]}";
            reader.Seek(16);
            MinMemType1 = reader.ReadUInt32();
            MaxMemType1 = reader.ReadUInt32();
            if (Version1 != 2 || Version2 != 0) return;
            NumGlyphs = reader.ReadUShort();
            for (var i = 0; i < NumGlyphs; i++)
            {
                GlyphNameIndex.Add(reader.ReadUShort());
            }
            for (var i = 0; i < NumGlyphs; i++)
            {
                GlyphNames.Add(reader.ReadByte());
            }
        }

        public string GetJson()
        {
            var builder = new StringBuilder();
            builder.Append("{");
            builder.Append($"\"Version\": \"{Version1}.{Version2}\",");
            builder.Append($"\"ItalicAngle\": \"{ItalicAngle}\",");
            builder.Append($"\"UnderlinePosition\": \"{UnderlinePosition}\",");
            builder.Append($"\"UnderlineThickness\": \"{UnderlineThickness}\",");
            builder.Append($"\"IsFixedPitch\": \"{IsFixedPitch}\",");
            builder.Append($"\"MinMemType42\": \"{MinMemType42}\",");
            builder.Append($"\"MaxMemType42\": \"{MaxMemType42}\"");
            builder.Append("}");
            return builder.ToString();
        }
    }
}