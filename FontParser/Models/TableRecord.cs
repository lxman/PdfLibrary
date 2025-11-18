using System;

namespace FontParser.Models
{
    public class TableRecord
    {
        public string Tag { get; set; } = string.Empty;

        public uint CheckSum { get; set; }

        public uint Offset { get; set; }

        public uint Length { get; set; }

        public byte[] Data { get; set; } = Array.Empty<byte>();
    }
}