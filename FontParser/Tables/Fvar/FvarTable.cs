using System.Collections.Generic;
using FontParser.Reader;

namespace FontParser.Tables.Fvar
{
    public class FvarTable : IFontTable
    {
        public static string Tag => "fvar";

        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public List<VariationAxisRecord> Axes { get; } = new List<VariationAxisRecord>();

        public List<InstanceRecord> Instances { get; } = new List<InstanceRecord>();

        public FvarTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            ushort axesArrayOffset = reader.ReadUShort();
            _ = reader.ReadUShort();
            ushort axisCount = reader.ReadUShort();
            ushort axisSize = reader.ReadUShort();
            ushort instanceCount = reader.ReadUShort();
            ushort instanceSize = reader.ReadUShort();
            reader.Seek(axesArrayOffset);
            for (var i = 0; i < axisCount; i++)
            {
                long start = reader.Position;
                Axes.Add(new VariationAxisRecord(reader));
                reader.Seek(start + axisSize);
            }

            for (var i = 0; i < instanceCount; i++)
            {
                long start = reader.Position;
                Instances.Add(new InstanceRecord(reader, axisCount, instanceSize));
                reader.Seek(start + instanceSize);
            }
        }
    }
}