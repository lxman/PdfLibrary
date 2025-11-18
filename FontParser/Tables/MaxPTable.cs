using FontParser.Reader;

namespace FontParser.Tables
{
    public class MaxPTable : IFontTable
    {
        public static string Tag => "maxp";

        public uint Version { get; }

        public ushort NumGlyphs { get; }

        public ushort MaxPoints { get; }

        public ushort MaxContours { get; }

        public ushort MaxCompositePoints { get; }

        public ushort MaxCompositeContours { get; }

        public ushort MaxZones { get; }

        public ushort MaxTwilightPoints { get; }

        public ushort MaxStorage { get; }

        public ushort MaxFunctionDefs { get; }

        public ushort MaxInstructionDefs { get; }

        public ushort MaxStackElements { get; }

        public ushort MaxSizeOfInstructions { get; }

        public ushort MaxComponentElements { get; }

        public ushort MaxComponentDepth { get; }

        public MaxPTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);
            Version = reader.ReadUInt32();
            NumGlyphs = reader.ReadUShort();
            if (Version == 0x00005000)
            {
                return;
            }
            MaxPoints = reader.ReadUShort();
            MaxContours = reader.ReadUShort();
            MaxCompositePoints = reader.ReadUShort();
            MaxCompositeContours = reader.ReadUShort();
            MaxZones = reader.ReadUShort();
            MaxTwilightPoints = reader.ReadUShort();
            MaxStorage = reader.ReadUShort();
            MaxFunctionDefs = reader.ReadUShort();
            MaxInstructionDefs = reader.ReadUShort();
            MaxStackElements = reader.ReadUShort();
            MaxSizeOfInstructions = reader.ReadUShort();
            MaxComponentElements = reader.ReadUShort();
            MaxComponentDepth = reader.ReadUShort();
        }
    }
}