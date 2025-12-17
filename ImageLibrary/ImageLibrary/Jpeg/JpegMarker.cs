namespace ImageLibrary.Jpeg;

/// <summary>
/// JPEG marker codes as defined in ITU-T T.81.
/// </summary>
internal static class JpegMarker
{
    // Start/End of Image
    public const byte SOI = 0xD8;  // Start of image
    public const byte EOI = 0xD9;  // End of image

    // Start of Frame markers (baseline, progressive, etc.)
    public const byte SOF0 = 0xC0; // Baseline DCT
    public const byte SOF1 = 0xC1; // Extended sequential DCT
    public const byte SOF2 = 0xC2; // Progressive DCT
    public const byte SOF3 = 0xC3; // Lossless (sequential)

    // Huffman table definition
    public const byte DHT = 0xC4;

    // Arithmetic coding conditioning
    public const byte DAC = 0xCC;

    // Restart markers
    public const byte RST0 = 0xD0;
    public const byte RST1 = 0xD1;
    public const byte RST2 = 0xD2;
    public const byte RST3 = 0xD3;
    public const byte RST4 = 0xD4;
    public const byte RST5 = 0xD5;
    public const byte RST6 = 0xD6;
    public const byte RST7 = 0xD7;

    // Other markers
    public const byte DQT = 0xDB;  // Define quantization table
    public const byte DNL = 0xDC;  // Define number of lines
    public const byte DRI = 0xDD;  // Define restart interval
    public const byte DHP = 0xDE;  // Define hierarchical progression
    public const byte EXP = 0xDF;  // Expand reference component

    // Application markers
    public const byte APP0 = 0xE0;  // JFIF
    public const byte APP1 = 0xE1;  // EXIF
    public const byte APP2 = 0xE2;  // ICC Profile
    public const byte APP14 = 0xEE; // Adobe

    // Start of Scan
    public const byte SOS = 0xDA;

    // Comment
    public const byte COM = 0xFE;

    // Marker prefix
    public const byte Prefix = 0xFF;

    /// <summary>
    /// Gets a human-readable name for a marker.
    /// </summary>
    public static string GetName(byte marker) => marker switch
    {
        SOI => "SOI",
        EOI => "EOI",
        SOF0 => "SOF0 (Baseline DCT)",
        SOF1 => "SOF1 (Extended Sequential)",
        SOF2 => "SOF2 (Progressive DCT)",
        SOF3 => "SOF3 (Lossless)",
        DHT => "DHT",
        DQT => "DQT",
        DRI => "DRI",
        SOS => "SOS",
        APP0 => "APP0 (JFIF)",
        APP1 => "APP1 (EXIF)",
        APP2 => "APP2 (ICC)",
        APP14 => "APP14 (Adobe)",
        COM => "COM",
        >= RST0 and <= RST7 => $"RST{marker - RST0}",
        >= 0xE0 and <= 0xEF => $"APP{marker - 0xE0}",
        _ => $"Unknown (0x{marker:X2})"
    };

    /// <summary>
    /// Returns true if the marker is a standalone marker (no length field).
    /// </summary>
    public static bool IsStandalone(byte marker)
    {
        return marker == SOI || marker == EOI || (marker >= RST0 && marker <= RST7);
    }
}
