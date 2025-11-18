using System.Text;
using FontParser.Reader;

namespace FontParser.Tables.Todo.Graphite.Feat
{
    public class FeatureSpec
    {
        public string FeatureName { get; }

        public ushort SettingCount { get; }

        public FeatureSpec(BigEndianReader reader)
        {
            FeatureName = Encoding.ASCII.GetString(reader.ReadBytes(4));
            SettingCount = reader.ReadUShort();
            _ = reader.ReadUShort(); // Reserved
            uint settingOffset = reader.ReadUInt32();
            ushort flags = reader.ReadUShort();
            ushort nameIndex = reader.ReadUShort();
        }
    }
}