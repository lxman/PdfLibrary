using FontParser.Reader;

namespace FontParser.Tables.Stat
{
    public class StatTable : IFontTable
    {
        public static string Tag => "STAT";

        private ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public DesignAxesArray DesignAxesArray { get; }

        public StatTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            ushort designAxisSize = reader.ReadUShort();
            ushort designAxisCount = reader.ReadUShort();
            uint designAxesOffset = reader.ReadUInt32();
            ushort axisValueCount = reader.ReadUShort();
            uint offsetToAxisValueOffsets = reader.ReadUInt32();
            uint elidedFallbackNameId = reader.ReadUShort();
            reader.Seek(0);
            DesignAxesArray =
                new DesignAxesArray(
                    reader,
                    designAxisSize,
                    designAxisCount,
                    axisValueCount,
                    designAxesOffset,
                    offsetToAxisValueOffsets);
        }
    }
}