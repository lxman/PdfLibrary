namespace Jp2Codec.Jp2File
{
    /// <summary>
    /// Four-character box type codes used in JP2 file format (ISO/IEC 15444-1
    /// Annex I). Stored as 32-bit big-endian ASCII (e.g. 'jp2 ' = 0x6A703220).
    /// </summary>
    internal static class BoxType
    {
        // Top-level boxes required by JP2 conformance.
        public const uint JpSignature   = 0x6A502020;  // "jP  "
        public const uint FileType      = 0x66747970;  // "ftyp"
        public const uint Jp2Header     = 0x6A703268;  // "jp2h" (superbox)
        public const uint ContiguousCodestream = 0x6A703263;  // "jp2c"

        // Boxes inside jp2h.
        public const uint ImageHeader        = 0x69686472;  // "ihdr"
        public const uint BitsPerComponent   = 0x62706363;  // "bpcc"
        public const uint ColourSpecification = 0x636F6C72; // "colr"
        public const uint Palette            = 0x70636C72;  // "pclr"
        public const uint ComponentMapping   = 0x636D6170;  // "cmap"
        public const uint ChannelDefinition  = 0x63646566;  // "cdef"
        public const uint Resolution         = 0x72657320;  // "res "

        // Top-level optional boxes — recognised so the file parser can skip them.
        public const uint IntellectualProperty = 0x6A703269; // "jp2i"
        public const uint Xml                  = 0x786D6C20; // "xml "
        public const uint Uuid                 = 0x75756964; // "uuid"
        public const uint UuidInfo             = 0x75696E66; // "uinf"

        /// <summary>Major brand value for a Part 1 JP2 file (ftyp box).</summary>
        public const uint Jp2Brand = 0x6A703220; // "jp2 "

        /// <summary>Pretty-print a box type for diagnostic messages.</summary>
        public static string Format(uint type)
        {
            return new string(new[]
            {
                (char)((type >> 24) & 0xFF),
                (char)((type >> 16) & 0xFF),
                (char)((type >> 8) & 0xFF),
                (char)(type & 0xFF),
            });
        }
    }
}
