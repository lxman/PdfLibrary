using FontParser.Reader;

namespace FontParser.Tables.Gpos
{
    public class GposHeader
    {
        public ushort MajorVersion { get; }

        public ushort MinorVersion { get; }

        public ushort ScriptListOffset { get; }

        public ushort FeatureListOffset { get; }

        public ushort LookupListOffset { get; }

        public uint FeatureVariationsOffset { get; }

        public GposHeader(BigEndianReader reader)
        {
            MajorVersion = reader.ReadUShort();
            MinorVersion = reader.ReadUShort();
            ScriptListOffset = reader.ReadUShort();
            FeatureListOffset = reader.ReadUShort();
            LookupListOffset = reader.ReadUShort();
            if (MinorVersion == 1)
            {
                FeatureVariationsOffset = reader.ReadUInt32();
            }
        }
    }
}