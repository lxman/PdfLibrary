using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Vorg
{
    public class VorgTable : IFontTable
    {
        public static string Tag => "VORG";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public short DefaultVertOriginY { get; }

        public List<VertOriginYMetrics> VertOriginYMetrics { get; } = new List<VertOriginYMetrics>();

        public VorgTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            DefaultVertOriginY = reader.ReadShort();
            ushort numVertOriginYMetrics = reader.ReadUShort();
            for (var i = 0; i < numVertOriginYMetrics; i++)
            {
                VertOriginYMetrics.Add(new VertOriginYMetrics(reader));
            }
        }
    }
}