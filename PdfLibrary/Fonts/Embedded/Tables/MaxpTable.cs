namespace PdfLibrary.Fonts.Embedded.Tables
{
    /// <summary>
    /// TrueType 'maxp' table parser - maximum profile (glyph count and other limits)
    /// Adapted from FontManager.NET for PdfLibrary use
    /// </summary>
    public class MaxpTable
    {
        public static string Tag => "maxp";

        public uint Version { get; }

        /// <summary>
        /// Number of glyphs in the font - CRITICAL for parsing 'hmtx' and 'loca' tables
        /// </summary>
        public ushort NumGlyphs { get; }

        // Version 1.0 fields (TrueType outlines) - 0 if version 0.5
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

        public MaxpTable(byte[] data)
        {
            using var reader = new BigEndianReader(data);

            // Read version as uint (0x00010000 = version 1.0, 0x00005000 = version 0.5)
            Version = reader.ReadUInt32();

            // Read number of glyphs (present in both version 0.5 and 1.0)
            NumGlyphs = reader.ReadUShort();

            // Version 0.5 (0x00005000) only has version and numGlyphs
            if (Version == 0x00005000)
            {
                return;
            }

            // Version 1.0 (0x00010000) has additional fields
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
