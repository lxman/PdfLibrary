namespace ImageLibrary.Jp2;

/// <summary>
/// JPEG2000 codestream marker constants.
/// </summary>
internal static class Jp2Markers
{
    // Delimiting markers (no parameters)
    public const ushort SOC = 0xFF4F;  // Start of codestream
    public const ushort SOT = 0xFF90;  // Start of tile-part
    public const ushort SOD = 0xFF93;  // Start of data (bitstream follows)
    public const ushort EOC = 0xFFD9;  // End of codestream

    // Fixed information segment markers
    public const ushort SIZ = 0xFF51;  // Image and tile size

    // Functional markers
    public const ushort COD = 0xFF52;  // Coding style default
    public const ushort COC = 0xFF53;  // Coding style component
    public const ushort RGN = 0xFF5E;  // Region of interest
    public const ushort QCD = 0xFF5C;  // Quantization default
    public const ushort QCC = 0xFF5D;  // Quantization component
    public const ushort POC = 0xFF5F;  // Progression order change

    // Pointer markers
    public const ushort TLM = 0xFF55;  // Tile-part lengths
    public const ushort PLM = 0xFF57;  // Packet length, main header
    public const ushort PLT = 0xFF58;  // Packet length, tile-part header
    public const ushort PPM = 0xFF60;  // Packed packet headers, main header
    public const ushort PPT = 0xFF61;  // Packed packet headers, tile-part header

    // Informational markers
    public const ushort CRG = 0xFF63;  // Component registration
    public const ushort COM = 0xFF64;  // Comment

    // In-bitstream markers
    public const ushort SOP = 0xFF91;  // Start of packet
    public const ushort EPH = 0xFF92;  // End of packet header

    /// <summary>
    /// Gets a human-readable name for a marker code.
    /// </summary>
    public static string GetName(ushort marker) => marker switch
    {
        SOC => "SOC",
        SOT => "SOT",
        SOD => "SOD",
        EOC => "EOC",
        SIZ => "SIZ",
        COD => "COD",
        COC => "COC",
        RGN => "RGN",
        QCD => "QCD",
        QCC => "QCC",
        POC => "POC",
        TLM => "TLM",
        PLM => "PLM",
        PLT => "PLT",
        PPM => "PPM",
        PPT => "PPT",
        CRG => "CRG",
        COM => "COM",
        SOP => "SOP",
        EPH => "EPH",
        _ => $"0x{marker:X4}"
    };
}