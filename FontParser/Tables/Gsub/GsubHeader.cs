using FontParser.Reader;

namespace FontParser.Tables.Gsub
{
    public class GsubHeader
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort ScriptListOffset { get; }

        public ushort FeatureListOffset { get; }

        public ushort LookupListOffset { get; }

        public ushort? FeatureVariationsOffset { get; }

        public GsubHeader(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            ScriptListOffset = reader.ReadUShort();
            FeatureListOffset = reader.ReadUShort();
            LookupListOffset = reader.ReadUShort();
            if (MinorVersion > 0)
            {
                FeatureVariationsOffset = reader.ReadUShort();
            }
        }
    }
}